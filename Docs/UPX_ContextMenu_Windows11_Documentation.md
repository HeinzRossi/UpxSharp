# UpxSharp — Integração com o Menu de Contexto Moderno do Windows 11

> **Projeto:** UpxSharp — Shell Extension
> **Alvo:** Windows 11 (menu de contexto moderno) + fallback Windows 10/7
> **Mecanismo:** `IExplorerCommand` + Identidade de Pacote (Sparse Package)
> **Finalidade:** Documento de referência para adicionar "Comprimir/Descomprimir com UpxSharp" ao clique direito no Explorer

---

## Sumário

1. [Por que o `IContextMenu` clássico não aparece mais](#1-por-que-o-icontextmenu-clássico-não-aparece-mais)
2. [Arquitetura Escolhida](#2-arquitetura-escolhida)
3. [Estrutura de Projeto](#3-estrutura-de-projeto)
4. [Implementação COM: IExplorerCommand](#4-implementação-com-iexplorercommand)
5. [Provider com múltiplos verbos](#5-provider-com-múltiplos-verbos)
6. [Registro COM (Surrogate Server, sem admin)](#6-registro-com-surrogate-server-sem-admin)
7. [Sparse Package Manifest](#7-sparse-package-manifest)
8. [Registro do Sparse Package em Runtime](#8-registro-do-sparse-package-em-runtime)
9. [Empacotamento, Certificado e Instalador](#9-empacotamento-certificado-e-instalador)
10. [Integração com o Core (PackMaster)](#10-integração-com-o-core-packmaster)
11. [Ícones no Menu Moderno](#11-ícones-no-menu-moderno)
12. [Depuração e Diagnóstico](#12-depuração-e-diagnóstico)
13. [Fallback: Windows 10 / "Mostrar mais opções"](#13-fallback-windows-10--mostrar-mais-opções)
14. [Checklist de Implementação](#14-checklist-de-implementação)

---

## 1. Por que o `IContextMenu` clássico não aparece mais

No Windows 11, itens registrados pelo modelo antigo (`HKCR\*\shellex\ContextMenuHandlers`, interface `IContextMenu`) são automaticamente empurrados para **"Mostrar mais opções"** (o menu legado, acessível também via `Shift+F10`). Isso vale mesmo para instaladores que fazem registro clássico corretamente — não é um bug a corrigir, é uma mudança deliberada de plataforma.

Para aparecer no **nível superior** do menu novo, a Microsoft exige duas coisas simultâneas:

1. A extensão deve implementar a interface **`IExplorerCommand`** (não `IContextMenu`).
2. O aplicativo precisa de **identidade de pacote** (*package identity*) — ou seja, o Explorer só carrega comandos de nível superior de apps que tenham um `AppxManifest` associado, mesmo que o executável em si continue sendo um Win32 tradicional instalado fora do `WindowsApps`.

Para um app desktop não empacotado (como o `UpxSharp.Wpf`, distribuído como `.exe`/MSI tradicional), a forma de obter identidade sem migrar para MSIX completo é o **Sparse Package** (disponível desde Windows 10 2004): um pacote "vazio", só com manifesto, que aponta para uma *external location* — a pasta onde o UpxSharp já está instalado.

---

## 2. Arquitetura Escolhida

```
┌─────────────────────────────┐        ┌──────────────────────────────┐
│  UpxSharp.Wpf (app principal)│        │  UpxSharp.Shell (novo projeto)│
│  já instalado em              │        │  DLL COM Surrogate             │
│  C:\Program Files\UpxSharp\   │        │  implementa IExplorerCommand   │
└─────────────────────────────┘        └──────────────────────────────┘
              │                                        │
              │ chama via named pipe / linha de comando│
              └──────────────► PackMaster (UpxSharp core) ◄────────┘

Sparse Package (identidade)
  └─ AppxManifest.xml aponta para a pasta de instalação (ExternalLocation)
  └─ Declara com:Extension windows.comServer  → registra a DLL como COM Surrogate
  └─ Declara desktop4:Extension windows.fileExplorerContextMenus → registra os verbos
```

Pontos de design:

- **Novo projeto** `UpxSharp.Shell` (Class Library, `net10.0-windows`, `<EnableComHosting>` ou COM surrogate via `dllhost.exe`) — mantém a extensão de shell isolada do app principal, evitando carregar WPF/CommunityToolkit dentro do processo do Explorer.
- A extensão **não referencia o core de compressão diretamente**; ela invoca o `UpxSharp.Cli` (ou o `UpxSharp.Wpf` já em execução, via named pipe) para fazer o trabalho pesado fora do processo do `explorer.exe`. Isso evita travar o Explorer e problemas de versão de runtime .NET dentro do host COM.
- **Sparse Package** em vez de MSIX completo — o UpxSharp continua sendo instalado do jeito atual (MSI/instalador tradicional); o sparse package só existe para dar identidade.

---

## 3. Estrutura de Projeto

```
UpxSharp.sln
├── src/
│   ├── UpxSharp/                       ← core (já existe)
│   ├── UpxSharp.Cli/                   ← CLI (já existe)
│   ├── UpxSharp.Wpf/                   ← UI (já existe)
│   │
│   └── UpxSharp.Shell/                 ← NOVO: extensão de shell
│       UpxSharp.Shell.csproj
│       │
│       ├── ExplorerCommand/
│       │   ├── CompressCommand.cs        ← IExplorerCommand: "Comprimir com UpxSharp"
│       │   ├── DecompressCommand.cs      ← IExplorerCommand: "Descomprimir com UpxSharp"
│       │   ├── TestCommand.cs            ← IExplorerCommand: "Testar com UpxSharp"
│       │   └── UpxCommandProvider.cs     ← IExplorerCommandProvider (agrupa os 3 num flyout)
│       │
│       ├── Interop/
│       │   ├── IExplorerCommand.cs       ← definição da interface COM (via ComImport)
│       │   ├── IExplorerCommandProvider.cs
│       │   ├── IObjectWithSelection.cs
│       │   ├── IShellItemArray.cs
│       │   └── ExplorerCommandFlags.cs
│       │
│       ├── ClassFactory.cs               ← IClassFactory manual (para registro COM sem regasm)
│       ├── ComRegistration.cs            ← GUIDs, ProgIds
│       └── DllMain.cs                    ← DllGetClassObject / DllCanUnloadNow (nativo AOT ou COM host)
│
└── packaging/
    └── UpxSharp.Sparse/                 ← NOVO: projeto de empacotamento do sparse package
        AppxManifest.xml
        Assets/                          ← ícones exigidos pelo manifesto (44x44, 150x150 etc.)
        UpxSharp.Sparse.wapproj          ← ou script MakeAppx manual
```

### `UpxSharp.Shell.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <!-- COM exige um host nativo: publicar como Native AOT com exports COM,
         ou hospedar via .NET COM Hosting (ComHost) -->
    <EnableComHosting>true</EnableComHosting>
    <PublishAot>true</PublishAot>
    <SelfContained>true</SelfContained>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <Platforms>x64;arm64</Platforms>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\UpxSharp\UpxSharp.csproj" />
  </ItemGroup>
</Project>
```

> **Nota de arquitetura:** o Explorer é um processo de 64 bits (ou ARM64 nativo em máquinas ARM). A DLL COM precisa existir na arquitetura correspondente — publique `win-x64` e `win-arm64` e registre o `Class` certo por plataforma no manifesto, ou use um único binário `AnyCPU`/AOT por RID com paths distintos.

---

## 4. Implementação COM: `IExplorerCommand`

```csharp
// Interop/IExplorerCommand.cs
using System.Runtime.InteropServices;

namespace UpxSharp.Shell.Interop;

[ComImport]
[Guid("A08CE4D0-FA25-44AB-B57C-C7B1C323E0B9")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IExplorerCommand
{
    void GetTitle(IShellItemArray? items, out string name);
    void GetIcon(IShellItemArray? items, out string icon); // "modulo,-recurso"
    void GetToolTip(IShellItemArray? items, out string tooltip);
    void GetCanonicalName(out Guid guid);
    void GetState(IShellItemArray? items, bool okToBeSlow, out ExplorerCommandState state);
    void Invoke(IShellItemArray? items, object? bindCtx);
    void GetFlags(out ExplorerCommandFlags flags);
    void EnumSubCommands(out object? enumCommands); // IEnumExplorerCommand
}
```

```csharp
// ExplorerCommand/CompressCommand.cs
using UpxSharp.Shell.Interop;

namespace UpxSharp.Shell.ExplorerCommand;

[ComVisible(true)]
[Guid("D3F1E5C2-1B2E-4E9E-9A3E-8F6F0E1B2C3D")] // ClsId único — gerar novo
public sealed class CompressCommand : IExplorerCommand, IObjectWithSelection
{
    private IShellItemArray? _selection;

    public void SetSelection(IShellItemArray? psia) => _selection = psia;
    public void GetSelection(in Guid riid, out object? ppv) => ppv = _selection;

    public void GetTitle(IShellItemArray? items, out string name)
        => name = "Comprimir com UpxSharp";

    public void GetIcon(IShellItemArray? items, out string icon)
        => icon = @"C:\Program Files\UpxSharp\UpxSharp.Wpf.exe,-101";

    public void GetToolTip(IShellItemArray? items, out string tooltip)
        => tooltip = "Comprimir executável(is) selecionado(s) com UPX";

    public void GetCanonicalName(out Guid guid) => guid = GetType().GUID;

    public void GetState(IShellItemArray? items, bool okToBeSlow, out ExplorerCommandState state)
    {
        // Habilita só quando a seleção tem extensão compatível (.exe, .dll dinâmicas etc.)
        state = ShellSelectionHelper.HasSupportedExtension(items ?? _selection)
            ? ExplorerCommandState.ECS_ENABLED
            : ExplorerCommandState.ECS_HIDDEN;
    }

    public void Invoke(IShellItemArray? items, object? bindCtx)
    {
        var paths = ShellSelectionHelper.GetPaths(items ?? _selection);
        UpxCliBridge.RunAsync("compress", paths).GetAwaiter().GetResult();
    }

    public void GetFlags(out ExplorerCommandFlags flags) => flags = ExplorerCommandFlags.ECF_DEFAULT;

    public void EnumSubCommands(out object? enumCommands) => enumCommands = null;
}
```

`GetState` é o ponto crítico: o Explorer chama com `okToBeSlow = false` primeiro; se a extensão precisar inspecionar o conteúdo do arquivo (não apenas a extensão) para decidir se mostra o item, deve devolver `ECS_ENABLED` de forma otimista ou pedir nova chamada com `okToBeSlow = true` — evite I/O pesado aqui, ou o menu trava ao abrir.

---

## 5. Provider com múltiplos verbos

Como o UpxSharp expõe três ações (Comprimir, Descomprimir, Testar), o padrão recomendado é agrupá-las num único CLSID de nível superior que implementa `IExplorerCommandProvider`, para o Windows 11 renderizar como um **flyout único com atribuição do app** (em vez de três itens soltos):

```csharp
// ExplorerCommand/UpxCommandProvider.cs
[ComVisible(true)]
[Guid("7B2E4A10-9C3D-4F5A-8E1B-2D3C4E5F6A7B")]
public sealed class UpxCommandProvider : IExplorerCommandProvider
{
    public void GetCommands(object? site, in Guid riid, out object? ppv)
        => ppv = new IExplorerCommand[]
        {
            new CompressCommand(),
            new DecompressCommand(),
            new TestCommand(),
        };

    public void GetCommand(in Guid rguidCommandId, in Guid riid, out object? ppv)
        => ppv = rguidCommandId switch
        {
            var g when g == typeof(CompressCommand).GUID   => new CompressCommand(),
            var g when g == typeof(DecompressCommand).GUID => new DecompressCommand(),
            var g when g == typeof(TestCommand).GUID       => new TestCommand(),
            _ => null,
        };
}
```

O CLSID registrado no manifesto (seção 7) é o do **provider**, não o dos comandos individuais — o Explorer descobre os subcomandos chamando `GetCommands`.

---

## 6. Registro COM (Surrogate Server, sem admin)

Diferente do registro clássico em `HKCR` (que exige admin e "suja" o registro global), o modelo moderno registra a DLL como **COM Surrogate** dentro do próprio manifesto do pacote — o Windows carrega a DLL via `dllhost.exe` isolado, sem qualquer chave de registro tradicional. Isso é feito inteiramente pela seção `com:Extension` do `AppxManifest.xml` (próxima seção); não há `regsvr32` nem `RegistrationServices` a rodar manualmente.

Requisitos do lado do código:

- A DLL deve exportar `DllGetClassObject` e `DllCanUnloadNow` (padrão COM nativo) — com .NET, isso é feito publicando com **Native AOT** e `[UnmanagedCallersOnly]`, ou usando o **.NET COM Hosting** (`ComHost.dll` gerado automaticamente por `EnableComHosting`).
- Cada classe COM precisa de um `Guid` estável e não pode mudar entre versões (o manifesto referencia o CLSID fixo).

---

## 7. Sparse Package Manifest

Novo projeto `packaging/UpxSharp.Sparse/AppxManifest.xml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Package
  xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
  xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
  xmlns:com="http://schemas.microsoft.com/appx/manifest/com/windows10"
  xmlns:desktop4="http://schemas.microsoft.com/appx/manifest/desktop/windows10/4"
  xmlns:desktop5="http://schemas.microsoft.com/appx/manifest/desktop/windows10/5"
  xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
  IgnorableNamespaces="uap com desktop4 desktop5 rescap">

  <Identity
    Name="UpxSharp.Team.UpxSharp"
    Publisher="CN=UpxSharp"
    Version="1.0.0.0"
    ProcessorArchitecture="x64" />

  <Properties>
    <DisplayName>UpxSharp</DisplayName>
    <PublisherDisplayName>UpxSharp</PublisherDisplayName>
    <Logo>Assets\StoreLogo.png</Logo>
  </Properties>

  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.19041.0" MaxVersionTested="10.0.26100.0" />
  </Dependencies>

  <Resources>
    <Resource Language="pt-BR" />
  </Resources>

  <!-- Sparse package: aponta para a instalação Win32 existente -->
  <Applications>
    <Application Id="UpxSharpShell" Executable="UpxSharp.Wpf.exe" EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements
        DisplayName="UpxSharp"
        Description="Compressor de executáveis UPX"
        BackgroundColor="transparent"
        Square150x150Logo="Assets\Square150x150Logo.png"
        Square44x44Logo="Assets\Square44x44Logo.png" />

      <Extensions>
        <!-- Registra a DLL como COM Surrogate -->
        <com:Extension Category="windows.comServer">
          <com:ComServer>
            <com:SurrogateServer DisplayName="UpxSharp Shell Extension">
              <com:Class Id="7F5D0D52-7266-4B01-96D0-744153A0BB02"
                         Path="UpxSharp.Shell.comhost.dll"
                         ThreadingModel="STA" />
            </com:SurrogateServer>
          </com:ComServer>
        </com:Extension>

        <!-- Registra o menu de contexto -->
        <desktop4:Extension Category="windows.fileExplorerContextMenus">
          <desktop4:FileExplorerContextMenus>
            <desktop5:ItemType Type="*">
              <desktop5:Verb Id="UpxSharpMenu" Clsid="7F5D0D52-7266-4B01-96D0-744153A0BB02" />
            </desktop5:ItemType>
          </desktop4:FileExplorerContextMenus>
        </desktop4:Extension>
      </Extensions>
    </Application>
  </Applications>
</Package>
```

Pontos importantes:

- `Path="UpxSharp.Shell.comhost.dll"` é **relativo à pasta apontada pelo Package Externa Location** (ver seção 8) — normalmente a mesma pasta de instalação do `UpxSharp.Wpf.exe`.
- `ItemType Type="*"` segue o modelo funcional observado no WinRAR para o menu moderno; o UpxSharp restringe a exibição para `.exe` dentro do `GetState`, incluindo validação de conteúdo quando `okToBeSlow=true`.
- O `Guid` do `com:Class` deve ser **o mesmo** usado no atributo `[Guid(...)]` do `UpxCommandProvider`.

---

## 8. Registro do Sparse Package em Runtime

Como o app não é instalado como MSIX completo, o instalador ou o botão **Instalar integração** registra um **sparse package MSIX assinado** apontando para a pasta de instalação:

```csharp
public static class SparsePackageRegistration
{
    private const string PackageFileName = "UpxSharp.Sparse.msix";

    public static async Task EnsureRegisteredAsync()
    {
        var installDir = AppContext.BaseDirectory;
        var packagePath = Path.Combine(installDir, PackageFileName);
        await RunPowerShellAsync(
            $"Add-AppxPackage -Path '{packagePath}' -ExternalLocation '{installDir}'");
    }
}
```

- O caminho principal usa `UpxSharp.Sparse.msix` assinado; `Add-AppxPackage -Register AppxManifest.xml` fica apenas como modo dev explícito.
- O registro é **por usuário**, não exige elevação (diferente do registro clássico em `HKLM`).
- O certificado do MSIX precisa estar confiável para o usuário atual; caso contrário o Windows bloqueia o menu moderno.
- Deve haver um `RemovePackageAsync` correspondente no desinstalador, ou o menu continuará aparecendo com o executável removido.

---

## 9. Empacotamento, Certificado e Instalador

1. O `AppxManifest.xml` do sparse package precisa ser **assinado** com um certificado cuja identidade (`Publisher`) bate exatamente com o `Identity/Publisher` do manifesto.
   - Desenvolvimento: certificado autoassinado (`New-SelfSignedCertificate` + instalar em `LocalMachine\Root` / "Autoridades de Certificação Raiz Confiáveis" usando PowerShell como Administrador, pois o AppX Deployment Service valida a cadeia fora do escopo do usuário comum).
   - Distribuição: certificado de assinatura de código válido (o mesmo já deve ser usado para assinar `UpxSharp.Wpf.exe`, para consistência de SmartScreen).
2. Gerar o pacote sparse com `MakeAppx.exe pack /d packaging\UpxSharp.Sparse /p UpxSharp.Sparse.msix`, depois assinar com `SignTool.exe sign /fd SHA256 /a /f cert.pfx UpxSharp.Sparse.msix`.
   - Fluxo dev atual: `tools\windows-shell\prepare-dev-signed-package.ps1` gera/confia certificado no usuário atual, empacota, assina e copia o `.msix` para o output WPF.
   - Se `-Password` for informado, o script também exporta um PFX dev; sem senha, ele assina pelo certificado no repositório `CurrentUser\My`.
3. O instalador tradicional (MSI/Inno Setup/WiX) do UpxSharp passa a:
   - Copiar `UpxSharp.Shell.comhost.dll` e o `UpxSharp.Sparse.msix` junto dos demais binários.
   - Chamar `SparsePackageRegistration.EnsureRegisteredAsync()` (ou um passo de instalação equivalente em PowerShell) ao final da instalação.
   - Remover o pacote no desinstalador (`Remove-AppxPackage` ou `PackageManager.RemovePackageAsync`).

---

## 10. Integração com o Core (`PackMaster`)

Para não carregar o runtime .NET completo nem o `UpxSharp` core dentro do processo `dllhost.exe`/`explorer.exe`, o `Invoke` do comando delega para um processo separado:

```csharp
// ExplorerCommand/UpxCliBridge.cs
internal static class UpxCliBridge
{
    public static async Task RunAsync(string command, IReadOnlyList<string> paths)
    {
        var cliPath = Path.Combine(AppContext.BaseDirectory, "UpxSharp.Cli.exe");
        var args = string.Join(' ', new[] { command }.Concat(paths.Select(p => $"\"{p}\"")));

        var psi = new ProcessStartInfo(cliPath, args)
        {
            UseShellExecute = false,
            CreateNoWindow = false, // false para mostrar progresso ao usuário
        };

        using var proc = Process.Start(psi)!;
        await proc.WaitForExitAsync();

        if (proc.ExitCode != 0)
            ShellNotification.ShowBalloon("UpxSharp", $"Falha ao processar {paths.Count} arquivo(s).");
    }
}
```

Alternativa mais rica: se o `UpxSharp.Wpf` já estiver em execução, enviar via named pipe / `AppInstance` redirection para reaproveitar a janela aberta (arquivos entram na fila da `FileListView` existente), em vez de abrir um processo CLI headless. Recomenda-se implementar o CLI primeiro (mais simples) e evoluir para essa integração depois.

---

## 11. Ícones no Menu Moderno

O menu moderno **não aceita mais `.ico` arbitrário via string clássica** (`"caminho,índice"`) da mesma forma flexível do menu antigo — o ícone exibido no item de nível superior é resolvido a partir do `Square44x44Logo` do `VisualElements` do pacote quando o item representa o app; para os subverbos individuais dentro do flyout, `GetIcon` (retornando `"módulo,-recurso"`) ainda é respeitado pela maioria dos hosts, mas deve apontar para um recurso de ícone embutido no próprio `UpxSharp.Wpf.exe` ou numa DLL de recursos ao lado do COM host — **verificar visualmente após empacotar**, pois esse é um dos pontos mais frágeis de compatibilidade entre builds do Windows 11.

---

## 12. Depuração e Diagnóstico

- **Reiniciar o Explorer** após qualquer mudança no manifesto ou na DLL: `Stop-Process -Name explorer -Force` (ele reinicia sozinho) — o Explorer cacheia extensões carregadas.
- **DebugView (Sysinternals)** para ver `OutputDebugString` da DLL COM, já que anexar um debugger ao `dllhost.exe` do Explorer é mais custoso.
- Validar o registro do sparse package com:
  ```powershell
  Get-AppxPackage -Name "UpxSharp.Team.UpxSharp"
  ```
- Se o item não aparecer nem em "Mostrar mais opções": provavelmente falha de registro do pacote (verificar `AddPackageByUriAsync`, certificado, `Publisher` inconsistente).
- Se aparecer só em "Mostrar mais opções": geralmente é `IExplorerCommand` não implementado corretamente, `GetState` retornando `ECS_HIDDEN`, ou o CLSID no manifesto não bate com o `Guid` da classe.
- Ferramentas de terceiros úteis para inspecionar extensões já registradas: `ShellExView` (NirSoft) e o próprio editor de menu de contexto do Windows 11 (`ContextMenuManager`, terceiros) para ver o que o sistema enxerga.

---

## 13. Fallback: Windows 10 / "Mostrar mais opções"

`IExplorerCommand` funciona desde o Windows 7 — em versões sem o menu moderno (Windows 10 e anteriores), o mesmo comando aparece diretamente no menu de contexto padrão, sem necessidade de código adicional. Não é necessário manter uma implementação paralela em `IContextMenu` clássico só para compatibilidade retroativa.

---

## 14. Checklist de Implementação

### Fase 1 — Projeto e Interop
- [x] Criar projeto `UpxSharp.Shell` (`net10.0-windows`, Native AOT ou `EnableComHosting`)
- [x] Definir interfaces COM via `ComImport` (`IExplorerCommand`, `IExplorerCommandProvider`, `IObjectWithSelection`, `IShellItemArray`)
- [x] Gerar GUIDs estáveis para o provider e para cada comando

### Fase 2 — Comandos
- [x] `CompressCommand`, `DecompressCommand`, `TestCommand` implementando `IExplorerCommand`
- [x] `UpxCommandProvider` implementando `IExplorerCommandProvider`
- [x] `GetState` validando extensão/magic bytes sem I/O bloqueante
- [x] `UpxCliBridge` invocando `UpxSharp.Cli.exe` fora do processo do Explorer

### Fase 3 — Empacotamento
- [x] Projeto `packaging/UpxSharp.Sparse` com `AppxManifest.xml`
- [x] Assets de ícone (`Square44x44Logo`, `Square150x150Logo`, `StoreLogo`)
- [x] Certificado de assinatura (dev: autoassinado; prod: certificado real)
- [x] Script `MakeAppx` + `SignTool` no pipeline de build

### Fase 4 — Instalação
- [x] `SparsePackageRegistration.EnsureRegisteredAsync()` chamado na instalação/primeira execução
- [x] Remoção do pacote no desinstalador
- [x] Teste de reinstalação/atualização (versão do `Identity` incrementada)

### Fase 5 — Validação
- [ ] Item aparece no nível superior do menu do Windows 11 para `.exe`
- [ ] Flyout agrupa Comprimir/Descomprimir/Testar com ícone do app
- [ ] `GetState` oculta o item para arquivos incompatíveis
- [ ] Fallback funcional em Windows 10 (mesmo binário, sem sparse package moderno)
- [ ] Teste em `win-x64` e `win-arm64`

---

*Documento gerado em 15/08/2026 — Windows App SDK / Sparse Packages / IExplorerCommand*
