[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ArtifactPath,

    [Parameter(Mandatory)]
    [string]$CertificateThumbprint,

    [ValidateSet('CurrentUser', 'LocalMachine')]
    [string]$CertificateStore = 'CurrentUser',

    [string]$TimestampUrl
)

$ErrorActionPreference = 'Stop'
$codeSigningOid = '1.3.6.1.5.5.7.3.3'

function Find-SignTool {
    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $programFilesX86 = [Environment]::GetFolderPath('ProgramFilesX86')
    $kitsBin = Join-Path $programFilesX86 'Windows Kits/10/bin'

    if (Test-Path -LiteralPath $kitsBin) {
        $candidate = Get-ChildItem -Path (Join-Path $kitsBin '*/x64/signtool.exe') -File |
            Sort-Object FullName -Descending |
            Select-Object -First 1

        if ($null -ne $candidate) {
            return $candidate.FullName
        }
    }

    throw 'SignTool.exe nao foi encontrado. Instale o Windows SDK conforme docs/CODE-SIGNING.md.'
}

$ArtifactPath = [IO.Path]::GetFullPath($ArtifactPath)
if (-not (Test-Path -LiteralPath $ArtifactPath -PathType Leaf)) {
    throw "Artefato nao encontrado: $ArtifactPath"
}

$normalizedThumbprint = [Regex]::Replace($CertificateThumbprint, '[^0-9A-Fa-f]', '').ToUpperInvariant()
if ([string]::IsNullOrWhiteSpace($normalizedThumbprint)) {
    throw 'O thumbprint do certificado e invalido.'
}

$certificatePath = "Cert:\$CertificateStore\My\$normalizedThumbprint"
$certificate = Get-Item -LiteralPath $certificatePath -ErrorAction SilentlyContinue
if ($null -eq $certificate) {
    throw "Certificado nao encontrado em $CertificateStore\My: $normalizedThumbprint"
}

if (-not $certificate.HasPrivateKey) {
    throw 'O certificado selecionado nao possui uma chave privada acessivel.'
}

$supportsCodeSigning = $certificate.EnhancedKeyUsageList.ObjectId.Value -contains $codeSigningOid
if (-not $supportsCodeSigning) {
    throw 'O certificado selecionado nao possui a finalidade Code Signing.'
}

$now = Get-Date
if ($now -lt $certificate.NotBefore -or $now -gt $certificate.NotAfter) {
    throw 'O certificado selecionado esta fora do periodo de validade.'
}

if (-not [string]::IsNullOrWhiteSpace($TimestampUrl)) {
    $timestampUri = $null
    if (-not [Uri]::TryCreate($TimestampUrl, [UriKind]::Absolute, [ref]$timestampUri) -or
        $timestampUri.Scheme -notin @('http', 'https')) {
        throw 'A URL de carimbo de tempo deve ser HTTP ou HTTPS e absoluta.'
    }
}

$signTool = Find-SignTool
$signArguments = @(
    'sign'
    '/sha1', $normalizedThumbprint
    '/s', 'My'
    '/fd', 'SHA256'
    '/d', 'A2D Alert Migrator'
)

if ($CertificateStore -eq 'LocalMachine') {
    $signArguments += '/sm'
}

if (-not [string]::IsNullOrWhiteSpace($TimestampUrl)) {
    $signArguments += @('/tr', $TimestampUrl, '/td', 'SHA256')
}

$signArguments += $ArtifactPath

& $signTool @signArguments
if ($LASTEXITCODE -ne 0) {
    throw "SignTool falhou ao assinar o artefato. Codigo: $LASTEXITCODE"
}

& $signTool verify /pa /all $ArtifactPath
if ($LASTEXITCODE -ne 0) {
    throw "SignTool nao validou a assinatura do artefato. Codigo: $LASTEXITCODE"
}

$signature = Get-AuthenticodeSignature -LiteralPath $ArtifactPath
if ($signature.Status -ne [Management.Automation.SignatureStatus]::Valid) {
    throw "O Windows nao validou a assinatura Authenticode. Status: $($signature.Status)."
}

Write-Host "Artefato assinado por: $($certificate.Subject)" -ForegroundColor Green
