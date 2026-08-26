using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using UpxSharp.Core;

namespace UpxSharp.Stubs;

public sealed record StubMetadata(
    UpxFormat Format,
    string ResourceName,
    string Kind,
    int Size,
    string Sha256)
{
    public bool IsReal => string.Equals(Kind, "real", StringComparison.OrdinalIgnoreCase);
    public bool IsFake => string.Equals(Kind, "fake", StringComparison.OrdinalIgnoreCase);
}

public static class StubRepository
{
    private static readonly Assembly Assembly = typeof(StubRepository).Assembly;
    private const string ResourcePrefix = "UpxSharp.Stubs.Resources.";
    private const string ManifestResourceName = ResourcePrefix + "stubs.manifest.json";

    private static readonly Lazy<IReadOnlyDictionary<UpxFormat, StubMetadata>> Manifest =
        new(LoadManifest);

    public static byte[] Load(UpxFormat format)
        => Load(format, requireReal: false);

    public static byte[] Load(UpxFormat format, bool requireReal)
    {
        StubMetadata metadata = GetMetadata(format);
        if (requireReal && !metadata.IsReal)
            throw new InternalErrorException($"Stub real obrigatorio para {format}, mas manifesto marca '{metadata.Kind}'");

        byte[] bytes = LoadResource(ToFullResourceName(metadata.ResourceName));
        ValidateMetadata(metadata, bytes);
        return bytes;
    }

    public static StubMetadata GetMetadata(UpxFormat format)
        => Manifest.Value.TryGetValue(format, out StubMetadata? metadata)
            ? metadata
            : throw new NotImplementedException($"Stub nao disponivel para {format}");

    public static IReadOnlyCollection<StubMetadata> GetAllMetadata()
        => Manifest.Value.Values.ToArray();

    public static byte[] LoadResource(string resourceName)
    {
        using var stream = Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InternalErrorException($"Stub resource nao encontrado: {resourceName}");

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static IReadOnlyDictionary<UpxFormat, StubMetadata> LoadManifest()
    {
        byte[] bytes = LoadResource(ManifestResourceName);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            bytes = bytes[3..];

        var manifest = JsonSerializer.Deserialize<StubManifestDocument>(bytes)
            ?? throw new InternalErrorException("Manifesto de stubs invalido");

        var result = new Dictionary<UpxFormat, StubMetadata>();
        foreach (StubManifestEntry entry in manifest.Stubs)
        {
            if (!Enum.TryParse(entry.Format, ignoreCase: false, out UpxFormat format))
                throw new InternalErrorException($"Formato invalido no manifesto de stubs: {entry.Format}");

            if (string.IsNullOrWhiteSpace(entry.ResourceName))
                throw new InternalErrorException($"Resource vazio no manifesto de stubs: {entry.Format}");

            if (!string.Equals(entry.Kind, "fake", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(entry.Kind, "real", StringComparison.OrdinalIgnoreCase))
                throw new InternalErrorException($"Tipo de stub invalido para {entry.Format}: {entry.Kind}");

            var metadata = new StubMetadata(format, entry.ResourceName, entry.Kind, entry.Size, entry.Sha256);
            if (!result.TryAdd(format, metadata))
                throw new InternalErrorException($"Formato duplicado no manifesto de stubs: {format}");
        }

        return result;
    }

    private static void ValidateMetadata(StubMetadata metadata, byte[] bytes)
    {
        if (bytes.Length != metadata.Size)
            throw new InternalErrorException($"Stub {metadata.ResourceName} tem tamanho {bytes.Length}, manifesto esperava {metadata.Size}");

        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        if (!string.Equals(hash, metadata.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InternalErrorException($"Stub {metadata.ResourceName} tem SHA-256 diferente do manifesto");
    }

    private static string ToFullResourceName(string resourceName)
        => resourceName.StartsWith(ResourcePrefix, StringComparison.Ordinal)
            ? resourceName
            : ResourcePrefix + resourceName;

    private sealed record StubManifestDocument(
        [property: JsonPropertyName("stubs")] StubManifestEntry[] Stubs);

    private sealed record StubManifestEntry(
        [property: JsonPropertyName("format")] string Format,
        [property: JsonPropertyName("resourceName")] string ResourceName,
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("size")] int Size,
        [property: JsonPropertyName("sha256")] string Sha256);
}
