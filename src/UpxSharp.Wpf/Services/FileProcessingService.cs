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
                ProcessingResult result = await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ReportProgress(file, 15, fileIndex, fileList.Count, onProgress, onOverallProgress);

                    if (request.Options.Command == UpxCommand.Test)
                        return TestRoundTrip(file.FilePath, request.Options, file, onProgress, cancellationToken);

                    string outputPath = ResolveOutputPath(file.FilePath, request);
                    bool replaceOriginal = request.OutputMode == FileOutputMode.ReplaceOriginal;
                    partialOutputPath = CreateTemporaryPathBeside(outputPath);

                    ReportProgress(file, 35, fileIndex, fileList.Count, onProgress, onOverallProgress);
                    ProcessFile(
                        file.FilePath,
                        partialOutputPath,
                        request.Options,
                        cancellationToken);

                    RejectGrownCompressedOutput(
                        file.FilePath,
                        partialOutputPath,
                        request.Options);

                    cancellationToken.ThrowIfCancellationRequested();
                    ReportProgress(file, 90, fileIndex, fileList.Count, onProgress, onOverallProgress);

                    if (replaceOriginal)
                    {
                        ReplaceOriginalSafely(
                            partialOutputPath,
                            file.FilePath,
                            request.Options.KeepBackup);
                        partialOutputPath = null;
                        outputPath = file.FilePath;
                    }
                    else
                    {
                        if (request.Options.KeepBackup)
                            CreateBackupCopy(file.FilePath);

                        File.Move(partialOutputPath, outputPath, overwrite: true);
                        partialOutputPath = null;
                    }

                    return new ProcessingResult(outputPath, new FileInfo(outputPath).Length);
                }, cancellationToken);

                stopwatch.Stop();
                file.CompressedSize = result.ResultSize;
                file.Status = FileProcessingStatus.Done;
                ReportProgress(file, 100, fileIndex, fileList.Count, onProgress, onOverallProgress);

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
        FileItemViewModel file,
        Action<FileItemViewModel, double> onProgress,
        CancellationToken cancellationToken)
    {
        string packedPath = Path.Combine(
            Path.GetTempPath(), $"upxsharp-test-{Guid.NewGuid():N}.upx");
        string unpackedPath = Path.Combine(
            Path.GetTempPath(), $"upxsharp-test-{Guid.NewGuid():N}.unpacked");

        try
        {
            var packOptions = CopyOptions(options, UpxCommand.Compress);
            ProcessFile(inputPath, packedPath, packOptions, cancellationToken);
            long packedSize = new FileInfo(packedPath).Length;
            onProgress(file, 55);

            var unpackOptions = CopyOptions(options, UpxCommand.Decompress);
            ProcessFile(packedPath, unpackedPath, unpackOptions, cancellationToken);
            onProgress(file, 85);

            cancellationToken.ThrowIfCancellationRequested();
            if (!FilesAreEqual(inputPath, unpackedPath))
                throw new CorruptedPackException(
                    "Teste falhou: arquivo restaurado difere do original.");

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

    private sealed record ProcessingResult(string OutputPath, long ResultSize);
}
