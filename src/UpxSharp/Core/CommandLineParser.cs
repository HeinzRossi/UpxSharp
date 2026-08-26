namespace UpxSharp.Core;

public static class CommandLineParser
{
    public static Options Parse(string[] args)
    {
        if (args.Length == 0)
            throw new ArgumentException(GetUsage());

        var options = Options.Default();
        var files = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "pack":
                case "compress":
                    options.Command = UpxCommand.Compress;
                    break;
                case "unpack":
                case "decompress":
                case "-d":
                case "--decompress":
                    options.Command = UpxCommand.Decompress;
                    break;
                case "test":
                case "-t":
                case "--test":
                    options.Command = UpxCommand.Test;
                    options.TestAfterPack = true;
                    break;
                case "list":
                case "-l":
                case "--list":
                    options.Command = UpxCommand.List;
                    options.ListOnly = true;
                    break;
                case "-o":
                case "--output":
                    if (++i >= args.Length)
                        throw new ArgumentException("Opção -o requer caminho de saída.");
                    options.OutputPath = args[i];
                    break;
                case "--lzma":
                    options.Method = CompressionMethod.Lzma;
                    break;
                case "--zstd":
                    options.Method = CompressionMethod.Zstd;
                    break;
                case "--nrv2b":
                    options.Method = CompressionMethod.Nrv2bLE32;
                    break;
                case "--nrv2d":
                    options.Method = CompressionMethod.Nrv2dLE32;
                    break;
                case "--nrv2e":
                    options.Method = CompressionMethod.Nrv2eLE32;
                    break;
                case "-f":
                case "--force":
                    options.Force = true;
                    break;
                case "-v":
                case "--verbose":
                    options.Verbose = true;
                    break;
                case "-q":
                case "--quiet":
                    options.Verbose = false;
                    break;
                default:
                    if (TryParseLevel(arg, out int level))
                    {
                        options.Level = level;
                    }
                    else if (arg.StartsWith('-'))
                    {
                        throw new ArgumentException($"Opção desconhecida: {arg}");
                    }
                    else
                    {
                        files.Add(arg);
                    }
                    break;
            }
        }

        if (files.Count == 0)
            throw new ArgumentException(GetUsage());

        options.InputPath = files[0];
        options.InputPaths.AddRange(files);
        options.Validate();
        return options;
    }

    public static string GetUsage()
        => "Usage: UpxSharp.Cli <pack|unpack|test|list> <input> [-o output] [--lzma|--zstd|--nrv2b|--nrv2d|--nrv2e] [-1..-9]";

    private static bool TryParseLevel(string arg, out int level)
    {
        level = 0;
        return arg.Length == 2 && arg[0] == '-' && arg[1] is >= '1' and <= '9'
            && int.TryParse(arg[1..], out level);
    }
}
