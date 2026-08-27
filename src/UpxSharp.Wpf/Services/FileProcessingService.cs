using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UpxSharp.Core;
using UpxSharp.IO;
using UpxSharp.Packers;
using UpxSharp.Wpf.ViewModels;

namespace UpxSharp.Wpf.Services;

public sealed class FileProcessingService : IFileProcessingService
{
    private readonly Action<ShellChangeNotification> _notifyShellChanged;

    public FileProcessingService()
        : this(NotifyShellChanged)
    {
    }

    public FileProcessingService(Action<ShellChangeNotification> notifyShellChanged)
        => _notifyShellChanged = notifyShellChanged;

    public async Task ProcessAsync(
        IEnumerable<FileItemViewModel> files,
        FileProcessingRequest request,
        Action<FileItemViewModel, double> onProgress,
        Action<double> onOverallProgress,
        Action<string, LogLevel> onLog,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);

        var fileList = files.ToList();
        onOverallProgress(0);

        for (int fileIndex = 0; fileIndex < fileList.Count; fileIndex++)
        {
            var file = fileList[fileIndex];
            cancellationToken.ThrowIfCancellationRequested();

            file.Status = FileProcessingStatus.Processing;
            file.Progress = 0;
            file.ErrorMessage = string.Empty;
            ReportProgress(file, 0, fileIndex, fileList.Count, onProgress, onOverallProgress);

            var stopwatch = Stopwatch.StartNew();
            string? partialOutputPath = null;

            try
            {
                void ReportCurrentProgress(double currentPercent)
                    => ReportProgress(file, currentPercent, fileIndex, fileList.Count, onProgress, onOverallProgress);

                ProcessingResult result = await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ReportCurrentProgress(10);

                    if (request.Options.Command == UpxCommand.Test)
                        return TestRoundTrip(file.FilePath, request.Options, ReportCurrentProgress, cancellationToken);

                    string outputPath = ResolveOutputPath(file.FilePath, request);
                    bool replaceOriginal = request.OutputMode == FileOutputMode.ReplaceOriginal;
                    partialOutputPath = CreateTemporaryPathBeside(outputPath);

                    ReportCurrentProgress(25);
                    RunWithEstimatedProgress(
                        () => ProcessFile(
                            file.FilePath,
                            partialOutputPath,
                            request.Options,
                            cancellationToken),
                        ReportCurrentProgress,
                        35,
                        84,
                        cancellationToken);

                    RejectGrownCompressedOutput(
                        file.FilePath,
                        partialOutputPath,
                        request.Options);

                    cancellationToken.ThrowIfCancellationRequested();
                    ReportCurrentProgress(85);

                    string? backupPath = request.Options.KeepBackup ? file.FilePath + ".bak" : null;

                    if (replaceOriginal)
                    {
                        ReportCurrentProgress(request.Options.KeepBackup ? 88 : 92);
                        ReplaceOriginalSafely(
                            partialOutputPath,
                            file.FilePath,
                            request.Options.KeepBackup);
                        partialOutputPath = null;
                        outputPath = file.FilePath;
                        ReportCurrentProgress(95);
                    }
                    else
                    {
                        if (request.Options.KeepBackup)
                        {
                            CreateBackupCopy(file.FilePath);
                            ReportCurrentProgress(90);
                        }

                        ReportCurrentProgress(93);
                        File.Move(partialOutputPath, outputPath, overwrite: true);
                        partialOutputPath = null;
                        ReportCurrentProgress(95);
                    }

                    RefreshFileSystemMetadata(file.FilePath, outputPath, backupPath);
                    ReportCurrentProgress(96);
                    NotifyProcessedPaths(file.FilePath, outputPath, backupPath);
                    ReportCurrentProgress(99);

                    return new ProcessingResult(outputPath, new FileInfo(outputPath).Length);
                }, cancellationToken);

                stopwatch.Stop();
                file.CompressedSize = result.ResultSize;
                file.Status = FileProcessingStatus.Done;

                string action = request.Options.Command switch
                {
                    UpxCommand.Decompress => "descomprimido",
                    UpxCommand.Test => "testado",
                    _ => "comprimido",
                };

                onLog(
                    $"✔ {file.FileName}: {action}, {file.OriginalSizeFormatted} → " +
                    $"{file.CompressedSizeFormatted} ({file.RatioFormatted}) em " +
                    $"{stopwatch.Elapsed.TotalSeconds:F1}s. Saída: {result.OutputPath}",
                    LogLevel.Success);

