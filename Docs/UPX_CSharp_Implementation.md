# UPX — Guia Completo de Reimplementação em C# / .NET 10

> **Projeto original:** UPX – Ultimate Packer for eXecutables  
> **Repositório:** https://github.com/upx/upx (branch `devel`)  
> **Linguagem alvo:** C# 13 / .NET 10  
> **Formatos alvo:** Todos os formatos suportados pelo UPX original  
> **Finalidade:** Documento de referência completo para guiar a implementação do zero em C#

---

## Sumário

1. [Visão Geral e Decisões de Projeto](#1-visão-geral-e-decisões-de-projeto)
2. [Estrutura de Solução .NET](#2-estrutura-de-solução-net)
3. [Tipos Primitivos e Endianness](#3-tipos-primitivos-e-endianness)
4. [Adler-32 e Checksums](#4-adler-32-e-checksums)
5. [MemBuffer — Gerenciamento de Memória](#5-membuffer--gerenciamento-de-memória)
6. [I/O de Arquivos](#6-io-de-arquivos)
7. [Sistema de Exceções](#7-sistema-de-exceções)
8. [Constantes Globais e Enumerações](#8-constantes-globais-e-enumerações)
9. [PackHeader](#9-packheader)
10. [Filtros de Código (Filters)](#10-filtros-de-código-filters)
11. [Sistema de Compressão](#11-sistema-de-compressão)
12. [Interface PackerBase e Classe Packer](#12-interface-packerbase-e-classe-packer)
13. [PackMaster — Detecção e Despacho](#13-packmaster--detecção-e-despacho)
14. [ElfLinker — Montagem de Stubs](#14-elflinker--montagem-de-stubs)
15. [Handlers: Linux ELF](#15-handlers-linux-elf)
16. [Handlers: Windows PE](#16-handlers-windows-pe)
17. [Handlers: macOS Mach-O](#17-handlers-macos-mach-o)
18. [Handlers: Formatos Legados e Embarcados](#18-handlers-formatos-legados-e-embarcados)
19. [Options — Interface de Linha de Comando](#19-options--interface-de-linha-de-comando)
20. [Dependências NuGet](#20-dependências-nuget)
21. [Testes](#21-testes)
22. [Checklist de Implementação](#22-checklist-de-implementação)

---

## 1. Visão Geral e Decisões de Projeto

### O que é o UPX

O UPX comprime executáveis incorporando um pequeno stub que, em tempo de execução, descomprime o código original na memória e transfere o controle para o entry point original. O arquivo comprimido permanece um executável válido para o SO.

### Decisões arquiteturais para C#

| Decisão | Escolha | Justificativa |
|---|---|---|
| Target framework | `net10.0` | Último LTS, Span<T>, NativeMemory, source generators |
| Leitura binária | `BinaryPrimitives` + `Span<byte>` | Zero-copy, portável, sem unsafe obrigatório |
| Gerenciamento de memória | `ArrayPool<byte>` + `MemoryOwner<T>` | Evita pressure no GC para buffers grandes |
| I/O de arquivo | `RandomAccess` + `FileStream` | Permite seek sem estado, thread-safe |
| Compressão | NuGet `SharpZipLib` / `ManagedLzma` | UCL portado ou wrapper LZMA gerenciado |
| Paralelismo | `Parallel.ForEach` no loop de otimização | Testar múltiplas combinações simultaneamente |
| Endianness | `BinaryPrimitives.ReadUInt32LittleEndian` | API nativa do .NET, sem implementação manual |
| Erros | Exceções tipadas derivadas de `UpxException` | Padrão idiomático C# |

---

## 2. Estrutura de Solução .NET

```
UpxSharp.sln
│
├── src/
│   └── UpxSharp/                        ← Biblioteca principal (Class Library)
│       UpxSharp.csproj
│       │
│       ├── Core/
│       │   ├── Constants.cs             ← UPX_F_*, M_*, limites
│       │   ├── UpxException.cs          ← Hierarquia de exceções
│       │   ├── Options.cs               ← Configurações globais
│       │   ├── PackHeader.cs            ← Struct de metadados UPX
│       │   ├── Adler32.cs               ← Implementação checksum
│       │   └── MemBuffer.cs             ← Buffer gerenciado seguro
│       │
│       ├── IO/
│       │   ├── InputFile.cs             ← Leitura de arquivo com seek
│       │   └── OutputFile.cs            ← Escrita de arquivo
│       │
│       ├── Endian/
│       │   └── BinaryEndian.cs          ← get_le32, get_be32, get_te32
│       │
│       ├── Compression/
│       │   ├── ICompressor.cs           ← Interface de compressão
│       │   ├── UclCompressor.cs         ← NRV2B/D/E via UCL
│       │   ├── LzmaCompressor.cs        ← LZMA
│       │   ├── ZstdCompressor.cs        ← Zstandard
│       │   └── CompressorFactory.cs     ← Cria compressor pelo método M_*
│       │
│       ├── Filters/
│       │   ├── Filter.cs                ← Struct de estado do filtro
│       │   ├── IFilter.cs               ← Interface filter/unfilter
│       │   ├── FilterX86.cs             ← Filtros FT_X86
│       │   ├── FilterArm32.cs           ← Filtros FT_ARM_EURO
│       │   ├── FilterArmThumb.cs        ← Filtros FT_ARM_THUMB
│       │   ├── FilterMips.cs            ← Filtros FT_MIPS_LE32
│       │   ├── FilterPpc.cs             ← Filtros FT_PPC32
│       │   └── FilterFactory.cs         ← Instancia filtro pelo ID
│       │
│       ├── Linker/
│       │   ├── ElfLinker.cs             ← Base: carrega/resolve stubs
│       │   ├── ElfLinkerAMD64.cs
│       │   ├── ElfLinkerArm32LE.cs
│       │   ├── ElfLinkerArm64LE.cs
│       │   ├── ElfLinkerMipsLE.cs
│       │   ├── ElfLinkerMipsBE.cs
│       │   └── ElfLinkerPpc32BE.cs
│       │
│       ├── Packers/
│       │   ├── PackerBase.cs            ← Interface abstrata
│       │   ├── Packer.cs                ← Implementação padrão
│       │   ├── PackMaster.cs            ← Detecção e despacho
│       │   │
│       │   ├── Linux/
│       │   │   ├── PackLinuxElf.cs      ← Base ELF
│       │   │   ├── PackLinuxElf32.cs
│       │   │   ├── PackLinuxElf64.cs
│       │   │   ├── PackLinuxElf32x86.cs
│       │   │   ├── PackLinuxElf32Arm.cs
│       │   │   ├── PackLinuxElf32Mips.cs
│       │   │   ├── PackLinuxElf32Ppc.cs
│       │   │   ├── PackLinuxElf64Amd.cs
│       │   │   ├── PackLinuxElf64Arm.cs
│       │   │   ├── PackLinuxElf64Ppc.cs
│       │   │   └── PackLinuxElf64Riscv.cs
│       │   │
│       │   ├── Windows/
│       │   │   ├── PeFile.cs            ← Base PE
│       │   │   ├── PeFile32.cs
│       │   │   └── PeFile64.cs
│       │   │
│       │   ├── MacOs/
│       │   │   ├── PackMachBase.cs
│       │   │   ├── PackMachI386.cs
│       │   │   └── PackMachAMD64.cs
│       │   │
│       │   └── Legacy/
│       │       ├── PackDosCom.cs
│       │       ├── PackDosExe.cs
│       │       ├── PackAtari.cs
│       │       ├── PackVmlinuzI386.cs
│       │       └── PackPs1.cs
│       │
│       └── Stubs/                       ← Stubs pré-compilados como resources
│           ├── Stubs.cs                 ← Acesso via GetManifestResourceStream
│           └── Resources/
│               ├── i386-linux.elf.stub
│               ├── amd64-linux.elf.stub
│               └── ...
│
├── src/
│   └── UpxSharp.Cli/                    ← Console App (entry point)
│       UpxSharp.Cli.csproj
│       └── Program.cs
│
└── tests/
    └── UpxSharp.Tests/                  ← xUnit tests
        UpxSharp.Tests.csproj
```

### UpxSharp.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>13</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <Optimize>true</Optimize>
  </PropertyGroup>

  <ItemGroup>
    <!-- Stubs pré-compilados embutidos no assembly -->
    <EmbeddedResource Include="Stubs/Resources/*.stub" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Toolkit.HighPerformance" Version="7.*" />
    <PackageReference Include="SharpZipLib" Version="1.*" />
    <PackageReference Include="ManagedLzma" Version="*" />
    <PackageReference Include="ZstdSharp.Port" Version="*" />
  </ItemGroup>
</Project>
```

---

## 3. Tipos Primitivos e Endianness

### BinaryEndian.cs

O .NET 10 já tem `System.Buffers.Binary.BinaryPrimitives` — use-o diretamente. Esta classe é um wrapper centralizado para facilitar a portabilidade do código C++ original.

```csharp
// Core/Endian/BinaryEndian.cs
using System.Buffers.Binary;

namespace UpxSharp.Endian;

/// <summary>
/// Equivalente ao bele.h do UPX original.
/// Provê leitura/escrita portável independente do endianness do host.
/// </summary>
public static class BinaryEndian
{
    // ── Little-Endian ────────────────────────────────────────────────────────

    public static ushort GetLE16(ReadOnlySpan<byte> buf, int offset = 0)
        => BinaryPrimitives.ReadUInt16LittleEndian(buf[offset..]);

    public static uint GetLE32(ReadOnlySpan<byte> buf, int offset = 0)
        => BinaryPrimitives.ReadUInt32LittleEndian(buf[offset..]);

    public static ulong GetLE64(ReadOnlySpan<byte> buf, int offset = 0)
        => BinaryPrimitives.ReadUInt64LittleEndian(buf[offset..]);

    public static void SetLE16(Span<byte> buf, ushort value, int offset = 0)
        => BinaryPrimitives.WriteUInt16LittleEndian(buf[offset..], value);

    public static void SetLE32(Span<byte> buf, uint value, int offset = 0)
        => BinaryPrimitives.WriteUInt32LittleEndian(buf[offset..], value);

    public static void SetLE64(Span<byte> buf, ulong value, int offset = 0)
        => BinaryPrimitives.WriteUInt64LittleEndian(buf[offset..], value);

    // ── Big-Endian ────────────────────────────────────────────────────────────

    public static ushort GetBE16(ReadOnlySpan<byte> buf, int offset = 0)
        => BinaryPrimitives.ReadUInt16BigEndian(buf[offset..]);

    public static uint GetBE32(ReadOnlySpan<byte> buf, int offset = 0)
        => BinaryPrimitives.ReadUInt32BigEndian(buf[offset..]);

    public static ulong GetBE64(ReadOnlySpan<byte> buf, int offset = 0)
        => BinaryPrimitives.ReadUInt64BigEndian(buf[offset..]);

    public static void SetBE32(Span<byte> buf, uint value, int offset = 0)
        => BinaryPrimitives.WriteUInt32BigEndian(buf[offset..], value);

    // ── Target Endian (determinado em runtime pelo packer) ────────────────────

    public static uint GetTE32(ReadOnlySpan<byte> buf, bool isBigEndian, int offset = 0)
        => isBigEndian ? GetBE32(buf, offset) : GetLE32(buf, offset);

    public static void SetTE32(Span<byte> buf, uint value, bool isBigEndian, int offset = 0)
    {
        if (isBigEndian) SetBE32(buf, value, offset);
        else             SetLE32(buf, value, offset);
    }
}
```

### Tipos com endianness embutida (structs)

Para mapear diretamente campos de headers ELF/PE, use structs com `[StructLayout]` apenas para leitura direta via `MemoryMarshal`. Para código mais seguro, prefira leitura campo a campo com `BinaryEndian`:

```csharp
// Exemplo: leitura portável de ELF header
public sealed class ElfHeader32
{
    public byte[]  EIdent     { get; init; } = new byte[16];
    public ushort  EType      { get; init; }
    public ushort  EMachine   { get; init; }
    public uint    EVersion   { get; init; }
    public uint    EEntry     { get; init; }
    public uint    EPhoff     { get; init; }
    public uint    EShoff     { get; init; }
    public uint    EFlags     { get; init; }
    public ushort  EHsize     { get; init; }
    public ushort  EPhentsize { get; init; }
    public ushort  EPhnum     { get; init; }

    public static ElfHeader32 Read(ReadOnlySpan<byte> data, bool bigEndian)
    {
        return new ElfHeader32
        {
            EIdent     = data[..16].ToArray(),
            EType      = BinaryEndian.GetTE16(data, bigEndian, 16),
            EMachine   = BinaryEndian.GetTE16(data, bigEndian, 18),
            EEntry     = BinaryEndian.GetTE32(data, bigEndian, 24),
            EPhoff     = BinaryEndian.GetTE32(data, bigEndian, 28),
            EPhnum     = BinaryEndian.GetTE16(data, bigEndian, 44),
        };
    }
}
```

---

## 4. Adler-32 e Checksums

```csharp
// Core/Adler32.cs
namespace UpxSharp.Core;

/// <summary>
/// Implementação do checksum Adler-32 — equivalente ao upx_adler32() do UPX.
/// Usado para verificar integridade dos dados comprimidos e descomprimidos.
/// </summary>
public static class Adler32
{
    private const uint ModAdler = 65521;

    /// <summary>
    /// Calcula ou atualiza checksum Adler-32.
    /// </summary>
    /// <param name="data">Dados a processar</param>
    /// <param name="initial">Valor inicial (1 para novo checksum, valor anterior para continuar)</param>
    /// <returns>Checksum Adler-32</returns>
    public static uint Compute(ReadOnlySpan<byte> data, uint initial = 1)
    {
        uint a = initial & 0xFFFF;
        uint b = (initial >> 16) & 0xFFFF;

        // Processa em blocos de 5550 para evitar overflow antes do módulo
        const int BlockSize = 5550;
        int offset = 0;

        while (offset < data.Length)
        {
            int blockEnd = Math.Min(offset + BlockSize, data.Length);

            for (int i = offset; i < blockEnd; i++)
            {
                a += data[i];
                b += a;
            }

            a %= ModAdler;
            b %= ModAdler;
            offset = blockEnd;
        }

        return (b << 16) | a;
    }

    /// <summary>
    /// Verifica se o checksum dos dados corresponde ao valor esperado.
    /// Lança CorruptedPackException se não corresponder.
    /// </summary>
    public static void Verify(ReadOnlySpan<byte> data, uint expected, string context)
    {
        uint actual = Compute(data);
        if (actual != expected)
            throw new CorruptedPackException(
                $"Falha no checksum Adler-32 em {context}: " +
                $"esperado=0x{expected:X8}, calculado=0x{actual:X8}");
    }
}
```

---

## 5. MemBuffer — Gerenciamento de Memória

```csharp
// Core/MemBuffer.cs
using System.Buffers;

namespace UpxSharp.Core;

/// <summary>
/// Buffer de memória seguro com verificação de limite.
/// Equivalente ao MemBuffer do UPX — usa ArrayPool para eficiência.
/// </summary>
public sealed class MemBuffer : IDisposable
{
    /// <summary>Tamanho máximo permitido: 768 MiB (UPX_RSIZE_MAX)</summary>
    public const long MaxSize = 768L * 1024 * 1024;

    private byte[]? _array;
    private bool    _disposed;

    public byte[] Data     => _array ?? throw new ObjectDisposedException(nameof(MemBuffer));
    public int    Length   { get; private set; }
    public Span<byte>           Span   => Data.AsSpan(0, Length);
    public ReadOnlySpan<byte>   ReadOnlySpan => Data.AsSpan(0, Length);
    public Memory<byte>         Memory => Data.AsMemory(0, Length);

    private MemBuffer() { }

    /// <summary>Aloca um buffer de tamanho <paramref name="size"/> bytes.</summary>
    public static MemBuffer Alloc(int size, bool clearMemory = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);

        if (size > MaxSize)
            throw new UpxException(
                $"Tamanho de buffer excede UPX_RSIZE_MAX: {size} > {MaxSize}");

        var buf = new MemBuffer
        {
            _array = ArrayPool<byte>.Shared.Rent(size),
            Length = size
        };

        if (clearMemory)
            buf.Span.Clear();

        return buf;
    }

    /// <summary>Cria MemBuffer a partir de array existente (sem cópia).</summary>
    public static MemBuffer FromArray(byte[] data)
        => new() { _array = data, Length = data.Length };

    /// <summary>Copia dados de um ReadOnlySpan para um novo MemBuffer.</summary>
    public static MemBuffer FromSpan(ReadOnlySpan<byte> data)
    {
        var buf = Alloc(data.Length, clearMemory: false);
        data.CopyTo(buf.Span);
        return buf;
    }

    /// <summary>Retorna subconjunto como Span sem alocação.</summary>
    public Span<byte> Slice(int start, int length)
        => Data.AsSpan(start, length);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_array is not null)
        {
            ArrayPool<byte>.Shared.Return(_array);
            _array = null;
        }
    }
}
```

---

## 6. I/O de Arquivos

```csharp
// IO/InputFile.cs
namespace UpxSharp.IO;

public sealed class InputFile : IDisposable
{
    private readonly FileStream _stream;

    public string Name   { get; }
    public long   Length => _stream.Length;

    public InputFile(string path)
    {
        Name    = path;
        _stream = new FileStream(path,
            FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 65536, FileOptions.RandomAccess);
    }

    /// <summary>Lê exatamente <paramref name="count"/> bytes — lança exceção se não conseguir.</summary>
    public void ReadExact(Span<byte> buffer, long offset)
    {
        int read = RandomAccess.Read(_stream.SafeFileHandle, buffer, offset);
        if (read != buffer.Length)
            throw new IOException(
                $"ReadExact: esperava {buffer.Length} bytes, leu {read} em '{Name}' offset {offset}");
    }

    /// <summary>Tenta ler até <paramref name="buffer.Length"/> bytes. Retorna bytes lidos.</summary>
    public int Read(Span<byte> buffer, long offset)
        => RandomAccess.Read(_stream.SafeFileHandle, buffer, offset);

    public void Dispose() => _stream.Dispose();
}

// IO/OutputFile.cs
public sealed class OutputFile : IDisposable
{
    private readonly FileStream _stream;
    private long _position;

    public string Name => _stream.Name;

    public OutputFile(string path, bool overwrite = false)
    {
        _stream = new FileStream(path,
            overwrite ? FileMode.Create : FileMode.CreateNew,
            FileAccess.Write, FileShare.None,
            bufferSize: 65536);
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        _stream.Write(data);
        _position += data.Length;
    }

    /// <summary>Sobrescreve bytes em posição específica sem mover o cursor de escrita principal.</summary>
    public void Rewrite(ReadOnlySpan<byte> data, long offset)
    {
        long saved = _stream.Position;
        _stream.Seek(offset, SeekOrigin.Begin);
        _stream.Write(data);
        _stream.Seek(saved, SeekOrigin.Begin);
    }

    public long Position => _position;

    public void Flush()   => _stream.Flush();
    public void Dispose() => _stream.Dispose();
}
```

---

## 7. Sistema de Exceções

```csharp
// Core/UpxException.cs
namespace UpxSharp.Core;

/// <summary>Exceção base de todos os erros do UPX.</summary>
public class UpxException : Exception
{
    public UpxException(string message) : base(message) { }
    public UpxException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Arquivo já está comprimido com UPX.</summary>
public sealed class AlreadyPackedException : UpxException
{
    public AlreadyPackedException() : base("Arquivo já está comprimido com UPX.") { }
}

/// <summary>Arquivo não foi comprimido pelo UPX.</summary>
public sealed class NotPackedException : UpxException
{
    public NotPackedException(string? msg = null)
        : base(msg ?? "Arquivo não foi comprimido pelo UPX.") { }
}

/// <summary>Não é possível comprimir este arquivo.</summary>
public sealed class CantPackException : UpxException
{
    public CantPackException(string reason) : base($"Não é possível comprimir: {reason}") { }
}

/// <summary>Arquivo corrompido ou inválido.</summary>
public sealed class CorruptedPackException : UpxException
{
    public CorruptedPackException(string reason) : base($"Arquivo corrompido: {reason}") { }
}

/// <summary>Erro de I/O.</summary>
public sealed class UpxIOException : UpxException
{
    public UpxIOException(string msg, Exception? inner = null) : base(msg, inner!) { }
}

/// <summary>Erro interno — indica bug na implementação.</summary>
public sealed class InternalErrorException : UpxException
{
    public InternalErrorException(string reason)
        : base($"Erro interno (bug): {reason}") { }
}
```

---

## 8. Constantes Globais e Enumerações

```csharp
// Core/Constants.cs
namespace UpxSharp.Core;

public static class UpxConstants
{
    // ── Magic bytes ───────────────────────────────────────────────────────────
    public const uint   MagicLE32  = 0x21585055;  // "UPX!" em LE
    public const uint   Magic2LE32 = 0x213d4c53;  // "SL=!" em LE
    public static readonly byte[] MagicBytes = "UPX!"u8.ToArray();

    // ── Limite de segurança ───────────────────────────────────────────────────
    public const long RsizeMax = 768L * 1024 * 1024;  // 768 MiB

    // ── Versão ────────────────────────────────────────────────────────────────
    public const byte VersionMajor = 4;
    public const byte VersionMinor = 2;
}

/// <summary>Identificadores de formato — equivalente a UPX_F_* do C++.</summary>
public enum UpxFormat : byte
{
    DosCom       = 1,
    DosSys       = 2,
    DosExe       = 3,
    Djgpp2Coff   = 4,
    Win32Pe      = 7,
    LinuxElfi386 = 11,
    Win64Pe      = 13,
    LinuxElfAmd64  = 24,
    LinuxElfArmLE  = 30,
    LinuxElfArm64LE= 38,
    LinuxElfMipsel = 40,
    LinuxElfMips   = 43,
    LinuxElfRiscv64= 49,
    MachI386     = 51,
    MachAMD64    = 52,
    Vmlinuzi386  = 60,
    AtariTos     = 80,
    Ps1Exe       = 90,
}

/// <summary>Métodos de compressão — equivalente a M_* do C++.</summary>
public enum CompressionMethod : byte
{
    Nrv2bLE32 = 2,
    Nrv2b8    = 3,
    Nrv2bLE16 = 4,
    Nrv2dLE32 = 5,
    Nrv2d8    = 6,
    Nrv2dLE16 = 7,
    Nrv2eLE32 = 8,
    Nrv2e8    = 9,
    Nrv2eLE16 = 10,
    Lzma      = 14,
    Deflate   = 15,
    Zstd      = 18,
}

/// <summary>Comandos da CLI.</summary>
public enum UpxCommand
{
    Compress,
    Decompress,
    Test,
    List,
    Info,
}
```

---

## 9. PackHeader

```csharp
// Core/PackHeader.cs
namespace UpxSharp.Core;

/// <summary>
/// Metadados embutidos no executável comprimido.
/// Equivalente ao PackHeader do C++ — serializado/desserializado manualmente.
/// </summary>
public sealed class PackHeader
{
    // Campos fixos (presentes em todas as versões)
    public byte[]           Magic       { get; set; } = "UPX!"u8.ToArray();
    public byte             Version     { get; set; }
    public UpxFormat        Format      { get; set; }
    public CompressionMethod Method     { get; set; }
    public byte             Level       { get; set; }

    public uint             UAdler      { get; set; }  // checksum descomprimido
    public uint             CAdler      { get; set; }  // checksum comprimido

    public uint             ULen        { get; set; }  // tamanho original
    public uint             CLen        { get; set; }  // tamanho comprimido
    public uint             UFileSize   { get; set; }  // tamanho do arquivo original

    public byte             FilterId    { get; set; }
    public byte             FilterCto   { get; set; }
    public byte             NMru        { get; set; }

    // Tamanho total serializado (varia por versão)
    public const int SerializedSize = 32;

    /// <summary>Serializa o PackHeader em buffer de bytes.</summary>
    public void Write(Span<byte> buf)
    {
        if (buf.Length < SerializedSize)
            throw new ArgumentException("Buffer muito pequeno para PackHeader");

        Magic.CopyTo(buf);               // offset  0: 4 bytes
        buf[4]  = Version;               // offset  4
        buf[5]  = (byte)Format;          // offset  5
        buf[6]  = (byte)Method;          // offset  6
        buf[7]  = Level;                 // offset  7

        BinaryPrimitives.WriteUInt32LittleEndian(buf[8..],  UAdler);    // offset  8
        BinaryPrimitives.WriteUInt32LittleEndian(buf[12..], CAdler);    // offset 12
        BinaryPrimitives.WriteUInt32LittleEndian(buf[16..], ULen);      // offset 16
        BinaryPrimitives.WriteUInt32LittleEndian(buf[20..], CLen);      // offset 20
        BinaryPrimitives.WriteUInt32LittleEndian(buf[24..], UFileSize); // offset 24

        buf[28] = FilterId;              // offset 28
        buf[29] = FilterCto;             // offset 29
        buf[30] = NMru;                  // offset 30
        buf[31] = 0;                     // padding
    }

    /// <summary>Desserializa um PackHeader de um buffer.</summary>
    public static PackHeader Read(ReadOnlySpan<byte> buf)
    {
        if (buf.Length < SerializedSize)
            throw new CorruptedPackException("PackHeader truncado");

        // Verifica magic bytes
        if (!buf[..4].SequenceEqual("UPX!"u8))
            throw new NotPackedException("Magic bytes UPX! não encontrados");

        return new PackHeader
        {
            Magic       = buf[..4].ToArray(),
            Version     = buf[4],
            Format      = (UpxFormat)buf[5],
            Method      = (CompressionMethod)buf[6],
            Level       = buf[7],
            UAdler      = BinaryPrimitives.ReadUInt32LittleEndian(buf[8..]),
            CAdler      = BinaryPrimitives.ReadUInt32LittleEndian(buf[12..]),
            ULen        = BinaryPrimitives.ReadUInt32LittleEndian(buf[16..]),
            CLen        = BinaryPrimitives.ReadUInt32LittleEndian(buf[20..]),
            UFileSize   = BinaryPrimitives.ReadUInt32LittleEndian(buf[24..]),
            FilterId    = buf[28],
            FilterCto   = buf[29],
            NMru        = buf[30],
        };
    }

    /// <summary>Verifica se existe um PackHeader válido em determinado offset.</summary>
    public static bool TryFind(ReadOnlySpan<byte> data, out int offset, out PackHeader? header)
    {
        offset = -1;
        header = null;

        for (int i = 0; i <= data.Length - SerializedSize; i++)
        {
            if (data[i] == (byte)'U' && data[i+1] == (byte)'P' &&
                data[i+2] == (byte)'X' && data[i+3] == (byte)'!')
            {
                try
                {
                    header = Read(data[i..]);
                    offset = i;
                    return true;
                }
                catch { /* continua buscando */ }
            }
        }
        return false;
    }
}
```

---

## 10. Filtros de Código (Filters)

### Filter.cs

```csharp
// Filters/Filter.cs
namespace UpxSharp.Filters;

/// <summary>
/// Estado de um filtro de pré-processamento.
/// Equivalente à struct Filter do C++.
/// </summary>
public sealed class FilterState
{
    public byte   Id      { get; set; }   // identificador do filtro (FT_*)
    public byte[] Buffer  { get; set; } = [];
    public int    BufLen  { get; set; }
    public uint   AddValue{ get; set; }   // valor adicionado em calls/jumps relativos
    public uint   Calls   { get; set; }   // saída: quantas transformações foram feitas
    public byte   Cto     { get; set; }   // call-through offset
    public byte   NMru    { get; set; }   // tamanho do MRU buffer
}
```

### IFilter.cs

```csharp
// Filters/IFilter.cs
namespace UpxSharp.Filters;

public interface IFilter
{
    byte Id { get; }
    /// <summary>Aplica transformação (encode) — melhora compressibilidade.</summary>
    void Filter(FilterState state);
    /// <summary>Reverte transformação (decode) — restaura código original.</summary>
    void Unfilter(FilterState state);
}
```

### FilterX86.cs (exemplo de implementação)

```csharp
// Filters/FilterX86.cs
namespace UpxSharp.Filters;

/// <summary>
/// Filtro para código x86/x86-64.
/// Converte endereços absolutos de CALL/JMP para relativos
/// ao início do buffer — aumenta redundância para o compressor.
/// </summary>
public sealed class FilterX86 : IFilter
{
    public byte Id { get; }

    public FilterX86(byte id) => Id = id;

    public void Filter(FilterState state)
    {
        var buf  = state.Buffer.AsSpan(0, state.BufLen);
        uint calls = 0;

        for (int i = 0; i < buf.Length - 4; i++)
        {
            // Detecta opcode CALL (0xE8) ou JMP relativo (0xE9)
            if (buf[i] is not (0xE8 or 0xE9)) continue;

            // Lê deslocamento relativo de 32-bit LE
            uint rel = BinaryPrimitives.ReadUInt32LittleEndian(buf[(i+1)..]);

            // Converte para endereço absoluto (base = início do buffer)
            uint abs = rel + (uint)(i + 5) + state.AddValue;

            // Escreve endereço absoluto de volta
            BinaryPrimitives.WriteUInt32LittleEndian(buf[(i+1)..], abs);
            calls++;
            i += 4; // pula os 4 bytes de operando
        }

        state.Calls = calls;
    }

    public void Unfilter(FilterState state)
    {
        var buf = state.Buffer.AsSpan(0, state.BufLen);

        for (int i = 0; i < buf.Length - 4; i++)
        {
            if (buf[i] is not (0xE8 or 0xE9)) continue;

            uint abs = BinaryPrimitives.ReadUInt32LittleEndian(buf[(i+1)..]);
            uint rel = abs - (uint)(i + 5) - state.AddValue;

            BinaryPrimitives.WriteUInt32LittleEndian(buf[(i+1)..], rel);
            i += 4;
        }
    }
}
```

### FilterFactory.cs

```csharp
// Filters/FilterFactory.cs
namespace UpxSharp.Filters;

public static class FilterFactory
{
    public static IFilter? Create(byte filterId) => filterId switch
    {
        0x00        => null,                       // FT_NONE
        >= 0x01 and <= 0x1F => new FilterX86(filterId),
        >= 0x40 and <= 0x4F => new FilterArm32(filterId),
        >= 0x50 and <= 0x5F => new FilterArmThumb(filterId),
        >= 0x80 and <= 0x8F => new FilterMips(filterId),
        >= 0x90 and <= 0x9F => new FilterPpc(filterId),
        _           => throw new CantPackException($"Filtro desconhecido: 0x{filterId:X2}")
    };

    /// <summary>Retorna todos os IDs de filtros compatíveis com a arquitetura.</summary>
    public static IEnumerable<byte> GetFiltersForArch(UpxFormat format) => format switch
    {
        UpxFormat.LinuxElfi386 or UpxFormat.Win32Pe or UpxFormat.Win64Pe
            => Enumerable.Range(0x01, 0x1F).Select(i => (byte)i).Prepend(0x00),

        UpxFormat.LinuxElfArmLE or UpxFormat.LinuxElfArm64LE
            => Enumerable.Range(0x40, 0x1F).Select(i => (byte)i).Prepend(0x00),

        _ => [0x00]
    };
}
```

---

## 11. Sistema de Compressão

### ICompressor.cs

```csharp
// Compression/ICompressor.cs
namespace UpxSharp.Compression;

public interface ICompressor
{
    CompressionMethod Method { get; }

    /// <summary>
    /// Comprime <paramref name="source"/> em <paramref name="destination"/>.
    /// Retorna tamanho comprimido, ou -1 se os dados não foram compressíveis.
    /// </summary>
    int Compress(
        ReadOnlySpan<byte> source,
        Span<byte>         destination,
        int                level = 8);

    /// <summary>Descomprime <paramref name="source"/> em <paramref name="destination"/>.</summary>
    void Decompress(
        ReadOnlySpan<byte> source,
        Span<byte>         destination);
}
```

### LzmaCompressor.cs (implementação de referência)

```csharp
// Compression/LzmaCompressor.cs
using ManagedLzma.LZMA;

namespace UpxSharp.Compression;

public sealed class LzmaCompressor : ICompressor
{
    public CompressionMethod Method => CompressionMethod.Lzma;

    public int Compress(ReadOnlySpan<byte> source, Span<byte> destination, int level = 8)
    {
        // Usa ManagedLzma (portável, 100% gerenciado)
        // Nível de compressão: level mapeia para dictionary size
        int dictSize = level switch
        {
            <= 3 => 1 << 16,   // 64 KB
            <= 6 => 1 << 20,   // 1 MB
            <= 8 => 1 << 23,   // 8 MB
            _    => 1 << 25,   // 32 MB
        };

        using var input  = new MemoryStream(source.ToArray());
        using var output = new MemoryStream();

        var encoder = new Encoder();
        encoder.SetCoderProperties(
        [
            CoderPropID.DictionarySize,
            CoderPropID.NumFastBytes,
            CoderPropID.MatchFinder,
        ],
        [dictSize, level >= 7 ? 128 : 32, "BT4"]);

        // Escreve propriedades LZMA (5 bytes de header)
        encoder.WriteCoderProperties(output);
        // Escreve tamanho original em 8 bytes LE (convenção UPX)
        output.Write(BitConverter.GetBytes((long)source.Length));
        // Comprime
        encoder.Code(input, output, source.Length, -1, null);

        byte[] result = output.ToArray();
        if (result.Length >= destination.Length) return -1;

        result.CopyTo(destination);
        return result.Length;
    }

    public void Decompress(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        using var input  = new MemoryStream(source.ToArray());
        using var output = new MemoryStream(destination.Length);

        // Lê propriedades LZMA (5 bytes)
        byte[] props = new byte[5];
        input.ReadExactly(props);

        // Pula tamanho original (8 bytes)
        input.Seek(8, SeekOrigin.Current);

        var decoder = new Decoder();
        decoder.SetDecoderProperties(props);
        decoder.Code(input, output, source.Length - 13, destination.Length, null);

        output.ToArray().AsSpan(0, destination.Length).CopyTo(destination);
    }
}
```

### CompressorFactory.cs

```csharp
// Compression/CompressorFactory.cs
namespace UpxSharp.Compression;

public static class CompressorFactory
{
    public static ICompressor Create(CompressionMethod method) => method switch
    {
        CompressionMethod.Nrv2bLE32 or
        CompressionMethod.Nrv2dLE32 or
        CompressionMethod.Nrv2eLE32 => new UclCompressor(method),
        CompressionMethod.Lzma      => new LzmaCompressor(),
        CompressionMethod.Zstd      => new ZstdCompressor(),
        CompressionMethod.Deflate   => new DeflateCompressor(),
        _ => throw new CantPackException($"Método de compressão não suportado: {method}")
    };
}
```

---

## 12. Interface PackerBase e Classe Packer

### PackerBase.cs

```csharp
// Packers/PackerBase.cs
namespace UpxSharp.Packers;

/// <summary>
/// Interface abstrata que todo handler de formato deve implementar.
/// Equivalente ao PackerBase do C++.
/// </summary>
public abstract class PackerBase
{
    protected readonly InputFile File;

    protected PackerBase(InputFile file) => File = file;

    // ── Identificação ─────────────────────────────────────────────────────────

    public abstract UpxFormat    GetFormat();
    public abstract string       GetName();
    public abstract byte         GetVersion();
    public abstract bool         IsBigEndian { get; }

    // ── Detecção ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifica se este packer pode comprimir o arquivo.
    /// Retorna: true = sim; false = não é este formato; null = erro definitivo.
    /// </summary>
    public abstract bool? CanPack();

    /// <summary>Verifica se este packer pode descomprimir o arquivo.</summary>
    public abstract bool? CanUnpack();

    // ── Operações ─────────────────────────────────────────────────────────────

    public abstract void DoPack(OutputFile output, Options options);
    public abstract void DoUnpack(OutputFile output, Options options);
}
```

### Packer.cs (classe base com implementações padrão)

```csharp
// Packers/Packer.cs
namespace UpxSharp.Packers;

/// <summary>
/// Implementação padrão com funcionalidades compartilhadas.
/// Equivalente à classe Packer do C++.
/// </summary>
public abstract class Packer : PackerBase
{
    protected readonly Options Options;
    protected ElfLinker?       Linker;
    protected PackHeader       Header = new();

    protected Packer(InputFile file, Options options) : base(file)
        => Options = options;

    // ── Compressão com otimização de filtros ──────────────────────────────────

    /// <summary>
    /// Tenta múltiplas combinações de filtros e parâmetros de compressão.
    /// Equivalente ao compressWithFilters() do C++.
    /// </summary>
    protected CompressResult CompressWithFilters(
        ReadOnlySpan<byte> code,
        IEnumerable<byte>  filterIds,
        IEnumerable<int>   levels)
    {
        CompressResult best = new() { CompressedSize = int.MaxValue };

        // Paralelismo: testa combinações simultaneamente
        var combinations = filterIds
            .SelectMany(f => levels.Select(l => (FilterId: f, Level: l)))
            .ToList();

        object lockObj = new();

        Parallel.ForEach(combinations, combo =>
        {
            using var codeCopy = MemBuffer.FromSpan(code);

            // Aplica filtro
            var filter = FilterFactory.Create(combo.FilterId);
            var filterState = new FilterState
            {
                Id     = combo.FilterId,
                Buffer = codeCopy.Data,
                BufLen = codeCopy.Length,
            };
            filter?.Filter(filterState);

            // Comprime
            using var compressed = MemBuffer.Alloc(code.Length + 1024);
            var compressor = CompressorFactory.Create(Options.Method);
            int cLen = compressor.Compress(
                codeCopy.ReadOnlySpan,
                compressed.Span,
                combo.Level);

            if (cLen < 0) return; // não compressível com esta combinação

            // Compara com melhor resultado
            lock (lockObj)
            {
                if (cLen < best.CompressedSize)
                {
                    best = new CompressResult
                    {
                        FilterId       = combo.FilterId,
                        FilterCto      = filterState.Cto,
                        Level          = combo.Level,
                        CompressedSize = cLen,
                        CompressedData = compressed.Span[..cLen].ToArray(),
                        UAdler = Adler32.Compute(code),
                        CAdler = Adler32.Compute(compressed.ReadOnlySpan[..cLen]),
                    };
                }
            }
        });

        if (best.CompressedSize == int.MaxValue)
            throw new CantPackException("Dados não foram compressíveis com nenhuma combinação.");

        return best;
    }

    /// <summary>Verifica se descompressão in-place é viável.</summary>
    protected static bool CheckOverlap(
        int srcOffset, int srcSize, int dstOffset, int overwriteSize)
    {
        // Origem e destino não podem se sobrepor de forma destrutiva
        return dstOffset + overwriteSize <= srcOffset ||
               srcOffset + srcSize      <= dstOffset;
    }
}

public record CompressResult
{
    public byte   FilterId       { get; init; }
    public byte   FilterCto      { get; init; }
    public int    Level          { get; init; }
    public int    CompressedSize { get; init; }
    public byte[] CompressedData { get; init; } = [];
    public uint   UAdler         { get; init; }
    public uint   CAdler         { get; init; }
}
```

---

## 13. PackMaster — Detecção e Despacho

```csharp
// Packers/PackMaster.cs
namespace UpxSharp.Packers;

/// <summary>
/// Detecta o formato do arquivo e instancia o packer correto.
/// Equivalente ao PackMaster do C++.
/// </summary>
public static class PackMaster
{
    // Lista ordenada de factories — formatos mais específicos primeiro
    private static readonly List<Func<InputFile, Options, PackerBase>> PackerFactories =
    [
        // Linux ELF
        (f, o) => new PackLinuxElf32x86(f, o),
        (f, o) => new PackLinuxElf64Amd(f, o),
        (f, o) => new PackLinuxElf32Arm(f, o),
        (f, o) => new PackLinuxElf64Arm(f, o),
        (f, o) => new PackLinuxElf32Mips(f, o),
        (f, o) => new PackLinuxElf32Ppc(f, o),
        (f, o) => new PackLinuxElf64Ppc(f, o),
        (f, o) => new PackLinuxElf64Riscv(f, o),

        // Windows PE
        (f, o) => new PeFile32(f, o),
        (f, o) => new PeFile64(f, o),

        // macOS Mach-O
        (f, o) => new PackMachI386(f, o),
        (f, o) => new PackMachAMD64(f, o),

        // Kernel Linux
        (f, o) => new PackVmlinuzI386(f, o),

        // Legados
        (f, o) => new PackDosCom(f, o),
        (f, o) => new PackDosExe(f, o),
        (f, o) => new PackAtari(f, o),
        (f, o) => new PackPs1(f, o),
    ];

    /// <summary>
    /// Encontra o packer adequado para o arquivo.
    /// Lança CantPackException se nenhum format for reconhecido.
    /// </summary>
    public static PackerBase FindPacker(InputFile file, Options options)
    {
        foreach (var factory in PackerFactories)
        {
            var packer = factory(file, options);
            bool? result = packer.CanPack();

            if (result == true)  return packer;
            if (result == null)  // erro definitivo — aborta
                throw new CantPackException($"Erro ao analisar '{file.Name}'");
            // result == false → próximo packer
        }

        throw new CantPackException(
            $"Formato não reconhecido ou não suportado: '{file.Name}'");
    }

    /// <summary>Encontra packer para descompressão.</summary>
    public static PackerBase FindUnpacker(InputFile file, Options options)
    {
        foreach (var factory in PackerFactories)
        {
            var packer = factory(file, options);
            bool? result = packer.CanUnpack();
            if (result == true) return packer;
            if (result == null) throw new NotPackedException();
        }
        throw new NotPackedException();
    }
}
```

---

## 14. ElfLinker — Montagem de Stubs

Estado atual: infraestrutura scaffold implementada e testada.

- `ElfLinker` mantém tabela de símbolos, clona o stub em `Relocate()` e aplica patches sem mutar o array original.
- Subclasses scaffold existem para AMD64, ARM32, ARM64, MIPS e PPC.
- O formato fake `UPXL` usado nos testes expõe seção `.loader` e patch `_patch32`.
- Relocações ELF reais ainda dependem de stubs reais do UPX e fixtures reais.

### Carregamento de Stubs via Embedded Resources

Estado atual: `StubRepository` carrega resources embutidos para todos os formatos scaffold implementados.

- `stubs.manifest.json` registra formato, nome do resource, tipo (`fake` ou `real`), tamanho e SHA-256.
- `StubRepository.Load(format)` valida manifesto contra bytes embutidos.
- `StubRepository.Load(format, requireReal: true)` rejeita stubs fake.
- O script `tools/import-real-stubs.ps1` importa um conjunto local de stubs reais e recalcula o manifesto.

Os stubs atuais são fake/dev. Eles servem para testar infraestrutura, não para gerar executáveis UPX reais.

---

## 15. Handlers: Linux ELF

Estado atual: scaffold pack/unpack implementado para ELF little-endian i386, amd64, ARM32, ARM64, MIPS, RISC-V64 e PPC64LE, além de MIPS, PPC32 e PPC64 big-endian.

O fluxo atual detecta magic ELF, classe 32/64, endianness e `e_machine`; localiza `.text` pela section table ou primeira seção executável; comprime o payload; anexa `PackHeader + compressedData` ao arquivo original; e no unpack valida limites, checksums, filtro inverso e restaura os bytes originais. Este fluxo é intencionalmente scaffold: ainda não reescreve headers ELF reais, não injeta loader executável e não altera entry point.

---

## 16. Handlers: Windows PE

Estado atual: scaffold pack/unpack implementado para PE32 e PE64.

O fluxo atual detecta `MZ`, `PE\0\0`, machine i386/amd64 e seção `.text`; comprime a seção de código com filtros x86; anexa `PackHeader + compressedData`; e no unpack valida truncamento, `CAdler`, `UAdler`, tamanho da seção e filtro inverso. Ainda não cria `.UPX0/.UPX1`, não injeta stub executável real, não ajusta entry point e não recalcula checksum PE.

---

## 17. Handlers: macOS Mach-O

Estado atual: scaffold pack/unpack implementado para Mach-O thin little-endian i386 e amd64.

O fluxo atual detecta magic Mach-O 32/64, CPU type, load commands `LC_SEGMENT`/`LC_SEGMENT_64` e seção `__TEXT,__text`; comprime o payload com filtros x86; anexa `PackHeader + compressedData`; e no unpack valida checksums, limites e filtro inverso. O scaffold de handler continua aceitando apenas Mach-O thin; no fluxo externo, candidatos fat/universal podem ter um slice thin i386/amd64 extraído antes da detecção. Code signing, relocação real e loader executável real continuam fora do scaffold.

---

## 18. Handlers: Formatos Legados e Embarcados

Estado atual: scaffold pack/unpack implementado para DOS EXE, DOS COM sintético, Atari TOS, PS1 EXE e vmlinuz i386 sintético.

O fluxo legado usa markers/headers sintéticos controlados pelos testes para localizar payload, comprime esse payload, anexa `PackHeader + compressedData` e restaura bytes originais no unpack. DOS COM real não possui magic confiável; por isso o scaffold atual usa marker `COM!`. vmlinuz usa `MZ` + `HdrS` e campos sintéticos de offset/tamanho, não um parser completo de bzImage real.

---

## 18.1 Matriz de Status dos Formatos

| Formato | Handler scaffold | Pack/unpack sintético | Stub real | Compat UPX oficial | Fixture real |
|---|---:|---:|---:|---:|---:|
| Win32 PE | Sim | Sim | Não | Não | Opt-in |
| Win64 PE | Sim | Sim | Não | Não | Opt-in |
| Linux ELF i386 | Sim | Sim | Não | Não | Opt-in |
| Linux ELF amd64 | Sim | Sim | Não | Não | Opt-in |
| Linux ELF ARM32 | Sim | Sim | Não | Não | Opt-in |
| Linux ELF ARM64 | Sim | Sim | Não | Não | Opt-in |
| Linux ELF MIPS32 LE | Sim | Sim | Não | Não | Opt-in |
| Linux ELF MIPS32 BE | Sim | Sim | Não | Não | Opt-in |
| Linux ELF PPC32 BE | Sim | Sim | Não | Não | Opt-in |
| Linux ELF PPC64 BE/LE | Sim | Sim | Não | Não | Opt-in |
| Linux ELF RISC-V64 | Sim | Sim | Não | Não | Opt-in |
| Mach-O i386 | Sim | Sim | Não | Não | Opt-in |
| Mach-O amd64 | Sim | Sim | Não | Não | Opt-in |
| vmlinuz i386 | Sim | Sim | Não | Não | Opt-in |
| DOS COM | Sim | Sim | Não | Não | Opt-in |
| DOS EXE | Sim | Sim | Não | Não | Opt-in |
| Atari TOS | Sim | Sim | Não | Não | Opt-in |
| PS1 EXE | Sim | Sim | Não | Não | Opt-in |

Bloqueadores finais para compatibilidade real:

- Importar stubs pré-compilados reais do UPX original e marcar manifesto como `real`.
- Adicionar fixtures reais ou pipeline com `UPXSHARP_REAL_FIXTURES_DIR`.
- Validar arquivos comprimidos pelo UPX oficial via `UPXSHARP_UPX_PACKED_FIXTURES_DIR`.
- Evoluir handlers scaffold para reescrita real de formato, loader executável e entry point.

---

## 19. Options — Interface de Linha de Comando

### Options.cs

```csharp
// Core/Options.cs
namespace UpxSharp.Core;

public sealed class Options
{
    // ── Operação ──────────────────────────────────────────────────────────────
    public UpxCommand        Command        { get; set; } = UpxCommand.Compress;
    public string?           OutputFile     { get; set; }

    // ── Compressão ────────────────────────────────────────────────────────────
    public CompressionMethod Method         { get; set; } = CompressionMethod.Nrv2eLE32;
    public int               Level          { get; set; } = -1;   // -1 = auto
    public int               FilterId       { get; set; } = -1;   // -1 = auto
    public bool              Brute          { get; set; }
    public bool              UltraBrute     { get; set; }

    // ── Comportamento ─────────────────────────────────────────────────────────
    public int               Verbose        { get; set; } = 1;
    public int               Force          { get; set; }
    public bool              Backup         { get; set; }
    public int               Overlay        { get; set; }  // 0=copy,1=strip,2=warn

    // ── Específico por formato ────────────────────────────────────────────────
    public bool              CompressExports{ get; set; } = true;   // PE
    public bool              CompressIcons  { get; set; } = true;   // PE
    public bool              CompressResources { get; set; } = true;// PE
    public bool              AndroidShlib   { get; set; }           // ELF Android
    public bool              PreserveBuildId{ get; set; }           // ELF Linux

    /// <summary>Retorna níveis de compressão a testar baseado nas opções.</summary>
    public IEnumerable<int> GetLevels()
    {
        if (Level > 0)         return [Level];
        if (Brute)             return [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        if (UltraBrute)        return Enumerable.Range(1, 10);
        return [3, 6, 9]; // padrão: rápido, médio, máximo
    }
}
```

### Program.cs (CLI)

```csharp
// UpxSharp.Cli/Program.cs
using UpxSharp.Core;
using UpxSharp.IO;
using UpxSharp.Packers;

var options = new Options();
var files   = new List<string>();

// Parse de argumentos
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "-d" or "--decompress": options.Command = UpxCommand.Decompress; break;
        case "-t" or "--test":       options.Command = UpxCommand.Test;       break;
        case "-l" or "--list":       options.Command = UpxCommand.List;       break;
        case "-1": options.Level = 1; break;
        case "-9": options.Level = 9; break;
        case "--best":         options.Level = 9; break;
        case "--brute":        options.Brute = true; break;
        case "--ultra-brute":  options.UltraBrute = true; break;
        case "--lzma":         options.Method = CompressionMethod.Lzma; break;
        case "--zstd":         options.Method = CompressionMethod.Zstd; break;
        case "-k" or "--backup":options.Backup = true; break;
        case "-f" or "--force": options.Force++; break;
        case "-v" or "--verbose": options.Verbose++; break;
        case "-q" or "--quiet":   options.Verbose = 0; break;
        case "-o": options.OutputFile = args[++i]; break;
        default:
            if (!args[i].StartsWith('-')) files.Add(args[i]);
            break;
    }
}

// Processa cada arquivo
int exitCode = 0;
foreach (var filePath in files)
{
    try
    {
        using var input  = new InputFile(filePath);
        var outPath      = options.OutputFile ?? filePath;
        using var output = new OutputFile(outPath, overwrite: true);

        var packer = options.Command switch
        {
            UpxCommand.Decompress => PackMaster.FindUnpacker(input, options),
            _                     => PackMaster.FindPacker(input, options),
        };

        if (options.Command == UpxCommand.Decompress)
            packer.DoUnpack(output, options);
        else
            packer.DoPack(output, options);

        if (options.Verbose > 0)
            Console.WriteLine($"OK: {filePath}");
    }
    catch (UpxException ex)
    {
        Console.Error.WriteLine($"ERRO: {filePath}: {ex.Message}");
        exitCode = 1;
    }
}

return exitCode;
```

---

## 20. Dependências NuGet

### UpxSharp.csproj (completo)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>13</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <Optimize>true</Optimize>
    <AssemblyName>UpxSharp</AssemblyName>
    <RootNamespace>UpxSharp</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <EmbeddedResource Include="Stubs/Resources/*.stub" />
  </ItemGroup>

  <ItemGroup>
    <!-- Compressão LZMA gerenciada (portável, sem P/Invoke) -->
    <PackageReference Include="ManagedLzma" Version="0.0.1-alpha5" />

    <!-- Compressão Zstandard (portado para C#) -->
    <PackageReference Include="ZstdSharp.Port" Version="0.8.*" />

    <!-- UCL / NRV: usar fork portado ou implementar via unsafe -->
    <!-- Alternativa: usar SharpZipLib para deflate -->
    <PackageReference Include="SharpZipLib" Version="1.4.*" />

    <!-- Alta performance: Span, ArrayPool, MemoryOwner -->
    <PackageReference Include="Microsoft.Toolkit.HighPerformance" Version="7.1.*" />
  </ItemGroup>
</Project>
```

### UpxSharp.Tests.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>13</LangVersion>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="xunit"          Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
    <PackageReference Include="FluentAssertions" Version="6.*" />
    <ProjectReference Include="../../src/UpxSharp/UpxSharp.csproj" />
  </ItemGroup>
</Project>
```

---

## 21. Testes

```csharp
// tests/UpxSharp.Tests/Adler32Tests.cs
public class Adler32Tests
{
    [Fact]
    public void Compute_EmptyBuffer_ReturnsOne()
        => Adler32.Compute([]).Should().Be(1);

    [Fact]
    public void Compute_KnownValue()
    {
        // "Wikipedia" → Adler-32 = 0x11E60398 (valor de referência)
        byte[] data = "Wikipedia"u8.ToArray();
        Adler32.Compute(data).Should().Be(0x11E60398);
    }

    [Fact]
    public void Verify_ThrowsOnMismatch()
    {
        byte[] data = [1, 2, 3];
        Assert.Throws<CorruptedPackException>(
            () => Adler32.Verify(data, expected: 0xDEADBEEF, "teste"));
    }
}

// tests/UpxSharp.Tests/PackHeaderTests.cs
public class PackHeaderTests
{
    [Fact]
    public void RoundTrip_SerializeDeserialize()
    {
        var original = new PackHeader
        {
            Version  = 11,
            Format   = UpxFormat.LinuxElfi386,
            Method   = CompressionMethod.Nrv2eLE32,
            Level    = 8,
            UAdler   = 0xAABBCCDD,
            CAdler   = 0x11223344,
            ULen     = 1_024_000,
            CLen     = 512_000,
            UFileSize= 1_025_024,
            FilterId = 0x49,
        };

        Span<byte> buf = stackalloc byte[PackHeader.SerializedSize];
        original.Write(buf);
        var restored = PackHeader.Read(buf);

        restored.Version.Should().Be(original.Version);
        restored.ULen.Should().Be(original.ULen);
        restored.CLen.Should().Be(original.CLen);
        restored.FilterId.Should().Be(original.FilterId);
    }

    [Fact]
    public void TryFind_DetectsMagicInLargerBuffer()
    {
        var header = new PackHeader { Version = 11 };
        byte[] buf = new byte[256];
        header.Write(buf.AsSpan(128)); // insere no offset 128

        bool found = PackHeader.TryFind(buf, out int offset, out _);
        found.Should().BeTrue();
        offset.Should().Be(128);
    }
}

// tests/UpxSharp.Tests/BinaryEndianTests.cs
public class BinaryEndianTests
{
    [Fact]
    public void GetLE32_ReadsCorrectly()
    {
        byte[] buf = [0x78, 0x56, 0x34, 0x12];
        BinaryEndian.GetLE32(buf).Should().Be(0x12345678);
    }

    [Fact]
    public void GetBE32_ReadsCorrectly()
    {
        byte[] buf = [0x12, 0x34, 0x56, 0x78];
        BinaryEndian.GetBE32(buf).Should().Be(0x12345678);
    }

    [Fact]
    public void SetAndGet_RoundTrip()
    {
        byte[] buf = new byte[4];
        BinaryEndian.SetLE32(buf, 0xDEADBEEF);
        BinaryEndian.GetLE32(buf).Should().Be(0xDEADBEEF);
    }
}

// tests/UpxSharp.Tests/CompressionTests.cs
public class CompressionTests
{
    [Theory]
    [InlineData(CompressionMethod.Lzma)]
    [InlineData(CompressionMethod.Zstd)]
    public void Compress_Decompress_RoundTrip(CompressionMethod method)
    {
        byte[] original = Enumerable.Range(0, 10_000)
            .Select(i => (byte)(i % 256)).ToArray();

        var compressor = CompressorFactory.Create(method);
        byte[] compressed = new byte[original.Length + 1024];
        int cLen = compressor.Compress(original, compressed, level: 6);
        cLen.Should().BePositive();

        byte[] restored = new byte[original.Length];
        compressor.Decompress(compressed.AsSpan(0, cLen), restored);

        restored.Should().Equal(original);
    }
}
```

### Testes opt-in com fixtures reais

Por padrão a suíte usa fixtures sintéticos e não depende de binários externos. Para validar binários reais localmente, configure variáveis de ambiente antes de rodar `dotnet test`:

```powershell
$env:UPXSHARP_REAL_FIXTURES_DIR="C:\path\originais"
$env:UPXSHARP_UPX_PACKED_FIXTURES_DIR="C:\path\upx-packed"
$env:UPXSHARP_REAL_FIXTURES_INVENTORY="C:\path\fixture-inventory.json"
$env:UPXSHARP_REAL_FIXTURES_REPORT_DIR="C:\path\reports"
dotnet test UpxSharp.sln
```

`UPXSHARP_REAL_FIXTURES_DIR` executa pack → unpack → comparação byte-a-byte para arquivos reconhecidos. `UPXSHARP_UPX_PACKED_FIXTURES_DIR` exercita a detecção de unpacker contra arquivos comprimidos pelo UPX oficial sem tornar a suíte padrão dependente desses artefatos.

Quando `UPXSHARP_REAL_FIXTURES_INVENTORY` está definido, os testes strict exigem cobertura por formato implementado. O inventário declara `format`, `path` do binário original e `packedPath` do arquivo comprimido pelo UPX oficial. A matriz esperada é: `Win32Pe`, `Win64Pe`, `LinuxElfi386`, `LinuxElfAmd64`, `LinuxElfArmLE`, `LinuxElfArm64LE`, `LinuxElfMipsel`, `LinuxElfMips`, `LinuxElf32Ppc`, `LinuxElf64Ppc`, `LinuxElfRiscv64`, `MachI386`, `MachAMD64`, `Vmlinuzi386`, `DosCom`, `DosExe`, `AtariTos`, `Ps1Exe`.

O inventário pode ser gerado a partir dos diretórios de artefatos com:

```powershell
.\tools\build-fixture-inventory.ps1 -RealFixturesDir C:\path\originais -UpxPackedFixturesDir C:\path\upx-packed -OutputPath C:\path\fixture-inventory.json
```

Use `-DryRun` para ver a matriz detectada sem gravar arquivo.

Sem `-DryRun`, o gerador também cria `fixture-missing-report.json`, que lista por formato o que ainda falta (`MissingOriginal`, `MissingPacked`, `PackedOrphan` ou `Ready`). Esse relatório guia o fechamento das duas pendências externas restantes.

O gate final aceita somente fixtures externas. Arquivos gerados pelo próprio projeto, como `src\UpxSharp.Cli\bin\...\UpxSharp.Cli.exe`, podem ser usados como smoke tests locais, mas não contam para fechar “binários reais” nem “compatibilidade UPX oficial”.

Quando alguma dessas variáveis está ativa, os testes geram relatórios JSON (`upxsharp-real-fixtures-report.json` e `upxsharp-upx-packed-fixtures-report.json`) em `UPXSHARP_REAL_FIXTURES_REPORT_DIR` ou na pasta temporária do sistema. Defina `UPXSHARP_REAL_FIXTURES_STRICT=1` para transformar falhas de fixtures reconhecidas em falha de teste.

### Importação opt-in de stubs reais

Os arquivos em `src/UpxSharp/Stubs/Resources/*.stub` continuam sendo stubs fake de scaffold até existirem artefatos reais do UPX. O manifesto `stubs.manifest.json` registra para cada stub o formato, nome do resource, tipo (`fake` ou `real`), tamanho e SHA-256; `StubRepository` valida esses dados ao carregar.

Para preparar stubs a partir de artefatos oficiais, use:

```powershell
.\tools\fetch-upx-official-artifacts.ps1
```

Esse script baixa `upx-5.2.0-src.tar.xz` e os binários oficiais publicados pela release `upx/upx` para Windows, Linux e DOS, valida os SHA-256 publicados pela API da release, extrai o source tree, extrai `upx.exe`, converte headers oficiais em `C:\upxsharp-artifacts\real-stubs\*.stub` e classifica os executáveis oficiais extraídos em `official-binaries-report.json`. Binários oficiais que já chegam comprimidos pelo UPX são registrados como packed órfãos até existir o original externo correspondente.

Para importar stubs reais locais, prepare um diretório com os nomes esperados para todos os formatos scaffold (`i386-linux.elf.stub`, `amd64-linux.elf.stub`, `arm-linux.elf.stub`, `arm64-linux.elf.stub`, `mipsel-linux.elf.stub`, `mips-linux.elf.stub`, `ppc32-linux.elf.stub`, `ppc64-linux.elf.stub`, `riscv64-linux.elf.stub`, `i386-mach.macho.stub`, `amd64-mach.macho.stub`, `i386-vmlinuz.stub`, `dos-com.stub`, `dos-exe.stub`, `atari-tos.stub`, `ps1-exe.stub`, `i386-win32.pe.stub`, `amd64-win64.pe.stub`) e rode:

```powershell
.\tools\import-real-stubs.ps1 -SourceDir C:\path\real-stubs
dotnet test UpxSharp.sln
```

O script copia os arquivos para `Stubs/Resources`, recalcula o manifesto e marca os itens como `real`. A checklist de stubs reais só deve ser marcada quando esses artefatos reais estiverem disponíveis no workspace ou pipeline.

### Fechamento das Pendências Externas

As pendências finais exigem artefatos externos e não devem ser marcadas como concluídas apenas com fixtures sintéticos:

- Stubs reais: todos os itens de `stubs.manifest.json` devem estar com `kind = "real"` após `tools/import-real-stubs.ps1`. Estado atual: fechado para os 18 formatos scaffold.
- Binários reais: `UPXSHARP_REAL_FIXTURES_DIR` e `UPXSHARP_REAL_FIXTURES_INVENTORY` devem cobrir todos os formatos implementados.
- Compatibilidade UPX oficial: `UPXSHARP_UPX_PACKED_FIXTURES_DIR` deve conter os `packedPath` do inventário, gerados pelo UPX original.

O relatório opt-in separa layouts ELF oficiais válidos ainda não implementados (`OfficialElfUnsupportedLayout`) de metadata corrompida ou incompleta (`OfficialUpxMetadataError`) e de falhas de payload (`OfficialUpxDecompressError`). Somente `UpxPackedUnpackOk` satisfaz o gate de compatibilidade; nenhum desses diagnósticos intermediários fecha a pendência.

Para coletar candidatos reais sem usar binários do próprio projeto:

```powershell
.\tools\fetch-real-fixture-candidates.ps1
```

A primeira execução cria `C:\upxsharp-artifacts\fixture-candidate-sources.json`. Preencha `url` ou `localPath` com artefatos externos, rode novamente e depois use `tools/import-external-fixtures.ps1`.

O coletor aceita `.com` para `DosCom` quando o detector reconhece o arquivo, e tenta extrair slices thin i386/amd64 de Mach-O fat/universal antes de classificar como `FatMachUnsupported`.

Use o gate final abaixo depois de importar os stubs e preparar os diretórios:

```powershell
$env:UPXSHARP_REAL_FIXTURES_DIR="C:\path\originais"
$env:UPXSHARP_UPX_PACKED_FIXTURES_DIR="C:\path\upx-packed"
$env:UPXSHARP_REAL_FIXTURES_INVENTORY="C:\path\fixture-inventory.json"
$env:UPXSHARP_REAL_FIXTURES_REPORT_DIR="C:\path\reports"
.\tools\verify-external-artifacts.ps1
```

O script `verify-external-artifacts.ps1` não baixa artefatos. Ele valida manifesto real, existência de fixtures e executa `dotnet test UpxSharp.sln` com `UPXSHARP_REAL_FIXTURES_STRICT=1`. Se faltar qualquer artefato, ele falha de forma controlada e explica o bloqueio.

Runbook operacional completo: `Docs/EXTERNAL_ARTIFACTS_RUNBOOK.md`.

---

## 22. Checklist de Implementação

### Fase 1 — Fundações (sem dependências externas)
- [x] `BinaryEndian.cs` — leitura/escrita LE/BE/TE
- [x] `Adler32.cs` — checksum
- [x] `MemBuffer.cs` — buffer seguro com limite de 768 MiB
- [x] `UpxException.cs` — hierarquia de exceções
- [x] `Constants.cs` — `UpxFormat`, `CompressionMethod`, constantes
- [x] `InputFile.cs` / `OutputFile.cs` — I/O com `RandomAccess`
- [x] **Testes unitários** das classes acima

### Fase 2 — Estruturas de Metadados
- [x] `PackHeader.cs` — serialização/desserialização completa
- [x] `PackHeader.TryFind()` — busca por magic bytes
- [x] `Options.cs` — todos os campos
- [x] **Testes** de round-trip do PackHeader

### Fase 3 — Compressão
- [x] `ICompressor.cs` — interface
- [x] `LzmaCompressor.cs` via `ManagedLzma`
- [x] `ZstdCompressor.cs` via `ZstdSharp.Port`
- [x] `UclCompressor.cs` — NRV2B/D/E (encoder literal-only + decoder LE32)
- [x] `CompressorFactory.cs`
- [x] **Testes** de round-trip para cada algoritmo

### Fase 4 — Filtros
- [x] `FilterState.cs` + `IFilter.cs`
- [x] `FilterX86.cs` — x86/x86-64 CALL/JMP
- [x] `FilterArmThumb.cs` — ARM Thumb branches
- [x] `FilterArm32.cs` — ARM 32-bit branches
- [x] `FilterMips.cs` — MIPS branches
- [x] `FilterPpc.cs` — PowerPC branches
- [x] `FilterFactory.cs`
- [x] **Testes** de filter/unfilter em código sintético

### Fase 5 — Packer Core
- [x] `PackerBase.cs` — interface abstrata
- [x] `Packer.cs` — `CompressWithFilters()` com paralelismo
- [x] `PackMaster.cs` — lista de factories e detecção
- [x] **Teste** de `PackMaster` com arquivos de cada formato

### Fase 6 — Linker e Stubs
- [x] `ElfLinker.cs` — carregamento e parsing de stubs
- [x] Subclasses por arquitetura (AMD64, ARM32, ARM64, MIPS, PPC)
- [x] `StubRepository.cs` — carregamento de embedded resources
- [x] Manifesto e validação opt-in para stubs reais
- [x] Cobertura de stubs fake para todos os formatos scaffold implementados
- [x] Copiar stubs pré-compilados do UPX original para `Stubs/Resources/`

### Fase 7 — Handlers (por prioridade)
- [x] `PackLinuxElf32x86.cs` — Linux i386 ELF (mais documentado)
- [x] `PackLinuxElf64Amd.cs` — Linux x86-64 ELF
- [x] `PeFile32.cs` — Windows PE 32-bit
- [x] `PeFile64.cs` — Windows PE 64-bit
- [x] `PackLinuxElf32Arm.cs` — Android/ARM 32-bit
- [x] `PackLinuxElf64Arm.cs` — Android/ARM 64-bit
- [x] `PackLinuxElf32Mips.cs`
- [x] `PackLinuxElf32MipsBe.cs`
- [x] `PackLinuxElf32Ppc.cs`
- [x] `PackLinuxElf64Ppc.cs`
- [x] `PackLinuxElf64Riscv.cs`
- [x] `PackMachI386.cs` / `PackMachAMD64.cs`
- [x] `PackVmlinuzI386.cs`
- [x] Handlers legados (DOS, Atari, PS1)

### Fase 8 — CLI
- [x] Parser de argumentos em `Program.cs`
- [x] Tratamento de erros com mensagens amigáveis
- [x] Modo `--test` (comprime + descomprime + verifica)
- [x] Modo `--list` (inspeciona sem alterar)
- [x] Suporte a wildcards (`*.exe`)

### Fase 9 — Testes de Integração
- [x] Round-trip completo: pack → unpack → comparar com original
- [ ] Compatibilidade com arquivos comprimidos pelo UPX original (C++) — 10/12 fixtures atuais passam byte a byte; PE exige fixtures sem Authenticode e ainda requer reconstrução dos dados omitidos pelo compactador. O gate 18/18 proposto precisa de exceção explícita para `Vmlinuzi386`: o UPX 5.2.0 descompacta o kernel interno, não preserva a imagem `vmlinuz` original e seu próprio `unpack()` recusa gerar o arquivo (`build a new kernel instead`).
- [ ] Testes com binários reais de cada plataforma — faltam fixtures públicas licenciadas para `AtariTos`, `DosCom`, `MachI386`, `MachAMD64`, `Ps1Exe` e `Vmlinuzi386`; nenhuma fixture gerada pelo projeto pode fechar este gate.
- [x] Teste de rejeição de arquivos corrompidos

---

*Documento gerado em 15/06/2026 — C# 13 / .NET 10 — branch `devel` do repositório upx/upx*
