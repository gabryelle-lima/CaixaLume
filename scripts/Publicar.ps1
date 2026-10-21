param([string]$Destino, [string]$Versao = '0.1.0')
$ErrorActionPreference = 'Stop'
if ($Versao -notmatch '^\d+\.\d+\.\d+$') { throw 'Informe a versão no formato principal.secundária.correção (exemplo: 0.1.0).' }
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($Destino)) {
    $Destino = Join-Path $projectRoot ('artifacts\publicacao\CaixaLume-' + $Versao + '-win-x64-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$Destino = [System.IO.Path]::GetFullPath($Destino)
if (Test-Path -LiteralPath $Destino) { throw 'Escolha uma pasta nova. A publicação não substitui instalações ou pastas existentes.' }
$archive = Join-Path (Split-Path -Parent $Destino) ('CaixaLume-' + $Versao + '-win-x64.zip')
if (Test-Path -LiteralPath $archive) { throw 'O pacote desta versão já existe. Incremente a versão antes de publicar novamente.' }
Push-Location $projectRoot
try {
    & dotnet publish MercadinhoPdv.csproj -p:PublishProfile=Windows-x64 -p:Version=$Versao --output $Destino
    if ($LASTEXITCODE -ne 0) { throw 'A publicação falhou.' }
    foreach ($asset in @('MercadinhoPdv.exe','Web\home.html','Web\index.html','Web\products.html','Web\reports.html','Web\modules.html','Web\app.js','Web\products.js','Web\reports.js','Web\modules.js','Web\pdv-domain.js','Web\bridge.js','Web\ui.js','Web\home.js','Web\styles.css','Web\home.css','Web\products.css','Web\reports.css','Web\modules.css')) {
        if (!(Test-Path -LiteralPath (Join-Path $Destino $asset))) { throw "Arquivo ausente na publicação: $asset" }
    }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\INSTALACAO_E_OPERACAO.md') -Destination (Join-Path $Destino 'COMO_EXECUTAR.md')
    [System.IO.File]::WriteAllText((Join-Path $Destino 'VERSAO.txt'), $Versao + [Environment]::NewLine)
    @(Get-ChildItem -LiteralPath $Destino -Recurse -File) |
        Get-FileHash -Algorithm SHA256 |
        Select-Object @{Name='Arquivo';Expression={[System.IO.Path]::GetRelativePath($Destino,$_.Path)}},Hash |
        Export-Csv -LiteralPath (Join-Path $Destino 'manifesto-sha256.csv') -NoTypeInformation -Encoding utf8
    Compress-Archive -Path (Join-Path $Destino '*') -DestinationPath $archive -CompressionLevel Optimal
    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    [System.IO.File]::WriteAllText($archive + '.sha256', "$hash  $(Split-Path -Leaf $archive)" + [Environment]::NewLine)
    Write-Output "Publicação criada: $Destino"
    Write-Output "Pacote de atualização: $archive"
}
finally { Pop-Location }
