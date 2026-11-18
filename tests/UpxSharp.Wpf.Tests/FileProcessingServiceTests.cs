using System.Buffers.Binary;
using System.Text;
using UpxSharp.Core;
using UpxSharp.Wpf.Services;
using UpxSharp.Wpf.ViewModels;
using Xunit;

namespace UpxSharp.Wpf.Tests;

public sealed class FileProcessingServiceTests
{
    public FileProcessingServiceTests()
    {
        Environment.SetEnvironmentVariable("UPXSHARP_DISABLE_OFFICIAL_UPX", "1");
    }

    [Fact]
    public async Task Compress_WithSuffix_CreatesOutputAndPreservesOriginal()
    {
        using var directory = new TempDirectory();
        string inputPath = directory.CreateFile("sample.exe", CreateSyntheticPe(0x014C));
        byte[] original = File.ReadAllBytes(inputPath);
        var file = CreateFileItem(inputPath);

        await ProcessAsync(
            [file],
            CreateRequest(
                UpxCommand.Compress,
                FileOutputMode.SameDirectoryWithSuffix,
                force: true));

        Assert.Equal(FileProcessingStatus.Done, file.Status);
        Assert.Equal(100, file.Progress);
        Assert.Equal(original, File.ReadAllBytes(inputPath));
        Assert.True(File.Exists(Path.Combine(directory.Path, "sample.upx")));
    }

    [Fact]
    public async Task BatchProgress_ReportsCurrentAndOverallProgress()
    {
        using var directory = new TempDirectory();
        var first = CreateFileItem(directory.CreateFile("first.exe", CreateSyntheticPe(0x014C)));
        var second = CreateFileItem(directory.CreateFile("second.exe", CreateSyntheticPe(0x014C)));
        var current = new List<double>();
        var overall = new List<double>();

        var service = new FileProcessingService();
        await service.ProcessAsync(
            [first, second],
            CreateRequest(UpxCommand.Compress, FileOutputMode.SameDirectoryWithSuffix, force: true),
            (file, progress) =>
            {
                file.Progress = progress;
                current.Add(progress);
            },
            overall.Add,
            (_, _) => { },
            CancellationToken.None);

        Assert.Equal(100, first.Progress);
        Assert.Equal(100, second.Progress);
        Assert.Equal(100, overall[^1]);
        Assert.Contains(current, value => value is > 35 and < 85);
        Assert.Contains(overall, value => value is > 0 and < 50);
        Assert.Contains(overall, value => value is > 50 and < 100);
    }

    [Fact]
    public async Task TestRoundTrip_ReportsCurrentAndOverallIntermediateProgress()
    {
        using var directory = new TempDirectory();
        var file = CreateFileItem(directory.CreateFile("sample.com", CreateDosCom()));
        var current = new List<double>();
        var overall = new List<double>();

        var service = new FileProcessingService();
        await service.ProcessAsync(
            [file],
            CreateRequest(UpxCommand.Test, FileOutputMode.SameDirectoryWithSuffix),
            (_, progress) => current.Add(progress),
            overall.Add,
            (_, _) => { },
            CancellationToken.None);

        Assert.Equal(100, current[^1]);
        Assert.Equal(100, overall[^1]);
        Assert.Contains(current, value => value is > 20 and < 55);
        Assert.Contains(current, value => value is > 58 and < 88);
        Assert.Contains(overall, value => value is > 20 and < 55);
        Assert.Contains(overall, value => value is > 58 and < 88);
    }

    [Fact]
    public async Task Compress_WithSuffixAndBackup_CreatesBackupBesideOriginal()
    {
        using var directory = new TempDirectory();
        string inputPath = directory.CreateFile("sample.exe", CreateSyntheticPe(0x014C));
        byte[] original = File.ReadAllBytes(inputPath);

        await ProcessAsync(
            [CreateFileItem(inputPath)],
            CreateRequest(
                UpxCommand.Compress,
                FileOutputMode.SameDirectoryWithSuffix,
                force: true,
                keepBackup: true));

        Assert.Equal(original, File.ReadAllBytes(inputPath + ".bak"));
        Assert.True(File.Exists(Path.Combine(directory.Path, "sample.upx")));
    }

