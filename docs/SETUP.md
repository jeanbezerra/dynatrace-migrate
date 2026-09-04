# Preparação da máquina

## Usuário do aplicativo

Baixe o MSI assinado no GitHub Release e execute-o. O pacote é autocontido,
instala em `Program Files` e solicita permissão de administrador. Não é
necessário instalar .NET nem SQLite.

> Não execute o banco diretamente no Google Drive. Use **Exportar projeto** para gerar o pacote compartilhável.

## Desenvolvedor

Instale:

1. [Git para Windows x64](https://git-scm.com/install/windows).
2. [.NET 10 SDK para Windows x64](https://dotnet.microsoft.com/download/dotnet/10.0) — selecione **SDK > Windows > x64**.
3. Opcional: [Visual Studio Community 2026](https://visualstudio.microsoft.com/downloads/) com a carga **Desenvolvimento para desktop com .NET**.

Para gerar MSIX para a Microsoft Store, adicione o componente opcional
**MSIX Packaging Tools** ao Visual Studio e siga
[`MICROSOFT-STORE.md`](MICROSOFT-STORE.md).

O SQLite é incorporado pelo pacote `Microsoft.Data.Sqlite`; não instale servidor
ou ferramenta separada.
O HTTP Client resiliente usa `Microsoft.Extensions.Http.Resilience`; ele também
é restaurado pelo NuGet e não exige instalador separado.

## Validar e executar testes

```powershell
git --version
dotnet --version
dotnet restore A2D.AlertMigrator.slnx
dotnet build A2D.AlertMigrator.slnx --no-restore
dotnet run --project tests/A2D.AlertMigrator.Infrastructure.SmokeTests -- $PWD.Path
```

Para abrir a tela:

```powershell
./scripts/run-desktop.ps1
```

Para gerar o executável autocontido:

```powershell
./scripts/publish-win-x64.ps1
```

O processo de release e os segredos exigidos estão em
[`RELEASES.md`](RELEASES.md).

Para distribuir em máquinas com App Control corporativo, publique com o
certificado Code Signing autorizado pela empresa. O procedimento está em
[`CODE-SIGNING.md`](CODE-SIGNING.md). O script pode usar `-RequireSignature`
para impedir que um pacote sem assinatura seja entregue por engano.

Em máquina restrita, baixe os instaladores e restaure os pacotes em uma máquina conectada antes de transferir o projeto.
