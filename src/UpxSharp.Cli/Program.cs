using System.Text.Json;
using UpxSharp.Core;
using UpxSharp.IO;
using UpxSharp.Packers;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--detect-fixture")
            return DetectFixture(args);

        return CommandLineRunner.Run(args, Console.Out, Console.Error);
    }

    private static int DetectFixture(string[] args)
    {
        string mode = args[1];
        var options = new Options { Method = CompressionMethod.Nrv2bLE32, Level = 1 };
        var results = new List<DetectionResult>();

        foreach (string path in args.Skip(2))
        {
            try
            {
                using var input = new InputFile(path);
                string format = mode == "unpack"
                    ? PackMaster.FindUnpacker(input, options).GetFormat().ToString()
                    : PackMaster.FindPacker(input, options).GetFormat().ToString();
                results.Add(new DetectionResult(path, format, true, null));
            }
            catch (Exception ex) when (ex is UpxException or IOException or UnauthorizedAccessException or NotImplementedException)
            {
                results.Add(new DetectionResult(path, null, false, ex.Message));
            }
        }

        Console.WriteLine(JsonSerializer.Serialize(results));
        return 0;
    }

    private sealed record DetectionResult(string File, string? Format, bool Recognized, string? Error);
}
