[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$BuildOnly,

    [string]$CertificateThumbprint,

    [ValidateSet('CurrentUser', 'LocalMachine')]
    [string]$CertificateStore = 'CurrentUser',

    [string]$TimestampUrl,

    [switch]$RequireSignature
)

$ErrorActionPreference = 'Stop'

$workspaceRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$projectDirectory = [IO.Path]::GetFullPath((Join-Path $workspaceRoot 'src/A2D.AlertMigrator.Desktop'))
$intermediateDirectory = [IO.Path]::GetFullPath(
    (Join-Path $projectDirectory "obj/$Configuration/net10.0-windows")
)
$outputPath = [IO.Path]::GetFullPath(
    (Join-Path $workspaceRoot 'artifacts/A2D.AlertMigrator-win-x64')
)
$executablePath = Join-Path $outputPath 'A2D.AlertMigrator.exe'
$publishScript = Join-Path $PSScriptRoot 'publish-win-x64.ps1'

function Clear-DesktopIntermediateDirectory {
    $projectPrefix = $projectDirectory.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar
    ) + [IO.Path]::DirectorySeparatorChar

    if (-not $intermediateDirectory.StartsWith(
        $projectPrefix,
        [StringComparison]::OrdinalIgnoreCase
    )) {
        throw "Diretorio intermediario fora do projeto Desktop: $intermediateDirectory"
    }

    if (Test-Path -LiteralPath $intermediateDirectory) {
        Write-Host 'Preparando o cache WPF para uma compilacao limpa...' -ForegroundColor DarkGray
        Remove-Item -LiteralPath $intermediateDirectory -Recurse -Force
    }
}

$runningInstances = @(
    Get-Process -Name 'A2D.AlertMigrator' -ErrorAction SilentlyContinue |
        Where-Object {
            try {
                [IO.Path]::GetFullPath($_.Path).Equals(
                    $executablePath,
                    [StringComparison]::OrdinalIgnoreCase
                )
            }
            catch {
                $false
            }
        }
)

if ($runningInstances.Count -gt 0) {
    $processIds = ($runningInstances.Id | Sort-Object) -join ', '
    Write-Host "ERRO: O A2D Alert Migrator ja esta aberto (PID: $processIds). Feche a janela antes de publicar novamente." -ForegroundColor Red
    exit 2
}

# A compilacao incremental de XAML pode manter referencias obsoletas a .g.cs.
# Regeneramos somente o cache intermediario do projeto Desktop.
Clear-DesktopIntermediateDirectory

$publishArguments = @{
    Configuration = $Configuration
    OutputPath = $outputPath
    CertificateStore = $CertificateStore
}

if (-not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    $publishArguments.CertificateThumbprint = $CertificateThumbprint
}

if (-not [string]::IsNullOrWhiteSpace($TimestampUrl)) {
    $publishArguments.TimestampUrl = $TimestampUrl
}

if ($RequireSignature) {
    $publishArguments.RequireSignature = $true
}

& $publishScript @publishArguments
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if ($BuildOnly) {
    Write-Host 'Publicacao do aplicativo Desktop concluida com sucesso.' -ForegroundColor Green
    exit 0
}

Write-Host 'Abrindo o pacote autocontido de arquivo unico...' -ForegroundColor DarkGray
& $executablePath
exit $LASTEXITCODE
