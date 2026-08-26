namespace UpxSharp.Core;

public sealed class Options
{
    private int _level = 7;
    private int _threads = Math.Max(1, Environment.ProcessorCount);

    public UpxCommand Command { get; set; } = UpxCommand.Compress;
    public CompressionMethod Method { get; set; } = CompressionMethod.Lzma;
    public int Level
    {
        get => _level;
        set
        {
            if (value is < 0 or > 9)
                throw new ArgumentOutOfRangeException(nameof(value), "Level deve estar entre 0 e 9.");
            _level = value;
        }
    }

    public bool Verbose { get; set; }
    public string? InputPath { get; set; }
    public List<string> InputPaths { get; } = [];
    public string? OutputPath { get; set; }
    public bool Force { get; set; }
    public bool KeepBackup { get; set; }
    public bool TestAfterPack { get; set; }
    public bool ListOnly { get; set; }
    public bool UseFilters { get; set; } = true;
    public byte MaxFilter { get; set; } = 0xFF;
    public int Threads
    {
        get => _threads;
        set
        {
            if (value < 1)
                throw new ArgumentOutOfRangeException(nameof(value), "Threads deve ser maior ou igual a 1.");
            _threads = value;
        }
    }

    public static Options Default() => new();

    public void Validate()
    {
        if (!Enum.IsDefined(Method))
            throw new ArgumentOutOfRangeException(nameof(Method), "Método de compressão inválido.");

        if (RequiresInputPath(Command) && InputPaths.Count == 0 && string.IsNullOrWhiteSpace(InputPath))
            throw new ArgumentException($"InputPath é obrigatório para o comando {Command}.", nameof(InputPath));

        if (!string.IsNullOrWhiteSpace(OutputPath) && InputPaths.Count > 1)
            throw new ArgumentException("OutputPath não pode ser usado com múltiplos arquivos.", nameof(OutputPath));
    }

    private static bool RequiresInputPath(UpxCommand command)
        => command is UpxCommand.Compress
            or UpxCommand.Decompress
            or UpxCommand.Test
            or UpxCommand.List
            or UpxCommand.Info;
}
