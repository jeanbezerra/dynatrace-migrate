param(
    [ValidateRange(1, 1000)]
    [int]$Count = 300,

    [string]$OutputDirectory = 'samples/json/generated-300'
)

$ErrorActionPreference = 'Stop'
$workspaceRoot = Split-Path -Parent $PSScriptRoot
$samplesRoot = [System.IO.Path]::GetFullPath((Join-Path $workspaceRoot 'samples/json'))
$templatePath = Join-Path $samplesRoot 'checkout.json'
$targetPath = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    [System.IO.Path]::GetFullPath($OutputDirectory)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $workspaceRoot $OutputDirectory))
}

if (-not $targetPath.StartsWith($samplesRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "A pasta de saida deve ficar dentro de $samplesRoot"
}

[System.IO.Directory]::CreateDirectory($targetPath) | Out-Null
$templateJson = [System.IO.File]::ReadAllText($templatePath)
$utf8WithoutBom = New-Object System.Text.UTF8Encoding($false, $true)
$cedilla = [char]0x00E7
$aTilde = [char]0x00E3
$eAcute = [char]0x00E9
$eCircumflex = [char]0x00EA
$applicationWord = 'Aplica' + $cedilla + $aTilde + 'o'
$applicationWordLower = 'aplica' + $cedilla + $aTilde + 'o'
$syntheticWord = 'sint' + $eAcute + 'tica'
$latencyWord = 'Lat' + $eCircumflex + 'ncia'

for ($index = 1; $index -le $Count; $index++) {
    $sequence = $index.ToString('000')
    $teamSequence = ((($index - 1) % 12) + 1).ToString('00')
    $applicationId = "sample-app-$sequence"
    $document = $templateJson | ConvertFrom-Json

    $document.application.id = $applicationId
    $document.application.name = "$applicationWord de Exemplo $sequence"
    $document.application.description = "$applicationWord $syntheticWord $sequence para testes locais de migra$cedilla$aTilde" + 'o'
    $document.application.owners = @("sre-team-$teamSequence")
    $document.application.labels.businessUnit = "unit-$((($index - 1) % 10) + 1)"
    $document.application.labels.environment = @('development', 'staging', 'production')[($index - 1) % 3]

    $document.defaults.alertGroup = $applicationId
    $document.defaults.profile.name = "NOC - Equipe $teamSequence"
    $document.defaults.profile.tagFilters = @("team:team-$teamSequence")

    $document.rules[0].id = "APPD-SAMPLE-$sequence-ERROR"
    $document.rules[0].name = "$applicationWord $sequence - quantidade de erros"
    $document.rules[0].groupId = "$applicationId-errors"
    $document.rules[0].targets[0].value = "$applicationId-api"
    $document.rules[0].targets[1].value = "$applicationId-worker"
    $document.rules[0].detector.threshold = 5 + ($index % 20)
    $document.rules[0].event.name = "$applicationWord $sequence com erros acima do limite"
    $document.rules[0].event.description = "Erros detectados nos servi$cedilla" + "os da $applicationWordLower $sequence."

    $document.rules[1].id = "APPD-SAMPLE-$sequence-LATENCY"
    $document.rules[1].name = "$applicationWord $sequence - anomalia de $($latencyWord.ToLowerInvariant())"
    $document.rules[1].targets[0].value = "SERVICE-$($index.ToString('X16'))"
    $document.rules[1].event.name = "$latencyWord anormal na $applicationWordLower $sequence"
    $document.rules[1].event.alertGroup = $applicationId

    $outputFile = Join-Path $targetPath "app-$sequence.json"
    $outputJson = $document | ConvertTo-Json -Depth 100
    $outputBytes = $utf8WithoutBom.GetBytes($outputJson)
    if ($outputBytes.Length -ge 3 -and $outputBytes[0] -eq 0xEF -and $outputBytes[1] -eq 0xBB -and $outputBytes[2] -eq 0xBF) {
        throw "Falha ao garantir UTF-8 sem BOM em: $outputFile"
    }

    [System.IO.File]::WriteAllBytes($outputFile, $outputBytes)
}

Write-Host "$Count arquivos de aplicacao gerados em: $targetPath"
