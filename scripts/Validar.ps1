param([ValidateSet('Debug','Release')][string]$Configuracao = 'Debug')
$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$originalTemp = $env:TEMP
$originalTmp = $env:TMP
Push-Location $projectRoot
try {
    $env:TEMP = Join-Path $projectRoot 'artifacts\validation-temp'
    $env:TMP = $env:TEMP
    New-Item -ItemType Directory -Path $env:TEMP -Force | Out-Null
    & dotnet build Tests\MercadinhoPdv.IntegrationTests\MercadinhoPdv.IntegrationTests.csproj --configuration $Configuracao -p:UseAppHost=false
    if ($LASTEXITCODE -ne 0) { throw 'A compilação falhou.' }
    & dotnet "Tests\MercadinhoPdv.IntegrationTests\bin\$Configuracao\net10.0-windows\MercadinhoPdv.IntegrationTests.dll"
    if ($LASTEXITCODE -ne 0) { throw 'A suíte C# falhou.' }
    & node --test Tests\Web\pdv-domain.test.js Tests\Web\bridge.test.js
    if ($LASTEXITCODE -ne 0) { throw 'A suíte JavaScript falhou.' }
    foreach ($script in Get-ChildItem -LiteralPath Web -Filter '*.js') {
        & node --check $script.FullName
        if ($LASTEXITCODE -ne 0) { throw "Sintaxe inválida: $($script.Name)" }
    }
}
finally { $env:TEMP = $originalTemp; $env:TMP = $originalTmp; Pop-Location }
