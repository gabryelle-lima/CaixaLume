param([Parameter(Mandatory=$true)][string]$Origem)
$ErrorActionPreference = 'Stop'
try {
    $source = (Resolve-Path -LiteralPath $Origem).Path.TrimEnd('\')
    $parent = Split-Path -Parent $source
    $destination = [IO.Path]::GetFullPath((Join-Path $parent 'CaixaLume'))
    if ((Split-Path -Leaf $source) -ne 'MercadinhoPdv' -or
        -not (Test-Path -LiteralPath (Join-Path $source 'CaixaLume.csproj')) -or
        (Split-Path -Parent $destination) -ne $parent) { throw 'A pasta de origem nao corresponde ao projeto esperado.' }
    if (Test-Path -LiteralPath $destination) { throw 'A pasta CaixaLume ja existe. Nenhum arquivo foi substituido.' }
    Set-Location -LiteralPath $parent
    Move-Item -LiteralPath $source -Destination $destination
    $desktop = [Environment]::GetFolderPath('Desktop')
    $wsh = New-Object -ComObject WScript.Shell
    Get-ChildItem -LiteralPath $desktop -Filter '*.lnk' | ForEach-Object {
        $link = $wsh.CreateShortcut($_.FullName)
        $changed = $false
        foreach ($property in @('TargetPath','WorkingDirectory','Arguments','IconLocation')) {
            if ($link.$property -and $link.$property.Contains($source)) {
                $link.$property = $link.$property.Replace($source,$destination)
                $changed = $true
            }
        }
        if ($changed) { $link.Save() }
    }
    $note = Join-Path $desktop 'Como acessar o Aurora.txt'
    if (Test-Path -LiteralPath $note) {
        $content = [IO.File]::ReadAllText($note)
        if ($content.Contains($source)) { [IO.File]::WriteAllText($note,$content.Replace($source,$destination),[Text.UTF8Encoding]::new($true)) }
    }
    Write-Output ('Concluido. Projeto e atalhos: ' + $destination)
    Write-Output 'Reabra o projeto usando o atalho CaixaLume - Projeto.'
}
catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host 'Se a pasta estiver em uso, feche o projeto no Codex, o CaixaLume e os terminais abertos nessa pasta. Depois tente novamente.'
    exit 1
}
