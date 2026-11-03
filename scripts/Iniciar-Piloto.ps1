#requires -Version 5.1
$ErrorActionPreference = 'Stop'

$program = Join-Path $PSScriptRoot 'CaixaLume.exe'
if (-not (Test-Path -LiteralPath $program -PathType Leaf)) {
    throw 'CaixaLume.exe não foi encontrado. Abra este arquivo dentro da pasta da versão instalada.'
}
if ((Get-Process -Name 'CaixaLume' -ErrorAction SilentlyContinue) -or
    (Get-Process -Name 'MercadinhoPdv' -ErrorAction SilentlyContinue)) {
    throw 'Feche a sessão normal do CaixaLume antes de iniciar o piloto.'
}

$pilotRoot = Join-Path $env:LOCALAPPDATA 'CaixaLume\piloto'
New-Item -ItemType Directory -Path $pilotRoot -Force | Out-Null
$pilotDatabase = [System.IO.Path]::GetFullPath((Join-Path $pilotRoot 'mercado-teste.db'))
. (Join-Path $PSScriptRoot 'Caminhos-CaixaLume.ps1')
$productionDatabase = Get-CaixaLumeDatabase
if ([string]::Equals($pilotDatabase, $productionDatabase, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'O banco de teste não pode ser igual ao banco principal.'
}

# Start-Process herda o ambiente deste processo. A variável não fica gravada no Windows.
$oldValue = [Environment]::GetEnvironmentVariable('CAIXALUME_DATABASE', 'Process')
try {
    [Environment]::SetEnvironmentVariable('CAIXALUME_DATABASE', $pilotDatabase, 'Process')
    Start-Process -FilePath $program -WorkingDirectory $PSScriptRoot -ErrorAction Stop | Out-Null
    Write-Output "Piloto iniciado com banco separado: $pilotDatabase"
}
finally {
    [Environment]::SetEnvironmentVariable('CAIXALUME_DATABASE', $oldValue, 'Process')
}
