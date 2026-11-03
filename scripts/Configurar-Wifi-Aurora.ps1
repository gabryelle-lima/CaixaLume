param([string]$ServerAddress = '192.168.0.7', [string]$WifiAddress = '192.168.0.8')
$ErrorActionPreference = 'Stop'
foreach ($address in @($ServerAddress,$WifiAddress)) {
    $parsedAddress = $null
    if ($address -notmatch '^192\.168\.0\.\d{1,3}$' -or -not [Net.IPAddress]::TryParse($address,[ref]$parsedAddress)) { throw 'Endereco de rede local invalido.' }
}
$scriptPath = Join-Path $PSScriptRoot 'gestao-linux/configurar-wifi-https.sh'
$destination = 'gabryelle@' + $ServerAddress
$sshOptions = @('-o','HostKeyAlias=192.168.0.6','-o','StrictHostKeyChecking=yes','-o','ConnectTimeout=10')
$remoteName = 'caixalume-wifi-' + (Get-Date -Format 'yyyyMMddHHmmss') + '.sh'
Write-Host 'Digite a senha do Linux quando o SSH solicitar. Nenhuma senha sera salva.'
& scp @sshOptions $scriptPath ($destination + ':~/' + $remoteName)
if ($LASTEXITCODE -ne 0) { throw 'Falha na copia. Nenhuma configuracao foi alterada.' }
& ssh @sshOptions -t $destination ('sudo bash ' + $remoteName + ' ' + $ServerAddress + ' ' + $WifiAddress)
if ($LASTEXITCODE -ne 0) { throw 'A configuracao remota nao foi confirmada. Nao retire o cabo; informe a mensagem final.' }
Write-Host 'Conferindo HTTPS pelo Windows, sem ignorar o certificado...'
$result = Invoke-RestMethod -Uri ('https://' + $WifiAddress + ':5443/health') -TimeoutSec 10
if ($result.status -ne 'ok') { throw 'O Windows nao confirmou a resposta esperada.' }
Write-Host ('SUCESSO: Windows confirmou HTTPS em https://' + $WifiAddress + ':5443')
Write-Host 'Mantenha o cabo conectado. Ainda precisamos ajustar o endereco salvo no CaixaLume e o tunel SSH.'
