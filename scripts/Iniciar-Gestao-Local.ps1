# Piloto local; encerre com Ctrl+C. Não publica o servidor na internet.
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$managementFolder = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'CaixaLumeGestao'
New-Item -ItemType Directory -Force -Path $managementFolder | Out-Null
$previousEnvironment = @{}
foreach ($name in @('CAIXALUME_ADMIN_TOKEN','CAIXALUME_MANAGEMENT_DB','ASPNETCORE_ENVIRONMENT','ASPNETCORE_URLS')) { $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name,'Process') }
try {
    $randomBytes = New-Object byte[] 32
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($randomBytes) } finally { $generator.Dispose() }
    $env:CAIXALUME_ADMIN_TOKEN = ([BitConverter]::ToString($randomBytes)).Replace('-','')
    $env:CAIXALUME_MANAGEMENT_DB = Join-Path $managementFolder 'management.db'
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = 'http://127.0.0.1:5187'
    Write-Host 'Painel local: http://127.0.0.1:5187'
    Write-Host 'Chave de configuracao inicial (usada somente antes de criar sua conta):'
    Write-Host $env:CAIXALUME_ADMIN_TOKEN
    Write-Host 'Este modo permite revisar o painel. O aplicativo exige HTTPS para vincular.'
    & dotnet run --project (Join-Path $projectRoot 'Server/CaixaLume.Management.csproj') --no-launch-profile
    if ($LASTEXITCODE -ne 0) { throw 'O servidor encerrou com erro.' }
} finally {
    foreach ($name in $previousEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name,$previousEnvironment[$name],'Process') }
}
