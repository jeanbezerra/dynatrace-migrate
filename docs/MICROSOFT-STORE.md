# Microsoft Store com MSIX

Este é um canal separado do MSI publicado no GitHub. A Microsoft Store
substitui qualquer assinatura do MSIX por um certificado Microsoft após a
certificação. Portanto, o PFX do workflow `release-msi.yml` não é necessário
para a submissão MSIX.

> A reassinatura vale apenas para MSIX/AppX distribuído pela Store. MSI, EXE e
> MSIX instalado por sideload continuam exigindo assinatura própria.

## Preparação única

1. Crie a conta no [Partner Center](https://partner.microsoft.com/dashboard)
   e reserve o produto como **MSIX/PWA**.
2. Em **Product management > Product identity**, copie exatamente `Name`,
   `Publisher` e `PublisherDisplayName`. Os valores diferenciam maiúsculas de
   minúsculas e não devem ser inventados no manifesto.
3. No Visual Studio, instale **Desenvolvimento para desktop com .NET** e o
   componente opcional **MSIX Packaging Tools**.
4. Adicione um **Windows Application Packaging Project**, referencie
   `A2D.AlertMigrator.Desktop` e defina a aplicação como ponto de entrada x64.
5. Use **Publish > Associate App with the Store** para aplicar a identidade
   reservada e gere os recursos visuais exigidos.

Para este WPF, mantenha o pacote como aplicação desktop de confiança total. O
manifesto deve apontar para `A2D.AlertMigrator.exe`, usar
`Windows.Desktop` e declarar somente a capacidade restrita `runFullTrust`.

## Gerar e validar

1. Abra **Publish > Create App Packages > Microsoft Store**.
2. Gere `Release | x64` como `.msixupload`, formato recomendado para o Partner
   Center. Use versão `MAJOR.MINOR.PATCH.0`; o quarto campo fica reservado para
   a Store.
3. Execute o Windows App Certification Kit no pacote.
4. Teste instalação, atualização, exportação do SQLite, logs, seletores de
   arquivos, proxy, TLS e conexões HTTPS em uma máquina limpa.

O pacote de teste local pode usar um certificado de desenvolvimento confiado na
máquina de teste. Ele não é o certificado entregue aos usuários da Store.

## Submeter

1. No Partner Center, crie uma submissão e envie o `.msixupload` em
   **Packages**.
2. Preencha preço e disponibilidade, propriedades, classificação etária,
   listagem, suporte e notas para certificação. Em política de privacidade,
   selecione **Fornecer o texto da política de privacidade** e cole o conteúdo
   de [`PRIVACY-POLICY.md`](PRIVACY-POLICY.md). Não é necessário publicar uma
   URL enquanto essa opção estiver disponível no Partner Center.
3. Selecione publicação imediata, agendada ou manual e envie para certificação.
4. Após a aprovação, confirme que o pacote publicado aparece assinado pela
   Microsoft e instale-o diretamente pela Store para o teste final.

Antes de enviar, confirme se o nome do publicador e o canal de suporte estão
corretos. A política foi escrita conforme o funcionamento atual do aplicativo e
deve ser revisada novamente se houver mudança na coleta, no armazenamento ou no
compartilhamento de dados.

Não publique o `.msixupload` como instalador no GitHub Releases. Ele é um
artefato de submissão; o pacote confiável para usuários é o disponibilizado pela
Store.

Referências: [publicar uma aplicação Windows](https://learn.microsoft.com/windows/apps/package-and-deploy/publish-first-app),
[requisitos do pacote](https://learn.microsoft.com/windows/apps/publish/publish-your-app/msix/app-package-requirements),
[empacotar WPF com MSIX](https://learn.microsoft.com/windows/apps/desktop/modernize/dotnet/package-app),
[enviar pacotes](https://learn.microsoft.com/windows/apps/publish/publish-your-app/msix/upload-app-packages)
e [processo de certificação](https://learn.microsoft.com/windows/apps/publish/publish-your-app/msix/app-certification-process),
[política de privacidade e suporte](https://learn.microsoft.com/windows/apps/publish/publish-your-app/msix/support-info)
e [Políticas da Microsoft Store](https://learn.microsoft.com/windows/apps/publish/store-policies).
