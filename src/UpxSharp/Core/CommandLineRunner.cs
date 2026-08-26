using UpxSharp.IO;
using UpxSharp.Packers;

namespace UpxSharp.Core;

public static class CommandLineRunner
{
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            var options = CommandLineParser.Parse(args);
            return Execute(options, output, error);
        }
        catch (ArgumentException ex)
        {
            error.WriteLine(ex.Message);
            return 1;
        }
        catch (UpxException ex)
        {
            error.WriteLine($"ERRO: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            error.WriteLine($"ERRO ({ex.GetType().Name}): {ex.Message}");
            return 1;
        }
    }

    public static int Execute(Options options, TextWriter output, TextWriter error)
    {
        options.Validate();
        var inputPaths = ResolveInputs(options.InputPaths.Count > 0 ? options.InputPaths : [options.InputPath!]);
        if (!string.IsNullOrWhiteSpace(options.OutputPath) && inputPaths.Count > 1)
            throw new ArgumentException("OutputPath não pode ser usado com múltiplos arquivos.", nameof(options.OutputPath));

        int exitCode = 0;
        foreach (string inputPath in inputPaths)
        {
            try
            {
                ExecuteOne(inputPath, options, output);
            }
            catch (Exception ex) when (inputPaths.Count > 1 && ex is UpxException or IOException or UnauthorizedAccessException)
            {
                error.WriteLine($"ERRO: {inputPath}: {ex.Message}");
                exitCode = 1;
            }
        }

        return exitCode;
    }

    private static void ExecuteOne(string inputPath, Options options, TextWriter output)
    {
        switch (options.Command)
        {
            case UpxCommand.Compress:
                Pack(inputPath, options.OutputPath ?? Path.ChangeExtension(inputPath, ".upx"), options);
                break;
            case UpxCommand.Decompress:
                Unpack(inputPath, options.OutputPath ?? Path.ChangeExtension(inputPath, ".unpacked"), options);
                break;
            case UpxCommand.List:
                List(inputPath, options, output);
                break;
            case UpxCommand.Test:
                Test(inputPath, options, output);
                break;
            case UpxCommand.Info:
                List(inputPath, options, output);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(options.Command), options.Command, "Comando inválido.");
        }
    }

    private static List<string> ResolveInputs(IEnumerable<string> inputs)
    {
        var resolved = new List<string>();
        foreach (string input in inputs)
        {
            if (!HasWildcard(input))
            {
                resolved.Add(input);
                continue;
            }

            string? directory = Path.GetDirectoryName(input);
            string pattern = Path.GetFileName(input);
            if (string.IsNullOrWhiteSpace(directory))
                directory = Directory.GetCurrentDirectory();

            string[] matches = Directory.GetFiles(directory, pattern);
            if (matches.Length == 0)
                throw new FileNotFoundException($"Nenhum arquivo encontrado para padrão: {input}");

            resolved.AddRange(matches.OrderBy(static p => p, StringComparer.OrdinalIgnoreCase));
        }

        return resolved;
    }

    private static bool HasWildcard(string path)
        => path.Contains('*') || path.Contains('?');

    private static void Pack(string inputPath, string outputPath, Options options)
    {
        string temporaryPath = CreateTemporaryPathBeside(outputPath);
        try
        {
            using (var input = new InputFile(inputPath))
            using (var output = new OutputFile(temporaryPath, overwrite: true))
            {
                var packer = PackMaster.FindPacker(input, options);
                packer.DoPack(output, options);
                output.Flush();
            }

            RejectGrownCompressedOutput(inputPath, temporaryPath);
            File.Move(temporaryPath, outputPath, overwrite: true);
        }
        finally
        {
            DeletePartialOutput(temporaryPath);
        }
    }

    private static void Unpack(string inputPath, string outputPath, Options options)
    {
        using var input = new InputFile(inputPath);
        using var output = new OutputFile(outputPath, overwrite: true);
        var packer = PackMaster.FindUnpacker(input, options);
        packer.DoUnpack(output, options);
    }

    private static void List(string inputPath, Options options, TextWriter output)
    {
        using var input = new InputFile(inputPath);
        try
        {
            var unpacker = PackMaster.FindUnpacker(input, options);
            output.WriteLine($"{unpacker.GetName()} {unpacker.GetFormat()} packed");
        }
        catch (NotPackedException)
        {
            var packer = PackMaster.FindPacker(input, options);
            output.WriteLine($"{packer.GetName()} {packer.GetFormat()} unpacked");
        }
    }

    private static void Test(string inputPath, Options options, TextWriter output)
    {
        string packedPath = Path.Combine(Path.GetTempPath(), $"upxsharp-test-{Guid.NewGuid():N}.upx");
        string unpackedPath = Path.Combine(Path.GetTempPath(), $"upxsharp-test-{Guid.NewGuid():N}.unpacked");

        try
        {
            Pack(inputPath, packedPath, options);
            Unpack(packedPath, unpackedPath, options);

            byte[] original = File.ReadAllBytes(inputPath);
            byte[] restored = File.ReadAllBytes(unpackedPath);
            if (!original.SequenceEqual(restored))
                throw new CorruptedPackException("Teste falhou: arquivo restaurado difere do original");

            output.WriteLine($"OK: {inputPath}");
        }
        finally
        {
            if (File.Exists(packedPath)) File.Delete(packedPath);
            if (File.Exists(unpackedPath)) File.Delete(unpackedPath);
        }
    }

    private static void RejectGrownCompressedOutput(string inputPath, string outputPath)
    {
        long inputSize = new FileInfo(inputPath).Length;
        long outputSize = new FileInfo(outputPath).Length;
        if (outputSize < inputSize)
            return;

        throw new CantPackException("resultado não ficou menor; arquivo não foi gerado.");
    }

    private static string CreateTemporaryPathBeside(string outputPath)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(outputPath))!;
        string fileName = Path.GetFileName(outputPath);
        return Path.Combine(directory, $".{fileName}.{Guid.NewGuid():N}.tmp");
    }

    private static void DeletePartialOutput(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup; preserve the original error.
        }
    }
}
