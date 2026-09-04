# Releases MSI

O workflow [`release-msi.yml`](../.github/workflows/release-msi.yml) compila,
executa os smoke tests, publica o aplicativo `win-x64` autocontido, assina o
executável e o MSI, gera SHA-256 e anexa os arquivos ao GitHub Release.

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

O empacotamento usa WiX 5.0.2 fixado no projeto. WiX 6 e 7 adotam a
[Open Source Maintenance Fee](https://docs.firegiant.com/wix/osmf/); uma futura
atualização deve passar por avaliação jurídica e de segurança.

Referências: [segredos no GitHub Actions](https://docs.github.com/actions/security-guides/using-secrets-in-github-actions),
[GitHub Releases](https://docs.github.com/repositories/releasing-projects-on-github/about-releases)
e [SignTool](https://learn.microsoft.com/windows/win32/seccrypto/signtool).
