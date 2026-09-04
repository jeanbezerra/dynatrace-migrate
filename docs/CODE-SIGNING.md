# Assinatura e liberação no Windows

## Downloads

Instale somente o que estiver ausente:

1. [.NET 10 SDK para Windows x64](https://dotnet.microsoft.com/download/dotnet/10.0)
2. [Windows SDK](https://developer.microsoft.com/windows/downloads/windows-sdk/), que inclui o SignTool
3. [Git para Windows](https://git-scm.com/install/windows)
4. Opcional: [GitHub CLI](https://cli.github.com/), para cadastrar secrets pelo terminal

O WiX é restaurado automaticamente pelo `dotnet`; não instale uma cópia global.

## Antes de criar a chave

Escolha o canal correto:

| Distribuição | O que fazer |
|---|---|
| Microsoft Store com MSIX | Não criar chave de produção. A Store reassina o pacote aprovado. |
| MSI/EXE corporativo | Solicitar certificado à PKI interna e autorizar o publicador no Windows App Control. |
| MSI/EXE público | Contratar autoridade certificadora ou serviço de assinatura com HSM. |
| MSIX por sideload | Assinar com certificado confiável nos computadores de destino. |

Este tutorial cria uma solicitação para a **PKI interna** e prepara um PFX para
o GitHub Actions. Certificados públicos atuais normalmente mantêm a chave em HSM
ou serviço remoto e não permitem exportação para PFX. Nesse caso, o workflow
deverá ser adaptado ao provedor; não tente contornar a proteção da chave.

Para Microsoft Store, siga [`MICROSOFT-STORE.md`](MICROSOFT-STORE.md).

## 1. Confirmar as ferramentas

Abra o PowerShell na raiz do repositório:

```powershell
git --version
dotnet --version

$kitsBin = Join-Path `
  ([Environment]::GetFolderPath('ProgramFilesX86')) `
  'Windows Kits\10\bin'

$signTool = Get-ChildItem `
  -Path (Join-Path $kitsBin '*\x64\signtool.exe') `
  -File |
  Sort-Object { [version]$_.Directory.Parent.Name } -Descending |
  Select-Object -First 1

if ($null -eq $signTool) {
  throw 'SignTool não encontrado. Repare a instalação do Windows SDK.'
}

$signTool.FullName
```

O último comando deve retornar um caminho terminado em `x64\signtool.exe`.

## 2. Solicitar os dados à equipe de PKI

Antes de executar o próximo passo, obtenha:

- nome legal que aparecerá como publicador;
- nome do servidor e da autoridade certificadora;
- nome interno do template de certificado Code Signing;
- confirmação de que a chave pode ser exportada para o GitHub Actions;
- URL RFC 3161 aprovada para timestamp;
- confirmação de que a raiz da PKI e o publicador estão autorizados nas máquinas.

O certificado deve ter a finalidade Code Signing, OID
`1.3.6.1.5.5.7.3.3`. Não use certificado autoassinado em produção.

## 3. Gerar a chave privada e o CSR

Crie a solicitação fora do repositório. Substitua o `Subject` pelo nome legal
fornecido pela PKI:

```powershell
$requestDirectory = 'C:\A2D-Signing'
New-Item -ItemType Directory -Path $requestDirectory -Force | Out-Null

$infPath = Join-Path $requestDirectory 'a2d-code-signing.inf'
$csrPath = Join-Path $requestDirectory 'a2d-code-signing.req'

$inf = @'
[Version]
Signature="$Windows NT$"

[NewRequest]
Subject = "CN=NOME LEGAL DA EMPRESA, O=NOME LEGAL DA EMPRESA, C=BR"
KeyAlgorithm = RSA
KeyLength = 3072
HashAlgorithm = sha256
KeyUsage = 0x80
ProviderName = "Microsoft Software Key Storage Provider"
MachineKeySet = FALSE
Exportable = TRUE
PrivateKeyArchive = FALSE
RequestType = PKCS10

[EnhancedKeyUsageExtension]
OID = 1.3.6.1.5.5.7.3.3
'@

[IO.File]::WriteAllText($infPath, $inf, [Text.Encoding]::ASCII)
certreq.exe -new $infPath $csrPath

if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $csrPath)) {
  throw 'Não foi possível gerar o CSR.'
}

certutil.exe -dump $csrPath
```

Esse processo cria a chave privada no repositório pessoal do Windows e grava
somente a solicitação pública em `.req`. Não envie a chave privada nem o PFX em
chamado ou e-mail.

`Exportable = TRUE` é necessário para o workflow PFX atual e deve ser autorizado
pela segurança. Se a política exigir HSM ou chave não exportável, pare aqui e
use o fluxo indicado pela PKI.

## 4. Enviar o CSR para emissão

Se a PKI receber solicitações por chamado, envie apenas
`C:\A2D-Signing\a2d-code-signing.req` e solicite o retorno em `.cer` ou `.p7b`.

Se a PKI permitir submissão direta, substitua os valores abaixo:

```powershell
$caConfiguration = 'SERVIDOR-CA\NOME-DA-CA'
$templateName = 'CodeSigning'
$cerPath = Join-Path $requestDirectory 'a2d-code-signing.cer'

certreq.exe -submit `
  -config $caConfiguration `
  -attrib "CertificateTemplate:$templateName" `
  $csrPath `
  $cerPath

if ($LASTEXITCODE -ne 0) {
  throw 'A autoridade certificadora não aprovou a solicitação.'
}
```

Uma solicitação pode ficar pendente para aprovação. Nesse caso, aguarde a PKI e
salve o certificado emitido como `a2d-code-signing.cer`.

## 5. Instalar o certificado emitido

O aceite associa o certificado à chave privada criada no passo 3:

```powershell
$cerPath = 'C:\A2D-Signing\a2d-code-signing.cer'
certreq.exe -accept $cerPath

if ($LASTEXITCODE -ne 0) {
  throw 'Não foi possível associar o certificado à chave privada.'
}
```

Localize e valide o certificado:

```powershell
$codeSigningOid = '1.3.6.1.5.5.7.3.3'

$certificate = Get-ChildItem 'Cert:\CurrentUser\My' |
  Where-Object {
    $_.HasPrivateKey -and
    $_.EnhancedKeyUsageList.ObjectId.Value -contains $codeSigningOid
  } |
  Sort-Object NotAfter -Descending |
  Select-Object -First 1

if ($null -eq $certificate) {
  throw 'Certificado Code Signing com chave privada não encontrado.'
}

$now = Get-Date
if ($now -lt $certificate.NotBefore -or $now -gt $certificate.NotAfter) {
  throw 'O certificado ainda não é válido ou está expirado.'
}

$thumbprint = $certificate.Thumbprint
$certificate | Format-List Subject, Issuer, Thumbprint, NotBefore, NotAfter, HasPrivateKey
```

Confirme visualmente o `Subject` e o `Issuer`; não escolha apenas pelo prazo de
validade se houver mais de um certificado Code Signing instalado.

## 6. Exportar o PFX para o GitHub Actions

Execute apenas se a exportação tiver sido autorizada:

```powershell
$pfxPath = 'C:\A2D-Signing\a2d-code-signing.pfx'
$pfxPassword = Read-Host 'Crie uma senha forte para o PFX' -AsSecureString

Export-PfxCertificate `
  -Cert $certificate `
  -FilePath $pfxPath `
  -Password $pfxPassword `
  -ChainOption BuildChain `
  -CryptoAlgorithmOption AES256_SHA256 `
  -ErrorAction Stop

if (-not (Test-Path -LiteralPath $pfxPath)) {
  throw 'Não foi possível exportar o PFX.'
}
```

Guarde o PFX em cofre aprovado. Não o coloque no repositório, Drive, OneDrive,
anexo, artefato da pipeline ou pasta compartilhada.

Se você já recebeu um PFX pronto, importe-o localmente assim:

```powershell
$pfxPath = 'C:\A2D-Signing\a2d-code-signing.pfx'
$pfxPassword = Read-Host 'Senha do PFX' -AsSecureString

Import-PfxCertificate `
  -FilePath $pfxPath `
  -CertStoreLocation 'Cert:\CurrentUser\My' `
  -Password $pfxPassword `
  -Exportable:$false
```

Depois repita a localização do `$certificate` e do `$thumbprint` do passo 5.

## 7. Testar a assinatura local

Use o timestamp aprovado pela PKI:

```powershell
$timestampUrl = 'http://timestamp.digicert.com'

./scripts/publish-win-x64.ps1 `
  -Version '1.0.0' `
  -CertificateThumbprint $thumbprint `
  -CertificateStore CurrentUser `
  -TimestampUrl $timestampUrl `
  -RequireSignature
```

Valide o executável:

```powershell
$exe = 'artifacts\A2D.AlertMigrator-win-x64\A2D.AlertMigrator.exe'

Get-AuthenticodeSignature -LiteralPath $exe |
  Format-List Status, StatusMessage, SignerCertificate, TimeStamperCertificate

& $signTool.FullName verify /pa /all /v $exe
```

O status deve ser `Valid` e o SignTool deve terminar com código zero.

## 8. Gerar o MSI assinado

```powershell
./scripts/build-release-msi.ps1 `
  -Version '1.0.0' `
  -CertificateThumbprint $thumbprint `
  -CertificateStore CurrentUser `
  -TimestampUrl $timestampUrl
```

Arquivos esperados:

```text
artifacts/releases/1.0.0/A2D.AlertMigrator-1.0.0-win-x64.msi
artifacts/releases/1.0.0/A2D.AlertMigrator-1.0.0-win-x64.msi.sha256
```

Valide a assinatura e o checksum:

```powershell
$msi = 'artifacts\releases\1.0.0\A2D.AlertMigrator-1.0.0-win-x64.msi'
$checksum = "$msi.sha256"

& $signTool.FullName verify /pa /all /v $msi

$expected = ((Get-Content -LiteralPath $checksum -Raw).Trim() -split '\s+')[0]
$actual = (Get-FileHash -LiteralPath $msi -Algorithm SHA256).Hash.ToLowerInvariant()

if ($actual -ne $expected) {
  throw 'O checksum do MSI não confere.'
}

"SHA-256 válido: $actual"
```

Se o WiX retornar `WIX1105` por política local, execute a geração pelo GitHub
Actions ou em máquina autorizada. Não desative a validação ICE no release.

## 9. Criar os secrets no GitHub

No repositório, abra **Settings > Environments**, crie `release` e configure
revisores obrigatórios. Cadastre:

| Tipo | Nome |
|---|---|
| Secret | `WINDOWS_SIGNING_CERTIFICATE_BASE64` |
| Secret | `WINDOWS_SIGNING_CERTIFICATE_PASSWORD` |
| Variable | `WINDOWS_TIMESTAMP_URL` |

Converta o PFX em Base64 sem criar outro arquivo:

```powershell
$pfxPath = 'C:\A2D-Signing\a2d-code-signing.pfx'
$pfxBase64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($pfxPath))
$pfxBase64 | Set-Clipboard
```

Cole o conteúdo no secret `WINDOWS_SIGNING_CERTIFICATE_BASE64`. Digite no outro
secret a mesma senha criada no passo 6. Base64 não é criptografia; a proteção é
fornecida pelo secret e pelas regras do environment `release`.

Se instalar o GitHub CLI, é possível cadastrar pelo terminal:

```powershell
gh auth login

$pfxBase64 | gh secret set `
  WINDOWS_SIGNING_CERTIFICATE_BASE64 `
  --env release

gh secret set WINDOWS_SIGNING_CERTIFICATE_PASSWORD --env release

gh variable set WINDOWS_TIMESTAMP_URL `
  --env release `
  --body $timestampUrl
```

O comando da senha solicitará o valor interativamente. Limpe as variáveis da
sessão ao terminar:

```powershell
$pfxPassword = $null
$pfxBase64 = $null
```

## 10. Publicar o release

Após integrar o commit na branch padrão:

```powershell
git tag -a v1.0.0 -m 'Release 1.0.0'
git push origin v1.0.0
```

O workflow **Publicar MSI** rejeita tag inválida, teste com falha, segredo
ausente, certificado inadequado, assinatura inválida ou erro no timestamp.

## 11. Diagnosticar bloqueio no Windows

Confirme primeiro a assinatura do arquivo baixado. Depois consulte o App Control:

```powershell
Get-WinEvent `
  -LogName 'Microsoft-Windows-CodeIntegrity/Operational' `
  -MaxEvents 100 |
  Where-Object Id -In 3033, 3077 |
  Select-Object TimeCreated, Id, LevelDisplayName, Message
```

Uma assinatura válida não substitui a política corporativa. Se houver bloqueio,
envie à segurança o evento, o SHA-256 e o certificado publicador para inclusão
na política Windows App Control.

## Referências

- [Certreq e formato do arquivo INF](https://learn.microsoft.com/windows-server/administration/windows-commands/certreq_1)
- [Exportar certificado e chave privada em PFX](https://learn.microsoft.com/windows-server/identity/ad-cs/export-certificate-private-key)
- [Requisitos para proteção de chaves Code Signing](https://cabforum.org/working-groups/code-signing/requirements/)
- [SignTool](https://learn.microsoft.com/windows/win32/seccrypto/signtool)
- [Timestamp de assinaturas Authenticode](https://learn.microsoft.com/windows/win32/seccrypto/time-stamping-authenticode-signatures)
- [Eventos do Windows App Control](https://learn.microsoft.com/windows/security/application-security/application-control/windows-defender-application-control/operations/event-id-explanations)
- [Secrets no GitHub Actions](https://docs.github.com/actions/security-for-github-actions/security-guides/using-secrets-in-github-actions)
- [Ambientes de implantação no GitHub](https://docs.github.com/actions/deployment/targeting-different-environments/managing-environments-for-deployment)
- [GitHub Releases](https://docs.github.com/repositories/releasing-projects-on-github/about-releases)
- [Assinatura pela Microsoft Store](https://learn.microsoft.com/windows/apps/publish/publish-your-app/msix/app-certification-process)
