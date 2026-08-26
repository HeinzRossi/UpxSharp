# UpxSharp External Artifacts Runbook

Este runbook fecha as pendencias que dependem de artefatos externos. O fluxo preferido baixa artefatos oficiais do UPX e deriva os stubs reais a partir dos headers oficiais; fixtures reais ainda precisam ser fornecidas localmente.

## Estrutura Esperada

```text
C:\upxsharp-artifacts\
  real-stubs\
    i386-linux.elf.stub
    amd64-linux.elf.stub
    arm-linux.elf.stub
    arm64-linux.elf.stub
    mipsel-linux.elf.stub
    mips-linux.elf.stub
    ppc32-linux.elf.stub
    ppc64-linux.elf.stub
    riscv64-linux.elf.stub
    i386-mach.macho.stub
    amd64-mach.macho.stub
    i386-vmlinuz.stub
    dos-com.stub
    dos-exe.stub
    atari-tos.stub
    ps1-exe.stub
    i386-win32.pe.stub
    amd64-win64.pe.stub
  real-fixtures\
    original executables here
  upx-packed-fixtures\
    files packed by official UPX here
  fixture-inventory.json
  fixture-missing-report.json
  fixture-acquisition-plan.json
  reports\
```

## Baixar Artefatos Oficiais UPX

```powershell
.\tools\fetch-upx-official-artifacts.ps1
```

O script baixa o UPX oficial 5.2.0, valida os SHA-256 publicados pela release do GitHub, extrai o source tarball, extrai `upx.exe`, converte headers oficiais de `src\stub\*.h` em stubs reais e baixa os binarios oficiais publicados para Windows, Linux e DOS.

Ele tambem extrai os binarios oficiais em `C:\upxsharp-artifacts\official-binaries`, detecta cada `upx`/`upx.exe` com o mesmo caminho do `PackMaster` e grava `official-binaries-report.json`. Binarios oficiais que ja estiverem comprimidos pelo UPX sao copiados apenas para `upx-packed-fixtures`; eles sao packed oficiais orfaos ate existir um original correspondente em `real-fixtures`.

Estado observado com UPX 5.2.0: os binarios oficiais baixados preencheram packed oficiais para `Win32Pe`, `Win64Pe`, `LinuxElfi386`, `LinuxElfAmd64`, `LinuxElfArmLE`, `LinuxElfArm64LE`, `LinuxElfMipsel`, `LinuxElfMips`, `LinuxElf32Ppc`, `LinuxElf64Ppc`, `LinuxElfRiscv64` e `DosExe`. Com os originais restaurados/coletados, esses 12 formatos ficam `Ready` no inventario; o unpack nativo de packed oficial ja entra na trilha de metadata oficial. Para ELF, o parser le `l_info`, `p_info`, a sequencia de `b_info` ate o marcador EOF quando disponivel e os `Ehdr`/`Phdrs` originais descomprimidos, reportando `elf segment rebuild pending` ate a remontagem dos segmentos. PE e DOS EXE continuam com parser oficial especifico pendente. Ainda faltam originais e packed oficiais para `AtariTos`, `DosCom`, `MachI386`, `MachAMD64`, `Vmlinuzi386` e `Ps1Exe`.

## Inventario de Fixtures

O gate final usa `C:\upxsharp-artifacts\fixture-inventory.json` por padrao. Cada entrada declara o formato esperado, o caminho relativo do binario original e o caminho relativo do arquivo comprimido pelo UPX oficial:

```json
{
  "fixtures": [
    {
      "format": "Win64Pe",
      "path": "sample-win64.exe",
      "packedPath": "sample-win64.exe.upx"
    }
  ]
}
```

Formatos exigidos para fechar as pendencias finais: `Win32Pe`, `Win64Pe`, `LinuxElfi386`, `LinuxElfAmd64`, `LinuxElfArmLE`, `LinuxElfArm64LE`, `LinuxElfMipsel`, `LinuxElfMips`, `LinuxElf32Ppc`, `LinuxElf64Ppc`, `LinuxElfRiscv64`, `MachI386`, `MachAMD64`, `Vmlinuzi386`, `DosCom`, `DosExe`, `AtariTos`, `Ps1Exe`.

