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
    & dotnet build Tests\CaixaLume.IntegrationTests\CaixaLume.IntegrationTests.csproj --configuration $Configuracao -p:UseAppHost=false
    if ($LASTEXITCODE -ne 0) { throw 'A compilação falhou.' }
    & dotnet "Tests\CaixaLume.IntegrationTests\bin\$Configuracao\net10.0-windows\CaixaLume.IntegrationTests.dll"
    if ($LASTEXITCODE -ne 0) { throw 'A suíte C# falhou.' }
    foreach ($suite in @('--security','--financial','--stock','--ux','--central')) {
        & dotnet "Tests\CaixaLume.IntegrationTests\bin\$Configuracao\net10.0-windows\CaixaLume.IntegrationTests.dll" $suite
        if ($LASTEXITCODE -ne 0) { throw "A suite $suite falhou." }
    }
    & dotnet run --project Tests\CaixaLume.Management.UnitTests\CaixaLume.Management.UnitTests.csproj --configuration $Configuracao
    if ($LASTEXITCODE -ne 0) { throw 'Os testes de autenticacao falharam.' }
    & dotnet build Server\CaixaLume.Management.csproj --configuration Debug
    if ($LASTEXITCODE -ne 0) { throw 'A compilacao do servidor falhou.' }
    & python Tests\management_server_test.py
    if ($LASTEXITCODE -ne 0) { throw 'Os testes do servidor falharam.' }
    & node --test Tests\Web\*.test.js
    if ($LASTEXITCODE -ne 0) { throw 'A suíte JavaScript falhou.' }
    foreach ($script in Get-ChildItem -LiteralPath Web -Filter '*.js') {
        & node --check $script.FullName
        if ($LASTEXITCODE -ne 0) { throw "Sintaxe inválida: $($script.Name)" }
    }
}
finally { $env:TEMP = $originalTemp; $env:TMP = $originalTmp; Pop-Location }