                ReportCurrentProgress(100);
            }
            catch (OperationCanceledException)
            {
                DeletePartialOutput(partialOutputPath);
                file.Status = FileProcessingStatus.Skipped;
                onLog($"⊘ {file.FileName}: cancelado pelo usuário", LogLevel.Warning);
                throw;
            }
            catch (Exception ex) when (ex is UpxException
                                       or IOException
                                       or UnauthorizedAccessException
                                       or ArgumentException)
            {
                DeletePartialOutput(partialOutputPath);
                stopwatch.Stop();
                file.Status = FileProcessingStatus.Error;
                file.ErrorMessage = ex.Message;
                file.Progress = 0;
                onOverallProgress((fileIndex + 1) * 100d / fileList.Count);
                onLog($"✘ {file.FileName}: {ex.Message}", LogLevel.Error);
            }
        }
    }

    private static void ReportProgress(
        FileItemViewModel file,
        double currentPercent,
        int fileIndex,
        int fileCount,
        Action<FileItemViewModel, double> onProgress,
        Action<double> onOverallProgress)
    {
        onProgress(file, currentPercent);
        onOverallProgress((fileIndex + currentPercent / 100d) * 100d / fileCount);
    }

    private static void ValidateRequest(FileProcessingRequest request)
    {
        if (request.OutputMode != FileOutputMode.CustomDirectory)
            return;

        if (string.IsNullOrWhiteSpace(request.CustomOutputDirectory))
            throw new ArgumentException("Selecione um diretório de saída.");

        if (!Directory.Exists(request.CustomOutputDirectory))
            throw new DirectoryNotFoundException(
                $"Diretório de saída não encontrado: {request.CustomOutputDirectory}");
    }

    private static ProcessingResult TestRoundTrip(
        string inputPath,
        Options options,
        Action<double> reportProgress,
        CancellationToken cancellationToken)
    {
        string packedPath = Path.Combine(
            Path.GetTempPath(), $"upxsharp-test-{Guid.NewGuid():N}.upx");
        string unpackedPath = Path.Combine(
            Path.GetTempPath(), $"upxsharp-test-{Guid.NewGuid():N}.unpacked");

        try
        {
            var packOptions = CopyOptions(options, UpxCommand.Compress);
            RunWithEstimatedProgress(
                () => ProcessFile(inputPath, packedPath, packOptions, cancellationToken),
                reportProgress,
                20,
                50,
                cancellationToken);
            long packedSize = new FileInfo(packedPath).Length;
            reportProgress(52);

            var unpackOptions = CopyOptions(options, UpxCommand.Decompress);
            RunWithEstimatedProgress(
                () => ProcessFile(packedPath, unpackedPath, unpackOptions, cancellationToken),
                reportProgress,
                58,
                84,
                cancellationToken);
            reportProgress(86);

            cancellationToken.ThrowIfCancellationRequested();
            if (!FilesAreEqual(inputPath, unpackedPath))
                throw new CorruptedPackException(
                    "Teste falhou: arquivo restaurado difere do original.");

            reportProgress(99);
            return new ProcessingResult("Teste concluído sem criar saída", packedSize);
        }
        finally
        {
            DeletePartialOutput(packedPath);
            DeletePartialOutput(unpackedPath);
        }
    }

    private static void ProcessFile(
        string inputPath,
        string outputPath,
        Options options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (IsWindowsPe(inputPath) && TryProcessWithOfficialUpx(inputPath, outputPath, options, cancellationToken))
            return;

        using var input = new InputFile(inputPath);
        using var output = new OutputFile(outputPath, overwrite: true);

        var packer = options.Command == UpxCommand.Decompress
            ? PackMaster.FindUnpacker(input, options)
            : PackMaster.FindPacker(input, options);

        cancellationToken.ThrowIfCancellationRequested();
        if (options.Command == UpxCommand.Decompress)
            packer.DoUnpack(output, options);
        else
            packer.DoPack(output, options);

        output.Flush();
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static void RunWithEstimatedProgress(
        Action action,
        Action<double> reportProgress,
        double startPercent,
        double endPercent,
        CancellationToken cancellationToken)
    {
        using var finished = new ManualResetEventSlim();
        Exception? pumpException = null;

        reportProgress(startPercent);
        reportProgress(Math.Min(startPercent + 5, endPercent));

        Task pump = Task.Run(() =>
        {
            try
            {
                double current = Math.Min(startPercent + 8, endPercent);
                while (!finished.Wait(150))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    reportProgress(current);
                    double remaining = endPercent - current;
                    if (remaining > 1)
                        current += Math.Max(0.5, remaining * 0.2);
                }
            }
            catch (Exception ex)
            {
                pumpException = ex;
            }
        }, CancellationToken.None);

        try
        {
            action();
        }
        finally
        {
            finished.Set();
            pump.GetAwaiter().GetResult();
        }

        if (pumpException is not null)
            throw pumpException;

        cancellationToken.ThrowIfCancellationRequested();
        reportProgress(endPercent);
    }

    private static bool TryProcessWithOfficialUpx(
        string inputPath,
        string outputPath,
        Options options,
        CancellationToken cancellationToken)
    {
        string? upxPath = FindOfficialUpxPath();
        if (upxPath is null)
            return false;

        RunOfficialUpx(upxPath, inputPath, outputPath, options, cancellationToken);
        return true;
    }

    private static void RunOfficialUpx(
        string upxPath,
        string inputPath,
        string outputPath,
        Options options,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "--no-progress",
            "--force-overwrite",
        };

        if (options.Command == UpxCommand.Decompress)
        {
            arguments.Add("-d");
        }
        else
        {
            if (options.Method == CompressionMethod.Lzma)
                arguments.Add("--lzma");

            if (options.Level > 0)
                arguments.Add($"-{options.Level}");

            arguments.Add("-f");
        }

        arguments.Add("-o");
        arguments.Add(outputPath);
        arguments.Add(inputPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = upxPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new IOException("Não foi possível iniciar o UPX oficial.");

        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        cancellationToken.ThrowIfCancellationRequested();

        if (process.ExitCode == 0)
        {
            if (options.Command == UpxCommand.Compress)
                EnsureWindowsCanLoadPackedPe(outputPath);
            return;
        }

        string message = string.Join(
            Environment.NewLine,
            new[] { standardError, standardOutput }
                .Where(static text => !string.IsNullOrWhiteSpace(text)))
            .Trim();

        if (string.IsNullOrWhiteSpace(message))
            message = $"UPX oficial retornou código {process.ExitCode}.";

        throw new CantPackException(message);
    }

    private static void EnsureWindowsCanLoadPackedPe(string outputPath)
    {
        if (GetBinaryType(outputPath, out _))
            return;

        throw new CantPackException(
            "executável compactado não é válido para esta plataforma; arquivo não foi gerado.");
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetBinaryType(
        string lpApplicationName,
        out uint lpBinaryType);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(
        uint wEventId,
        uint uFlags,
        string? dwItem1,
        string? dwItem2);

    private static bool IsWindowsPe(string path)
    {
        Span<byte> header = stackalloc byte[64];
        using var stream = File.OpenRead(path);
        if (stream.Length < header.Length)
            return false;

        stream.ReadExactly(header);
        if (header[0] != (byte)'M' || header[1] != (byte)'Z')
            return false;

        int peOffset = BitConverter.ToInt32(header[60..64]);
        if (peOffset < 64 || peOffset + 4 > stream.Length)
            return false;

        stream.Position = peOffset;
        Span<byte> peMagic = stackalloc byte[4];
        stream.ReadExactly(peMagic);
        return peMagic.SequenceEqual("PE\0\0"u8);
    }

    private static string? FindOfficialUpxPath()
    {
        if (string.Equals(
                Environment.GetEnvironmentVariable("UPXSHARP_DISABLE_OFFICIAL_UPX"),
                "1",
                StringComparison.Ordinal))
        {
            return null;
        }

        string? pathFromEnvironment = Environment.GetEnvironmentVariable("UPXSHARP_UPX_PATH");
        if (File.Exists(pathFromEnvironment))
            return pathFromEnvironment;

        string? pathFromCommand = FindOnPath("upx.exe");
        if (pathFromCommand is not null)
            return pathFromCommand;

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string toolsDirectory = Path.Combine(directory.FullName, "tools", "upx");
            if (Directory.Exists(toolsDirectory))
            {
                string? bundled = Directory
                    .EnumerateFiles(toolsDirectory, "upx.exe", SearchOption.AllDirectories)
                    .OrderByDescending(static path => path, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (bundled is not null)
                    return bundled;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string? FindOnPath(string fileName)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return null;

        foreach (string directory in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
                continue;

            string candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static void RejectGrownCompressedOutput(
        string inputPath,
        string outputPath,
        Options options)
    {
        if (options.Command != UpxCommand.Compress)
            return;

        long inputSize = new FileInfo(inputPath).Length;
        long outputSize = new FileInfo(outputPath).Length;
        if (outputSize < inputSize)
            return;

        throw new CantPackException(
            $"resultado não ficou menor ({FormatBytes(outputSize)} >= {FormatBytes(inputSize)}); arquivo não foi gerado.");
    }

    private static string FormatBytes(long bytes)
        => bytes == 1 ? "1 byte" : $"{bytes} bytes";

    private static string ResolveOutputPath(string inputPath, FileProcessingRequest request)
    {
        string suffix = request.Options.Command == UpxCommand.Decompress
            ? ".unpacked"
            : ".upx";

        if (request.OutputMode == FileOutputMode.ReplaceOriginal)
            return inputPath;

        string outputDirectory = request.OutputMode == FileOutputMode.CustomDirectory
            ? Path.GetFullPath(request.CustomOutputDirectory!)
            : Path.GetDirectoryName(inputPath)!;

        return Path.Combine(
            outputDirectory,
            Path.GetFileNameWithoutExtension(inputPath) + suffix);
    }

    private static string CreateTemporaryPathBeside(string inputPath)
    {
        string directory = Path.GetDirectoryName(inputPath)!;
        string fileName = Path.GetFileName(inputPath);
        return Path.Combine(directory, $".{fileName}.{Guid.NewGuid():N}.tmp");
    }

    private void NotifyProcessedPaths(string originalPath, string outputPath, string? backupPath)
    {
        NotifyFileChangedBestEffort(originalPath);
        NotifyFileChangedBestEffort(outputPath);

        if (!string.IsNullOrWhiteSpace(backupPath))
            NotifyFileChangedBestEffort(backupPath);

        string? originalDirectory = Path.GetDirectoryName(originalPath);
        string? outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(originalDirectory))
            NotifyDirectoryChangedBestEffort(originalDirectory);
        if (!string.IsNullOrWhiteSpace(outputDirectory) &&
            !string.Equals(originalDirectory, outputDirectory, StringComparison.OrdinalIgnoreCase))
        {
            NotifyDirectoryChangedBestEffort(outputDirectory);
        }

        NotifyShellChangedBestEffort(new ShellChangeNotification(
            null,
            ShellChangeEvent.AssociationChanged));
    }

    private void NotifyFileChangedBestEffort(string path)
    {
        NotifyShellChangedBestEffort(new ShellChangeNotification(
            path,
            ShellChangeEvent.UpdateItem));
        NotifyShellChangedBestEffort(new ShellChangeNotification(
            path,
            ShellChangeEvent.Attributes));
    }

    private void NotifyDirectoryChangedBestEffort(string path)
        => NotifyShellChangedBestEffort(new ShellChangeNotification(
            path,
            ShellChangeEvent.UpdateDir));

    private void NotifyShellChangedBestEffort(ShellChangeNotification notification)
    {
        try
        {
            _notifyShellChanged(notification);
        }
        catch
        {
            // Explorer refresh is best-effort and must not fail processing.
        }
    }

    private static void RefreshFileSystemMetadata(string originalPath, string outputPath, string? backupPath)
    {
        RefreshPathBestEffort(originalPath);
        RefreshPathBestEffort(outputPath);

        if (!string.IsNullOrWhiteSpace(backupPath))
            RefreshPathBestEffort(backupPath);

        string? originalDirectory = Path.GetDirectoryName(originalPath);
        string? outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(originalDirectory))
            RefreshPathBestEffort(originalDirectory);
        if (!string.IsNullOrWhiteSpace(outputDirectory) &&
            !string.Equals(originalDirectory, outputDirectory, StringComparison.OrdinalIgnoreCase))
        {
            RefreshPathBestEffort(outputDirectory);
        }
    }

    private static void RefreshPathBestEffort(string path)
    {
        try
        {
            if (Directory.Exists(path))
                new DirectoryInfo(path).Refresh();
            else
                new FileInfo(path).Refresh();
        }
        catch
        {
            // Metadata refresh is best-effort before Explorer notification.
        }
    }

    private static void ReplaceOriginalSafely(
        string temporaryPath,
        string originalPath,
        bool keepBackup)
    {
        string? backupPath = keepBackup ? originalPath + ".bak" : null;
        string? previousBackupPath = null;

        if (backupPath is not null && File.Exists(backupPath))
        {
            previousBackupPath = backupPath + $".{Guid.NewGuid():N}.previous";
            File.Move(backupPath, previousBackupPath);
        }

        try
        {
            File.Replace(
                temporaryPath,
                originalPath,
                backupPath,
                ignoreMetadataErrors: true);

            if (keepBackup && (backupPath is null || !File.Exists(backupPath)))
                throw new IOException("Não foi possível criar o backup do original.");

            if (previousBackupPath is not null)
                DeletePartialOutput(previousBackupPath);
        }
        catch
        {
            if (previousBackupPath is not null &&
                backupPath is not null &&
                File.Exists(previousBackupPath) &&
                !File.Exists(backupPath))
            {
                File.Move(previousBackupPath, backupPath);
            }

            throw;
        }
    }

    private static void CreateBackupCopy(string originalPath)
    {
        string backupPath = originalPath + ".bak";
        string temporaryBackupPath = backupPath + $".{Guid.NewGuid():N}.tmp";

        try
        {
            File.Copy(originalPath, temporaryBackupPath, overwrite: true);
            File.Move(temporaryBackupPath, backupPath, overwrite: true);
        }
        finally
        {
            DeletePartialOutput(temporaryBackupPath);
        }
    }

    private static bool FilesAreEqual(string firstPath, string secondPath)
    {
        var firstInfo = new FileInfo(firstPath);
        var secondInfo = new FileInfo(secondPath);
        if (firstInfo.Length != secondInfo.Length)
            return false;

        using var first = File.OpenRead(firstPath);
        using var second = File.OpenRead(secondPath);
        Span<byte> firstBuffer = stackalloc byte[8192];
        Span<byte> secondBuffer = stackalloc byte[8192];

        while (true)
        {
            int firstRead = first.Read(firstBuffer);
            int secondRead = second.Read(secondBuffer);
            if (firstRead != secondRead)
                return false;
            if (firstRead == 0)
                return true;
            if (!firstBuffer[..firstRead].SequenceEqual(secondBuffer[..secondRead]))
                return false;
        }
    }

    private static Options CopyOptions(Options source, UpxCommand command) => new()
    {
        Command = command,
        Method = source.Method,
        Level = source.Level,
        Verbose = source.Verbose,
        Force = source.Force,
        KeepBackup = source.KeepBackup,
        TestAfterPack = source.TestAfterPack,
        ListOnly = source.ListOnly,
        UseFilters = source.UseFilters,
        MaxFilter = source.MaxFilter,
        Threads = source.Threads,
    };

    private static void DeletePartialOutput(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Limpeza de melhor esforço; erro original deve ser preservado.
        }
    }

    private static void NotifyShellChanged(ShellChangeNotification notification)
    {
        const uint shcneUpdateItem = 0x00002000;
        const uint shcneUpdateDir = 0x00001000;
        const uint shcneAttributes = 0x00000800;
        const uint shcneAssocChanged = 0x08000000;
        const uint shcnfPathW = 0x0005;
        const uint shcnfIdList = 0x0000;
        const uint shcnfFlush = 0x1000;

        uint eventId = notification.Event switch
        {
            ShellChangeEvent.UpdateItem => shcneUpdateItem,
            ShellChangeEvent.UpdateDir => shcneUpdateDir,
            ShellChangeEvent.Attributes => shcneAttributes,
            ShellChangeEvent.AssociationChanged => shcneAssocChanged,
            _ => shcneUpdateItem,
        };

        uint flags = notification.Path is null
            ? shcnfIdList | shcnfFlush
            : shcnfPathW | shcnfFlush;

        SHChangeNotify(eventId, flags, notification.Path, null);
    }

    private sealed record ProcessingResult(string OutputPath, long ResultSize);
}

public sealed record ShellChangeNotification(string? Path, ShellChangeEvent Event);

public enum ShellChangeEvent
{
    UpdateItem,
    Attributes,
    UpdateDir,
    AssociationChanged,
}