Para gerar o inventario a partir dos arquivos existentes:

```powershell
.\tools\build-fixture-inventory.ps1 `
  -RealFixturesDir C:\upxsharp-artifacts\real-fixtures `
  -UpxPackedFixturesDir C:\upxsharp-artifacts\upx-packed-fixtures `
  -OutputPath C:\upxsharp-artifacts\fixture-inventory.json
```

Use `-DryRun` para imprimir a tabela `Format | Original | Packed | Status` sem gravar arquivo. Sem `-DryRun`, o gerador tambem grava `fixture-missing-report.json`.

## Relatorio de Faltantes

`fixture-missing-report.json` e o checklist operacional. Cada formato recebe:

- `needsOriginal`
- `needsPacked`
- `detectedOriginal`
- `detectedPacked`
- `status`: `Ready`, `MissingOriginal`, `MissingPacked`, ou `PackedOrphan`

Comandos uteis:

```powershell
$items = New-Object System.Collections.Generic.List[object]
foreach ($item in (Get-Content C:\upxsharp-artifacts\fixture-missing-report.json -Raw | ConvertFrom-Json)) {
  $items.Add($item) | Out-Null
}
$items | Group-Object status | Select-Object Name,Count
```

```powershell
$items = New-Object System.Collections.Generic.List[object]
foreach ($item in (Get-Content C:\upxsharp-artifacts\fixture-missing-report.json -Raw | ConvertFrom-Json)) {
  $items.Add($item) | Out-Null
}
$items | Where-Object needsOriginal | Select-Object format,status,detectedPacked
```

Preencha `real-fixtures` e `upx-packed-fixtures` ate todos os formatos ficarem `Ready`.

## Plano de Aquisicao

Gere o plano de aquisicao a partir do relatorio de faltantes:

```powershell
.\tools\build-fixture-acquisition-plan.ps1 `
  -MissingReportPath C:\upxsharp-artifacts\fixture-missing-report.json `
  -OutputPath C:\upxsharp-artifacts\fixture-acquisition-plan.json
```

Cada entrada traz `format`, `needsOriginal`, `needsPacked`, nomes sugeridos, packed orfao atual quando existir, e `packedFixtureStatus`/`packedFixtureBlocker`/`packedFixtureError` quando o relatorio opt-in de packed fixtures existir. No estado atual, 12 formatos tem original + packed detectados. O status `OfficialElfUnsupportedLayout` indica metadata ELF valida em um layout ainda nao implementado; ele nao deve ser tratado como `OfficialUpxMetadataError`, que continua reservado para metadata corrompida ou incompleta. `OfficialUpxDecompressError` identifica falha no payload. 6 ainda precisam de original + packed: `AtariTos`, `DosCom`, `MachI386`, `MachAMD64`, `Vmlinuzi386` e `Ps1Exe`.

Fixtures do proprio repositorio, como `src\UpxSharp.Cli\bin\...\UpxSharp.Cli.exe`, podem servir para smoke tests locais, mas nao contam para o gate externo. O gerador e o verificador ignoram ou rejeitam caminhos dentro de `C:\Projetos\UpxSharp`; coloque binarios reais externos em `C:\upxsharp-artifacts\real-fixtures`.

## Buscar Candidatos Reais

Use `fetch-real-fixture-candidates.ps1` para baixar ou copiar candidatos declarados em um manifesto. O script nao inventa fixtures, nao sintetiza headers e nao aceita binarios do proprio repositorio.

Para preencher o manifesto com fontes publicas conhecidas quando possivel, rode:

```powershell
.\tools\populate-fixture-candidate-sources.ps1
```

Esse helper resolve pacotes Debian/Debian Ports por `Packages.gz`, adiciona candidatos Windows publicos conhecidos e deixa entradas `UNRESOLVED-*` para formatos raros sem fonte confiavel configurada. Ele registra `url`, `license` e `notes`; se uma fonte nao for resolvida, a entrada permanece pendente em vez de criar fixture falsa.

Se preferir manifesto manual, a primeira execucao do fetch cria um template:

```powershell
.\tools\fetch-real-fixture-candidates.ps1
```

Edite `C:\upxsharp-artifacts\fixture-candidate-sources.json` com URLs publicas ou caminhos locais externos:

```json
{
  "sources": [
    {
      "format": "Win64Pe",
      "sourceName": "vendor-tool-1.0",
      "url": "https://example.org/releases/vendor-tool-win64.zip",
      "localPath": null,
      "archivePath": null,
      "fileGlobs": [ "*.exe" ],
      "license": "MIT",
      "notes": "Original upstream release"
    }
  ]
}
```

Depois rode:

```powershell
.\tools\fetch-real-fixture-candidates.ps1 `
  -CandidateManifestPath C:\upxsharp-artifacts\fixture-candidate-sources.json
```

