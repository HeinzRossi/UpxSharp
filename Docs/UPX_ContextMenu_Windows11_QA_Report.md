# QA — Integração com Menu de Contexto do Windows 11

Data: 15/08/2026

## Ambiente

- Windows: `10.0.26200.0`
- Menu moderno: Windows 11
- Escopo: somente arquivos `.exe`
- Ferramentas Windows SDK: `MakeAppx.exe` e `SignTool.exe` encontrados via Windows Kits
- Pacote sparse: `UpxSharp.Sparse.msix` gerado no output WPF
- Assinatura do pacote: MSIX dev assinado gerado
- Confiança do certificado: pendente para instalação AppX; `Get-AuthenticodeSignature` está válido no usuário atual, mas `Add-AppxPackage` exige a raiz em `LocalMachine\Root` para certificado autoassinado

## Checklist manual

| Item | Resultado | Observação |
| --- | --- | --- |
| Instalar certificado dev em LocalMachine\Root | Pendente | Importar `artifacts\windows-shell\UpxSharp.DevCertificate.cer` em PowerShell como Administrador |
| Instalar integração pelo WPF | Não testado | Após confiar o certificado, clicar no botão do app |
| Reiniciar Explorer após instalar | Não testado | Usar `tools\windows-shell\restart-explorer.ps1` ou opção `-RestartExplorer` no diagnóstico |
| `.exe` mostra UpxSharp no menu moderno | Não testado | Confirmar visualmente no Explorer |
| Flyout mostra Comprimir/Descomprimir/Testar | Não testado | Confirmar visualmente no Explorer |
| `.txt`, `.png`, `.dll` não mostram a opção | Não testado | Confirmar visualmente no Explorer |
| Fallback em “Mostrar mais opções” | Não testado | Confirmar visualmente no Explorer |
| Windows 10 | Não testável neste ambiente | Máquina atual é Windows 11 |
| ARM64 | Não testável neste ambiente | Máquina atual não validada como ARM64 |

## Roteiro de validação

1. Gerar pacote dev assinado:

   ```powershell
   tools\windows-shell\prepare-dev-signed-package.ps1 -SkipTrust
   ```

2. Importar manualmente `artifacts\windows-shell\UpxSharp.DevCertificate.cer` em **LocalMachine\Root / Autoridades de Certificação Raiz Confiáveis** usando PowerShell como Administrador:

   ```powershell
   certutil.exe -f -addstore Root C:\Projetos\UpxSharp\artifacts\windows-shell\UpxSharp.DevCertificate.cer
   ```
3. Abrir `src\UpxSharp.Wpf\bin\Debug\net10.0-windows\UpxSharp.Wpf.exe`.
4. Clicar em `Instalar integração`.
5. Reiniciar o Explorer.
6. Rodar:

   ```powershell
   tools\windows-shell\validate-modern-context-menu.ps1 -AppDirectory src\UpxSharp.Wpf\bin\Debug\net10.0-windows
   ```

7. No Explorer, clicar com o botão direito em:
   - um `.exe` válido;
   - um `.dll`;
   - um `.txt`;
   - um `.png`.
7. Registrar cada item como `Aprovado`, `Falhou` ou `Não testável`.

## Critério para marcar Fase 5

Marcar os itens da Fase 5 na documentação somente após a validação visual real confirmar o menu moderno/flyout/GetState, ou quando uma plataforma específica for explicitamente registrada como não testável.
