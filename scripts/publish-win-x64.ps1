[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$OutputPath,

    [string]$CertificateThumbprint,

    [ValidateSet('CurrentUser', 'LocalMachine')]
    [string]$CertificateStore = 'CurrentUser',

    [string]$TimestampUrl,

    [string]$Version,

    [switch]$RequireSignature
)

$ErrorActionPreference = 'Stop'

$workspaceRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$projectPath = Join-Path $workspaceRoot 'src/A2D.AlertMigrator.Desktop/A2D.AlertMigrator.Desktop.csproj'
$signingScript = Join-Path $PSScriptRoot 'sign-windows-artifact.ps1'
$dotnetCommand = Get-Command dotnet -ErrorAction Stop

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $workspaceRoot 'artifacts/A2D.AlertMigrator-win-x64'
}

$OutputPath = [IO.Path]::GetFullPath($OutputPath)
$executablePath = Join-Path $OutputPath 'A2D.AlertMigrator.exe'

if ($RequireSignature -and [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    throw 'Informe -CertificateThumbprint quando usar -RequireSignature.'
}

$publishArguments = @(
    'publish'
    $projectPath
    '--configuration', $Configuration
    '--runtime', 'win-x64'
    '--self-contained', 'true'
    '--nologo'
    '--tl:off'
    '-p:PublishSingleFile=true'
    '-p:IncludeNativeLibrariesForSelfExtract=true'
    '-p:ContinuousIntegrationBuild=true'
    '--output', $OutputPath
)

if (-not [string]::IsNullOrWhiteSpace($Version)) {
    if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
        throw 'A versão deve usar o formato numérico MAJOR.MINOR.PATCH.'
    }

    $publishArguments += @(
        "-p:Version=$Version"
        "-p:AssemblyVersion=$Version.0"
        "-p:FileVersion=$Version.0"
    )
}

& $dotnetCommand.Source @publishArguments
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "O executavel publicado nao foi encontrado: $executablePath"
}

if (-not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    $signingArguments = @{
        ArtifactPath = $executablePath
        CertificateThumbprint = $CertificateThumbprint
        CertificateStore = $CertificateStore
    }

    if (-not [string]::IsNullOrWhiteSpace($TimestampUrl)) {
        $signingArguments.TimestampUrl = $TimestampUrl
    }

    & $signingScript @signingArguments
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

$signature = Get-AuthenticodeSignature -LiteralPath $executablePath
$isSigned = $signature.Status -eq [Management.Automation.SignatureStatus]::Valid

if ($RequireSignature -and -not $isSigned) {
    throw "A assinatura do executavel nao foi validada. Status: $($signature.Status)."
}

Write-Host "Aplicativo publicado em: $executablePath" -ForegroundColor Green

if ($isSigned) {
    Write-Host "Assinatura valida: $($signature.SignerCertificate.Subject)" -ForegroundColor Green
}
else {
    Write-Warning 'O pacote esta sem uma assinatura Authenticode valida. Ele pode ser executado localmente, mas uma politica corporativa pode bloquea-lo. Consulte docs/CODE-SIGNING.md.'
}