Resultados reconhecidos por `UpxSharp.Cli --detect-fixture pack` sao copiados para `C:\upxsharp-artifacts\incoming-real-binaries`. O relatorio `real-fixture-candidates-report.json` registra `source`, `license`, formato detectado e status (`CopiedRecognized`, `NotRecognized`, `WrongFormat`, `RepoLocalSkipped`, etc.).

Status importantes do relatorio:

- `CopiedRecognized`: candidato externo reconhecido e copiado para importacao.
- `NoTrustedSource`: ainda nao ha URL/caminho externo confiavel configurado para o formato.
- `BlockedByScaffold`: arquivo candidato existe, mas o handler atual ainda so aceita scaffold/sinalizacao interna.
- `FatMachUnsupported`: arquivo Mach-O universal/fat foi encontrado, mas nao havia slice thin i386/amd64 compativel para extrair.
- `ExtractedFatMachSlice`: arquivo Mach-O universal/fat tinha slice thin compativel; o slice extraido ainda precisa ser reconhecido pelo detector antes de entrar no inventario.

Estado observado apos a coleta web atual: 12 formatos ficaram `Ready` no inventario; 6 seguem bloqueados por falta de fonte confiavel/original externo: `AtariTos`, `DosCom`, `MachAMD64`, `MachI386`, `Ps1Exe` e `Vmlinuzi386`. O pipeline aceita arquivos `.com` quando o detector reconhece `DosCom`, e candidatos Mach-O fat/universal podem ser materializados como slice thin antes da deteccao. Os packed oficiais detectados ja passam pelo detector/parser nativo; ELF fica em `elf segment rebuild pending` ate existir reconstrucao completa do container, e PE/DOS ainda exigem parser oficial dedicado.

Nos relatorios de packed fixtures, use estes diagnosticos:

- `OfficialElfUnsupportedLayout`: ELF oficial estruturalmente valido, mas fora dos layouts nativos implementados.
- `OfficialUpxMetadataError`: metadata oficial corrompida, truncada ou inconsistente.
- `OfficialUpxDecompressError`: metadata aceita, mas o payload nao pode ser descomprimido com seguranca.
- `UpxPackedUnpackOk`: unpack nativo concluido; unico estado que satisfaz o gate de compatibilidade.

## Materializar Packed Orphans Oficiais

Alguns formatos podem ter `packed` oficial baixado do UPX, mas nao ter o original correspondente. Quando `fixture-missing-report.json` mostrar `PackedOrphan`, use o UPX oficial local para restaurar o original:

```powershell
.\tools\materialize-official-orphan-fixtures.ps1
```

O script usa `C:\upxsharp-artifacts\upx-bin\upx.exe -d`, grava originais em `real-fixtures`, valida cada arquivo com `UpxSharp.Cli --detect-fixture pack` e escreve `materialized-official-orphans-report.json`. O original so entra no gate quando o formato detectado bate com o formato esperado.

## Importar Fixtures Externas

Depois de colocar binarios reais fora do repositorio, use o importador assistido:

```powershell
.\tools\import-external-fixtures.ps1 -SourceDir C:\caminho\para\binarios-externos
```

O importador:

- ignora arquivos dentro do repositorio;
- detecta formato com o mesmo caminho do `PackMaster`;
- copia originais reconhecidos para `C:\upxsharp-artifacts\real-fixtures`;
- usa `C:\upxsharp-artifacts\upx-bin\upx.exe` para criar packed oficiais em `upx-packed-fixtures`;
- reconstrui `fixture-inventory.json` e `fixture-missing-report.json`.

Use `-DryRun` para ver a tabela sem copiar nem empacotar. Use `-Strict` para falhar em duplicidade, arquivo nao reconhecido ou falha do UPX oficial.

Se um packed alvo ja existir em `upx-packed-fixtures`, o importador reaproveita o arquivo e nao chama `upx.exe`. Isso ajuda a completar manualmente casos onde o UPX oficial nao roda no host atual.

## Importar Stubs Reais

```powershell
.\tools\import-real-stubs.ps1 -SourceDir C:\upxsharp-artifacts\real-stubs
```

O importador copia os stubs para `src\UpxSharp\Stubs\Resources` e atualiza `stubs.manifest.json` com `kind = "real"`, tamanho e SHA-256.

## Rodar Gate Final

```powershell
$env:UPXSHARP_REAL_FIXTURES_DIR="C:\upxsharp-artifacts\real-fixtures"
$env:UPXSHARP_UPX_PACKED_FIXTURES_DIR="C:\upxsharp-artifacts\upx-packed-fixtures"
$env:UPXSHARP_REAL_FIXTURES_INVENTORY="C:\upxsharp-artifacts\fixture-inventory.json"
$env:UPXSHARP_REAL_FIXTURES_REPORT_DIR="C:\upxsharp-artifacts\reports"
.\tools\verify-external-artifacts.ps1
```

O gate valida stubs reais, inventario completo, fixtures originais, fixtures packed oficiais e `dotnet test UpxSharp.sln` com `UPXSHARP_REAL_FIXTURES_STRICT=1`.

Fluxo completo recomendado:

```powershell
.\tools\fetch-upx-official-artifacts.ps1
.\tools\import-real-stubs.ps1 -SourceDir C:\upxsharp-artifacts\real-stubs
.\tools\populate-fixture-candidate-sources.ps1
.\tools\fetch-real-fixture-candidates.ps1 -CandidateManifestPath C:\upxsharp-artifacts\fixture-candidate-sources.json
.\tools\materialize-official-orphan-fixtures.ps1
.\tools\import-external-fixtures.ps1 -SourceDir C:\caminho\para\binarios-externos
.\tools\build-fixture-inventory.ps1 -RealFixturesDir C:\upxsharp-artifacts\real-fixtures -UpxPackedFixturesDir C:\upxsharp-artifacts\upx-packed-fixtures -OutputPath C:\upxsharp-artifacts\fixture-inventory.json
$env:UPXSHARP_REAL_FIXTURES_DIR="C:\upxsharp-artifacts\real-fixtures"
$env:UPXSHARP_UPX_PACKED_FIXTURES_DIR="C:\upxsharp-artifacts\upx-packed-fixtures"
$env:UPXSHARP_REAL_FIXTURES_INVENTORY="C:\upxsharp-artifacts\fixture-inventory.json"
$env:UPXSHARP_REAL_FIXTURES_REPORT_DIR="C:\upxsharp-artifacts\reports"
.\tools\verify-external-artifacts.ps1
```

## Criterios Para Fechar Checklist

- `Copiar stubs pre-compilados do UPX original para Stubs/Resources/`: marcar quando `stubs.manifest.json` nao tiver nenhum item `fake`.
- `Testes com binarios reais de cada plataforma`: marcar somente quando o inventario cobrir todos os formatos exigidos, `UPXSHARP_REAL_FIXTURES_DIR` estiver populado e o gate passar.
- `Compatibilidade com arquivos comprimidos pelo UPX original (C++)`: marcar somente quando o inventario tiver `packedPath` para todos os formatos, `UPXSHARP_UPX_PACKED_FIXTURES_DIR` estiver populado com arquivos do UPX oficial e o gate passar.
