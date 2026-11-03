#requires -Version 5.1
$ErrorActionPreference = 'Stop'

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectFile = Join-Path $projectRoot 'CaixaLume.csproj'
$localRoot = Join-Path $projectRoot 'artifacts\local'
$buildName = 'CaixaLume-local-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 6)
$output = Join-Path $localRoot $buildName
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'O .NET SDK não foi encontrado. Instale o SDK usado pelo projeto para abrir a versão local.'
}
if ((Get-Process -Name 'CaixaLume' -ErrorAction SilentlyContinue) -or
    (Get-Process -Name 'MercadinhoPdv' -ErrorAction SilentlyContinue)) {
    throw 'Feche o CaixaLume antes de preparar uma nova versão local.'
}

New-Item -ItemType Directory -Path $localRoot -Force | Out-Null
Write-Output 'Preparando versão local do CaixaLume...'
Push-Location $projectRoot
try {
    & dotnet publish $projectFile -p:PublishProfile=Windows-x64 -p:Version=0.1.4-local --no-restore --output $output
    if ($LASTEXITCODE -ne 0) {
        Write-Output 'Preparando dependências do projeto...'
        & dotnet restore $projectFile
        if ($LASTEXITCODE -ne 0) { throw 'Não foi possível preparar as dependências do projeto.' }
        & dotnet publish $projectFile -p:PublishProfile=Windows-x64 -p:Version=0.1.4-local --no-restore --output $output
        if ($LASTEXITCODE -ne 0) { throw 'Não foi possível compilar a versão local.' }
    }
}
finally { Pop-Location }

$program = Join-Path $output 'CaixaLume.exe'
if (-not (Test-Path -LiteralPath $program -PathType Leaf)) { throw 'O executável local não foi criado.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Iniciar-Piloto.ps1') -Destination $output
Write-Output "Versão local: $output"
& (Join-Path $output 'Iniciar-Piloto.ps1')
