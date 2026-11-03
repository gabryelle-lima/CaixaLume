param([switch]$Watch, [string]$ServerAddress = '192.168.0.8')
$ErrorActionPreference = 'Stop'
$panelUrl='http://127.0.0.1:5188'
function Test-Panel {
    try { $r=Invoke-RestMethod -Uri ($panelUrl+'/auth/status') -TimeoutSec 2; return ($null -ne $r.configured) } catch { return $false }
}
if ($Watch) {
    for ($attempt=0; $attempt -lt 90; $attempt++) {
        if (Test-Panel) { Start-Process $panelUrl; exit 0 }
        Start-Sleep -Seconds 1
    }
    exit 1
}
if ($ServerAddress -notmatch '^192\.168\.0\.\d{1,3}$') { throw 'Endereco local invalido.' }
if (Test-Panel) { Start-Process $panelUrl; exit 0 }
Write-Host 'Abrindo conexao protegida com o Aurora. Digite a senha do Linux se solicitar.'
Write-Host 'Mantenha esta janela aberta. O navegador abrira quando o painel responder.'
$watcher=Start-Process -FilePath 'powershell.exe' -WindowStyle Hidden -PassThru -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"'+$PSCommandPath+'"'),'-Watch')
try {
    & ssh -o HostKeyAlias=192.168.0.6 -o StrictHostKeyChecking=yes -o ExitOnForwardFailure=yes -o ConnectTimeout=10 -o ServerAliveInterval=30 -o ServerAliveCountMax=3 -N -L 127.0.0.1:5188:127.0.0.1:5187 ('gabryelle@'+$ServerAddress)
    if ($LASTEXITCODE -ne 0) { throw 'A conexao SSH encerrou com erro. Confira o IP e a rede do Aurora.' }
} finally {
    $watcher.Refresh()
    if (-not $watcher.HasExited) { Stop-Process -Id $watcher.Id -ErrorAction SilentlyContinue }
}
