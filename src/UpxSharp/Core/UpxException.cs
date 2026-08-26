namespace UpxSharp.Core;

public class UpxException : Exception
{
    public UpxException(string message) : base(message) { }
    public UpxException(string message, Exception inner) : base(message, inner) { }
}

public sealed class AlreadyPackedException : UpxException
{
    public AlreadyPackedException() : base("Arquivo já está comprimido com UPX.") { }
}

public sealed class NotPackedException : UpxException
{
    public NotPackedException(string? msg = null)
        : base(msg ?? "Arquivo não foi comprimido pelo UPX.") { }
}

public sealed class CantPackException : UpxException
{
    public CantPackException(string reason) : base($"Não é possível comprimir: {reason}") { }
}

public sealed class CorruptedPackException : UpxException
{
    public CorruptedPackException(string reason) : base($"Arquivo corrompido: {reason}") { }
}

public sealed class OfficialUpxUnsupportedException : UpxException
{
    public OfficialUpxUnsupportedException(string reason)
        : base($"Arquivo UPX oficial detectado, mas unpack nativo ainda nÃ£o suportado: {reason}") { }
}

public sealed class OfficialUpxMetadataException : UpxException
{
    public OfficialUpxMetadataException(string reason)
        : base($"Metadados UPX oficial invÃ¡lidos ou incompletos: {reason}") { }
}

public sealed class OfficialUpxUnsupportedLayoutException : UpxException
{
    public OfficialUpxUnsupportedLayoutException(string reason)
        : base($"Layout ELF UPX oficial valido, mas ainda nao suportado: {reason}") { }
}

public sealed class OfficialUpxDecompressException : UpxException
{
    public OfficialUpxDecompressException(string reason)
        : base($"Falha ao descomprimir UPX oficial: {reason}") { }
}

public sealed class UpxIOException : UpxException
{
    public UpxIOException(string msg, Exception? inner = null) : base(msg, inner!) { }
}

public sealed class InternalErrorException : UpxException
{
    public InternalErrorException(string reason)
        : base($"Erro interno (bug): {reason}") { }
}
