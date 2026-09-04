# Releases

O workflow [`release-msi.yml`](../.github/workflows/release-msi.yml) compila,
executa os smoke tests, publica o aplicativo `win-x64` autocontido, assina o
executável e o MSI, gera SHA-256 e anexa os arquivos ao GitHub Release.

O workflow [`release-exe.yml`](../.github/workflows/release-exe.yml) executa a
cada push na `master` ou manualmente. Ele publica somente o executável
autocontido em dois canais:

- `exe-v1.0.N`, release histórica cuja versão usa o número da execução;
- `exe-latest`, release móvel que sempre aponta para o último executável.

Cada release desse workflow contém um único arquivo `.exe`. O canal incremental
preserva o nome versionado, enquanto o canal móvel usa
`A2D.AlertMigrator-latest-win-x64.exe`.

| Canal | Pacote | Assinatura final |
|---|---|---|
| GitHub Release ou distribuição corporativa | MSI | Certificado Code Signing do projeto |
| GitHub Release incremental ou `exe-latest` | EXE autocontido | Opcional; depende dos secrets do projeto |
| Microsoft Store | MSIX | Microsoft Store após a certificação |

O procedimento da Store está em
[`MICROSOFT-STORE.md`](MICROSOFT-STORE.md). A reassinatura da Store não se
aplica ao MSI deste workflow nem a MSIX distribuído por sideload.

## Configuração única no GitHub

Em **Settings > Environments**, crie o ambiente `release` e, de preferência,
adicione revisores obrigatórios. Cadastre nele:

| Tipo | Nome | Valor |
|---|---|---|
| Secret | `WINDOWS_SIGNING_CERTIFICATE_BASE64` | PFX Code Signing em Base64 |
| Secret | `WINDOWS_SIGNING_CERTIFICATE_PASSWORD` | Senha do PFX |
| Variable | `WINDOWS_TIMESTAMP_URL` | Endpoint RFC 3161; se omitido, usa DigiCert |

Para copiar o PFX em Base64 sem criar outro arquivo:

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes('C:\certificados\codesign.pfx')) | Set-Clipboard
```

O certificado precisa conter chave privada, finalidade **Code Signing** e uma
cadeia confiável nas máquinas de destino. O PFX é apagado logo após a importação
e o certificado é removido do runner ao final.

## Publicar

Depois de integrar o commit à branch padrão:

```powershell
git tag -a v1.0.0 -m 'Release 1.0.0'
git push origin v1.0.0
```

Também é possível executar **Publicar MSI** manualmente e informar uma tag já
existente. Tags fora do formato `vMAJOR.MINOR.PATCH`, testes com falha ou
assinaturas inválidas impedem a publicação.

Para o executável, integre o commit na `master` ou execute **Publicar EXE** em
**Actions > Run workflow**. Não é necessário criar uma tag: o próprio workflow
usa `github.run_number` para gerar a próxima versão. Uma nova execução normal
gera uma nova versão; repetir a mesma execução atualiza a release correspondente.

Sem os dois secrets de assinatura, o workflow entra automaticamente no modo
gratuito e publica o EXE sem Authenticode. A release e o resumo da execução
mostram esse estado. Esse modo não evita alertas do SmartScreen nem bloqueios de
políticas corporativas. Para distribuição gratuita sem esses avisos, prefira o
MSIX reassinado pela Microsoft Store.

O empacotamento usa WiX 5.0.2 fixado no projeto. WiX 6 e 7 adotam a
[Open Source Maintenance Fee](https://docs.firegiant.com/wix/osmf/); uma futura
atualização deve passar por avaliação jurídica e de segurança.

Referências: [segredos no GitHub Actions](https://docs.github.com/actions/security-guides/using-secrets-in-github-actions),
[GitHub Releases](https://docs.github.com/repositories/releasing-projects-on-github/about-releases)
e [SignTool](https://learn.microsoft.com/windows/win32/seccrypto/signtool),
[conta gratuita da Microsoft Store](https://learn.microsoft.com/windows/apps/publish/faq/open-developer-account)
e [reputação do SmartScreen](https://learn.microsoft.com/windows/apps/package-and-deploy/smartscreen-reputation).
