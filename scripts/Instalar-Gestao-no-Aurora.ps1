param([string]$ServerAddress = '192.168.0.8')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$archivePath = Join-Path $projectRoot 'artifacts/caixalume-gestao-linux.tar.gz'
if (-not (Test-Path -LiteralPath $archivePath)) { throw 'Prepare primeiro o pacote com scripts/Preparar-Gestao-Linux.ps1.' }
if ($ServerAddress -notmatch '^192\.168\.0\.\d{1,3}$') { throw 'Informe o endereco local do Aurora.' }
$releaseStamp = Get-Date -Format 'yyyyMMddHHmmss'
$remoteArchive = 'caixalume-gestao-' + $releaseStamp + '.tar.gz'
$remoteFolder = 'caixalume-install-' + $releaseStamp
$destination = 'gabryelle@' + $ServerAddress
# A mesma chave do Aurora ja verificada no endereco anterior; nunca aceitar outra automaticamente.
$sshOptions = @('-o','HostKeyAlias=192.168.0.6','-o','StrictHostKeyChecking=yes','-o','ConnectTimeout=10')
Write-Host 'Copia do pacote para o Aurora. Digite a senha do Linux quando solicitada.'
& scp @sshOptions $archivePath ($destination + ':~/' + $remoteArchive)
if ($LASTEXITCODE -ne 0) { throw 'A copia falhou. Nenhuma instalacao foi iniciada.' }
Write-Host 'Instalacao: o Linux pode pedir a senha de login e depois a senha para sudo.'
$remoteCommand = 'mkdir -m 700 ' + $remoteFolder + ' && tar -xzf ' + $remoteArchive + ' -C ' + $remoteFolder + ' && sudo bash ' + $remoteFolder + '/install.sh'
& ssh @sshOptions -t $destination $remoteCommand
if ($LASTEXITCODE -ne 0) { throw 'A instalacao nao foi confirmada. Envie a mensagem de erro, sem senhas ou chaves.' }
Write-Host 'Instalacao concluida. O servidor so aceita acesso local. Configuraremos o painel pelo tunel SSH.'
