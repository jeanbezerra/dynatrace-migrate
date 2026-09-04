[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Version,

    [Parameter(Mandatory)]
    [string]$CertificateThumbprint,

    [ValidateSet('CurrentUser', 'LocalMachine')]
    [string]$CertificateStore = 'CurrentUser',

    [Parameter(Mandatory)]
    [string]$TimestampUrl,

    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

$workspaceRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$solutionPath = Join-Path $workspaceRoot 'A2D.AlertMigrator.slnx'
$infrastructureTests = Join-Path $workspaceRoot 'tests/A2D.AlertMigrator.Infrastructure.SmokeTests/A2D.AlertMigrator.Infrastructure.SmokeTests.csproj'
$desktopTests = Join-Path $workspaceRoot 'tests/A2D.AlertMigrator.Desktop.SmokeTests/A2D.AlertMigrator.Desktop.SmokeTests.csproj'
$installerProject = Join-Path $workspaceRoot 'installer/A2D.AlertMigrator.Installer.wixproj'
$publishScript = Join-Path $PSScriptRoot 'publish-win-x64.ps1'
$signingScript = Join-Path $PSScriptRoot 'sign-windows-artifact.ps1'
$dotnetCommand = Get-Command dotnet -ErrorAction Stop

if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
    throw 'A versão deve usar o formato numérico MAJOR.MINOR.PATCH, por exemplo 1.4.0.'
}

$versionParts = @($Version.Split('.') | ForEach-Object { [int]$_ })
if ($versionParts[0] -gt 255 -or $versionParts[1] -gt 255 -or $versionParts[2] -gt 65535) {
    throw 'A versão excede os limites do Windows Installer: 255.255.65535.'
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $workspaceRoot "artifacts/releases/$Version"
}

$OutputPath = [IO.Path]::GetFullPath($OutputPath)
$publishPath = Join-Path $OutputPath 'publish'
$msiName = "A2D.AlertMigrator-$Version-win-x64.msi"
$msiPath = Join-Path $OutputPath $msiName
$checksumPath = "$msiPath.sha256"

New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
New-Item -ItemType Directory -Path $publishPath -Force | Out-Null

Write-Host 'Compilando a solução...' -ForegroundColor Cyan
& $dotnetCommand.Source build $solutionPath `
    --configuration Release `
    --nologo `
    --tl:off `
    "-p:Version=$Version" `
    '-p:ContinuousIntegrationBuild=true'
if ($LASTEXITCODE -ne 0) {
    throw "A compilação falhou. Código: $LASTEXITCODE"
}

Write-Host 'Executando os testes de infraestrutura...' -ForegroundColor Cyan
& $dotnetCommand.Source run --project $infrastructureTests --configuration Release --no-build -- $workspaceRoot
if ($LASTEXITCODE -ne 0) {
    throw "Os testes de infraestrutura falharam. Código: $LASTEXITCODE"
}

Write-Host 'Executando os testes da aplicação desktop...' -ForegroundColor Cyan
& $dotnetCommand.Source run --project $desktopTests --configuration Release --no-build
if ($LASTEXITCODE -ne 0) {
    throw "Os testes da aplicação desktop falharam. Código: $LASTEXITCODE"
}

Write-Host 'Publicando e assinando o executável...' -ForegroundColor Cyan
& $publishScript `
    -Configuration Release `
    -OutputPath $publishPath `
    -Version $Version `
    -CertificateThumbprint $CertificateThumbprint `
    -CertificateStore $CertificateStore `
    -TimestampUrl $TimestampUrl `
    -RequireSignature
if ($LASTEXITCODE -ne 0) {
    throw "A publicação do executável falhou. Código: $LASTEXITCODE"
}

Write-Host 'Gerando o MSI...' -ForegroundColor Cyan
& $dotnetCommand.Source build $installerProject `
    --configuration Release `
    --nologo `
    --tl:off `
    "-p:ProductVersion=$Version" `
    "-p:PublishDir=$publishPath" `
    "-p:OutputPath=$OutputPath"
if ($LASTEXITCODE -ne 0) {
    throw "A geração do MSI falhou. Código: $LASTEXITCODE"
}

if (-not (Test-Path -LiteralPath $msiPath -PathType Leaf)) {
    throw "O MSI esperado não foi encontrado: $msiPath"
}

Write-Host 'Assinando o MSI...' -ForegroundColor Cyan
& $signingScript `
    -ArtifactPath $msiPath `
    -CertificateThumbprint $CertificateThumbprint `
    -CertificateStore $CertificateStore `
    -TimestampUrl $TimestampUrl
if ($LASTEXITCODE -ne 0) {
    throw "A assinatura do MSI falhou. Código: $LASTEXITCODE"
}

$hash = (Get-FileHash -LiteralPath $msiPath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($checksumPath, "$hash  $msiName`n", [Text.UTF8Encoding]::new($false))

Write-Host "MSI assinado: $msiPath" -ForegroundColor Green
Write-Host "SHA-256: $hash" -ForegroundColor Green
