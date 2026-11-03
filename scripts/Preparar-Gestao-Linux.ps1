$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$bundleFolder = Join-Path $projectRoot 'artifacts/gestao-linux'
$appFolder = Join-Path $bundleFolder 'app'
New-Item -ItemType Directory -Force -Path $appFolder | Out-Null
& dotnet publish (Join-Path $projectRoot 'Server/CaixaLume.Management.csproj') -c Release --self-contained false -p:UseAppHost=false --no-restore -o $appFolder
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o servidor.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'gestao-linux/install.sh') -Destination $bundleFolder
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'gestao-linux/caixalume-gestao.service') -Destination $bundleFolder
$hashLines = Get-ChildItem -LiteralPath $bundleFolder -Recurse -File | Where-Object { $_.Name -ne 'SHA256SUMS' } | Sort-Object FullName | ForEach-Object {
    $relativeName = $_.FullName.Substring($bundleFolder.Length + 1).Replace('\','/')
    '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $relativeName
}
[IO.File]::WriteAllText((Join-Path $bundleFolder 'SHA256SUMS'),($hashLines -join "`n")+"`n",[Text.UTF8Encoding]::new($false))
$archivePath = Join-Path $projectRoot 'artifacts/caixalume-gestao-linux.tar.gz'
& tar -czf $archivePath -C $bundleFolder .
if ($LASTEXITCODE -ne 0) { throw 'Falha ao empacotar o servidor.' }
Get-Item -LiteralPath $archivePath | Select-Object FullName,Length