    [Fact]
    public async Task Compress_ToCustomDirectoryAndBackup_CreatesBackupBesideOriginal()
    {
        using var directory = new TempDirectory();
        string inputPath = directory.CreateFile("sample.exe", CreateSyntheticPe(0x014C));
        byte[] original = File.ReadAllBytes(inputPath);
        string outputDirectory = Directory.CreateDirectory(Path.Combine(directory.Path, "out")).FullName;

        await ProcessAsync(
            [CreateFileItem(inputPath)],
            CreateRequest(
                UpxCommand.Compress,
                FileOutputMode.CustomDirectory,
                outputDirectory,
                force: true,
                keepBackup: true));

        Assert.Equal(original, File.ReadAllBytes(inputPath + ".bak"));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "sample.upx")));
    }

    [Fact]
    public async Task Compress_WithBackup_NotifiesExplorerAfterSuccessfulOutputAndBackup()
    {
        using var directory = new TempDirectory();
        string inputPath = directory.CreateFile("sample.exe", CreateSyntheticPe(0x014C));
        string outputPath = Path.Combine(directory.Path, "sample.upx");
        string backupPath = inputPath + ".bak";
        var notifications = new List<ShellChangeNotification>();

        var service = new FileProcessingService(notifications.Add);
        await service.ProcessAsync(
            [CreateFileItem(inputPath)],
            CreateRequest(
                UpxCommand.Compress,
                FileOutputMode.SameDirectoryWithSuffix,
                force: true,
                keepBackup: true),
            (_, _) => { },
            _ => { },
            (_, _) => { },
            CancellationToken.None);

        Assert.Contains(notifications, notification =>
            notification.Path == inputPath && notification.Event == ShellChangeEvent.UpdateItem);
        Assert.Contains(notifications, notification =>
            notification.Path == inputPath && notification.Event == ShellChangeEvent.Attributes);
        Assert.Contains(notifications, notification =>
            notification.Path == outputPath && notification.Event == ShellChangeEvent.UpdateItem);
        Assert.Contains(notifications, notification =>
            notification.Path == backupPath && notification.Event == ShellChangeEvent.UpdateItem);
        Assert.Contains(notifications, notification =>
            notification.Path == directory.Path && notification.Event == ShellChangeEvent.UpdateDir);
        Assert.Contains(notifications, notification =>
            notification.Path is null && notification.Event == ShellChangeEvent.AssociationChanged);
    }

    [Fact]
    public async Task Decompress_ToCustomDirectory_RestoresOriginal()
    {
        using var directory = new TempDirectory();
        string inputPath = directory.CreateFile("sample.exe", CreateSyntheticPe(0x014C));
        byte[] original = File.ReadAllBytes(inputPath);
        var packedItem = CreateFileItem(inputPath);

        await ProcessAsync(
            [packedItem],
            CreateRequest(
                UpxCommand.Compress,
                FileOutputMode.SameDirectoryWithSuffix,
                force: true));

        string packedPath = Path.Combine(directory.Path, "sample.upx");
        string customDirectory = Directory.CreateDirectory(
            Path.Combine(directory.Path, "output")).FullName;
        var unpackItem = CreateFileItem(packedPath);

        await ProcessAsync(
            [unpackItem],
            CreateRequest(
                UpxCommand.Decompress,
                FileOutputMode.CustomDirectory,
                customDirectory));

        string unpackedPath = Path.Combine(customDirectory, "sample.unpacked");
        Assert.Equal(FileProcessingStatus.Done, unpackItem.Status);
        Assert.Equal(original, File.ReadAllBytes(unpackedPath));
    }

    [Fact]
    public async Task ReplaceOriginal_WithBackup_ReplacesAtomicallyAndKeepsOriginalBytes()
    {
        using var directory = new TempDirectory();
        string inputPath = directory.CreateFile("sample.exe", CreateSyntheticPe(0x8664));
        byte[] original = File.ReadAllBytes(inputPath);
        var file = CreateFileItem(inputPath);

        await ProcessAsync(
            [file],
            CreateRequest(
                UpxCommand.Compress,
                FileOutputMode.ReplaceOriginal,
                force: true,
                keepBackup: true));

        Assert.Equal(FileProcessingStatus.Done, file.Status);
        Assert.NotEqual(original, File.ReadAllBytes(inputPath));
        Assert.Equal(original, File.ReadAllBytes(inputPath + ".bak"));
    }

    [Fact]
    public async Task Test_RoundTrip_PreservesInputAndCreatesNoOutput()
    {
        using var directory = new TempDirectory();
        string inputPath = directory.CreateFile("sample.com", CreateDosCom());
        byte[] original = File.ReadAllBytes(inputPath);
        var file = CreateFileItem(inputPath);

        await ProcessAsync(
            [file],
            CreateRequest(UpxCommand.Test, FileOutputMode.SameDirectoryWithSuffix));

        Assert.Equal(FileProcessingStatus.Done, file.Status);
        Assert.Equal(original, File.ReadAllBytes(inputPath));
        Assert.False(File.Exists(Path.Combine(directory.Path, "sample.upx")));
        Assert.False(File.Exists(Path.Combine(directory.Path, "sample.unpacked")));
    }

    [Fact]
    public async Task InvalidFile_DoesNotStopRemainingFiles()
    {
        using var directory = new TempDirectory();
        string invalidPath = directory.CreateFile("invalid.bin", [1, 2, 3, 4]);
        string validPath = directory.CreateFile("valid.exe", CreateSyntheticPe(0x014C));
        var invalid = CreateFileItem(invalidPath);
        var valid = CreateFileItem(validPath);

        await ProcessAsync(
            [invalid, valid],
            CreateRequest(
                UpxCommand.Compress,
                FileOutputMode.SameDirectoryWithSuffix,
                force: true));

        Assert.Equal(FileProcessingStatus.Error, invalid.Status);
        Assert.Equal(FileProcessingStatus.Done, valid.Status);
        Assert.True(File.Exists(Path.Combine(directory.Path, "valid.upx")));
    }

    [Fact]
    public async Task Compress_WhenOutputWouldGrowWithoutForce_MarksErrorAndPreservesOriginal()
    {
        using var directory = new TempDirectory();
        string inputPath = directory.CreateFile("sample.com", CreateDosCom());
        byte[] original = File.ReadAllBytes(inputPath);
        var file = CreateFileItem(inputPath);
        var logs = new List<string>();
        var service = new FileProcessingService();

        await service.ProcessAsync(
            [file],
            CreateRequest(UpxCommand.Compress, FileOutputMode.ReplaceOriginal),
            (item, progress) => item.Progress = progress,
            _ => { },
            (message, _) => logs.Add(message),
            CancellationToken.None);

        Assert.Equal(FileProcessingStatus.Error, file.Status);
        Assert.Equal(original, File.ReadAllBytes(inputPath));
        Assert.DoesNotContain(Directory.EnumerateFiles(directory.Path), path => path.EndsWith(".tmp"));
        Assert.Contains(logs, message => message.Contains("resultado não ficou menor"));
    }

    [Fact]
    public async Task Compress_WhenOutputFails_DoesNotNotifyExplorerForFinalOutput()
    {
        using var directory = new TempDirectory();
        string inputPath = directory.CreateFile("sample.com", CreateDosCom());
        var notifications = new List<ShellChangeNotification>();
        var service = new FileProcessingService(notifications.Add);

        await service.ProcessAsync(
            [CreateFileItem(inputPath)],
            CreateRequest(UpxCommand.Compress, FileOutputMode.ReplaceOriginal),
            (_, _) => { },
            _ => { },
            (_, _) => { },
            CancellationToken.None);

        Assert.Empty(notifications);
    }

    [Fact]
    public async Task CustomDirectory_WhenMissing_FailsBeforeProcessing()
    {
        using var directory = new TempDirectory();
        string inputPath = directory.CreateFile("sample.com", CreateDosCom());
        var file = CreateFileItem(inputPath);
        string missing = Path.Combine(directory.Path, "missing");

        var service = new FileProcessingService();
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            service.ProcessAsync(
                [file],
                CreateRequest(
                    UpxCommand.Compress,
                    FileOutputMode.CustomDirectory,
                missing),
                (_, _) => { },
                _ => { },
                (_, _) => { },
                CancellationToken.None));

        Assert.Equal(FileProcessingStatus.Pending, file.Status);
    }

    [Fact]
    public async Task Cancellation_MarksCurrentFileSkippedAndStopsBatch()
    {
        using var directory = new TempDirectory();
        string firstPath = directory.CreateFile("first.com", CreateDosCom());
        string secondPath = directory.CreateFile("second.com", CreateDosCom());
        var first = CreateFileItem(firstPath);
        var second = CreateFileItem(secondPath);
        using var cancellation = new CancellationTokenSource();
        var service = new FileProcessingService();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.ProcessAsync(
                [first, second],
                CreateRequest(
                    UpxCommand.Compress,
                    FileOutputMode.SameDirectoryWithSuffix),
                (_, progress) =>
                {
                    if (progress >= 35)
                        cancellation.Cancel();
                },
                _ => { },
                (_, _) => { },
                cancellation.Token));

        Assert.Equal(FileProcessingStatus.Skipped, first.Status);
        Assert.Equal(FileProcessingStatus.Pending, second.Status);
        Assert.False(File.Exists(Path.Combine(directory.Path, "first.upx")));
    }

    private static async Task ProcessAsync(
        IEnumerable<FileItemViewModel> files,
        FileProcessingRequest request)
    {
        var service = new FileProcessingService();
        await service.ProcessAsync(
            files,
            request,
            (file, progress) => file.Progress = progress,
            _ => { },
            (_, _) => { },
            CancellationToken.None);
    }

    private static FileProcessingRequest CreateRequest(
        UpxCommand command,
        FileOutputMode outputMode,
        string? customDirectory = null,
        bool force = false,
        bool keepBackup = false)
        => new(
            new Options
            {
                Command = command,
                Method = CompressionMethod.Lzma,
                Level = 9,
                Force = force,
                KeepBackup = keepBackup,
            },
            outputMode,
            customDirectory);

    private static FileItemViewModel CreateFileItem(string path)
        => new()
        {
            FilePath = path,
            FileName = Path.GetFileName(path),
            OriginalSize = new FileInfo(path).Length,
        };

    private static byte[] CreateDosCom()
    {
        byte[] data = new byte[512];
        for (int index = 0; index < data.Length; index++)
            data[index] = (byte)((index * 31 + 7) & 0xFF);
        data[0] = 0x90;
        data[1] = 0x90;
        return data;
    }

    private static byte[] CreateSyntheticPe(ushort machine)
    {
        bool is64 = machine == 0x8664;
        byte[] data = new byte[0x10000];
        data[0] = (byte)'M';
        data[1] = (byte)'Z';
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(60), 0x80);

        int peOffset = 0x80;
        data[peOffset] = (byte)'P';
        data[peOffset + 1] = (byte)'E';
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(peOffset + 4), machine);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(peOffset + 6), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(peOffset + 20), is64 ? (ushort)0xF0 : (ushort)0xE0);

        int optional = peOffset + 24;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(optional), is64 ? (ushort)0x20B : (ushort)0x10B);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(optional + 16), 0x1000);
        if (is64)
            BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(optional + 24), 0x140000000);
        else
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(optional + 28), 0x400000);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(optional + 60), 0x200);

        int section = optional + (is64 ? 0xF0 : 0xE0);
        Encoding.ASCII.GetBytes(".text\0\0\0").CopyTo(data.AsSpan(section, 8));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(section + 8), 0x40);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(section + 12), 0x1000);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(section + 16), 0x40);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(section + 20), 0x200);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(section + 36), 0x60000020);
        "UPXSharp WPF test"u8.CopyTo(data.AsSpan(0x200));
        return data;
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"upxsharp-wpf-tests-{Guid.NewGuid():N}");

        public TempDirectory() => Directory.CreateDirectory(Path);

        public string CreateFile(string name, byte[] contents)
        {
            string path = System.IO.Path.Combine(Path, name);
            File.WriteAllBytes(path, contents);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
