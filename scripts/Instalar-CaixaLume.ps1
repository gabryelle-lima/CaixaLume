#requires -Version 5.1
param(
    [switch]$Reverter,
    [string]$Pacote = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'
$installRoot = [System.IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'CaixaLume'))
$versionsRoot = [System.IO.Path]::GetFullPath((Join-Path $installRoot 'versoes'))
$currentFile = Join-Path $installRoot 'versao-atual.txt'
$previousFile = Join-Path $installRoot 'versao-anterior.txt'

function Assert-Within([string]$base, [string]$candidate) {
    $baseFull = [System.IO.Path]::GetFullPath($base).TrimEnd([System.IO.Path]::DirectorySeparatorChar)
    $candidateFull = [System.IO.Path]::GetFullPath($candidate)
    if (-not $candidateFull.StartsWith($baseFull + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Caminho fora da pasta de instalação: $candidateFull"
    }
    return $candidateFull
}

function Read-Version([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    $value = ([System.IO.File]::ReadAllText($path)).Trim()
    if ($value -notmatch '^\d+\.\d+\.\d+$') { throw "Identificação de versão inválida: $path" }
    return $value
}

function Ensure-Closed {
    if ((Get-Process -Name 'MercadinhoPdv' -ErrorAction SilentlyContinue) -or
        (Get-Process -Name 'CaixaLume' -ErrorAction SilentlyContinue)) {
        throw 'Feche o CaixaLume/MercadinhoPdv antes de instalar ou voltar uma versão.'
    }
}

function Set-Shortcut([string]$version) {
    $program = Assert-Within $versionsRoot (Join-Path (Join-Path $versionsRoot $version) 'CaixaLume.exe')
    if (-not (Test-Path -LiteralPath $program -PathType Leaf)) { throw "Executável ausente: $program" }
    $shell = New-Object -ComObject WScript.Shell
    $startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
    $locations = @((Join-Path $startMenu 'CaixaLume.lnk'))
    $desktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
    if (-not [string]::IsNullOrWhiteSpace($desktop) -and (Test-Path -LiteralPath $desktop)) {
        $locations += Join-Path $desktop 'CaixaLume.lnk'
    }
    foreach ($location in $locations) {
        $shortcut = $shell.CreateShortcut($location)
        $shortcut.TargetPath = $program
        $shortcut.WorkingDirectory = Split-Path -Parent $program
        $shortcut.IconLocation = $program
        $shortcut.Description = "CaixaLume $version"
        $shortcut.Save()
    }
}

Ensure-Closed

if ($Reverter) {
    $current = Read-Version $currentFile
    $previous = Read-Version $previousFile
    if ($null -eq $current -or $null -eq $previous) { throw 'Não há uma versão anterior registrada para voltar.' }
    Set-Shortcut $previous
    [System.IO.File]::WriteAllText($currentFile, $previous + [Environment]::NewLine)
    [System.IO.File]::WriteAllText($previousFile, $current + [Environment]::NewLine)
    Write-Output "CaixaLume voltou para a versão $previous. Os dados não foram alterados."
    exit 0
}

$packageRoot = [System.IO.Path]::GetFullPath($Pacote)
$versionFile = Join-Path $packageRoot 'VERSAO.txt'
$manifestFile = Join-Path $packageRoot 'manifesto-sha256.csv'
if (-not (Test-Path -LiteralPath $versionFile -PathType Leaf) -or -not (Test-Path -LiteralPath $manifestFile -PathType Leaf)) {
    throw 'Extraia o ZIP completo antes de instalar. VERSAO.txt ou manifesto-sha256.csv está ausente.'
}
$version = Read-Version $versionFile
$target = Assert-Within $versionsRoot (Join-Path $versionsRoot $version)
$targetExists = Test-Path -LiteralPath $target
foreach ($asset in @('CaixaLume.exe', 'Instalar-CaixaLume.ps1', 'Instalar-CaixaLume.cmd', 'Iniciar-Piloto.ps1', 'Iniciar-Piloto.cmd', 'COMO_EXECUTAR.md', 'ROTEIRO_PILOTO.md', 'VALIDACAO_MANUAL.md', 'Web\shell.html', 'Web\shell.js', 'Web\shell.css', 'Web\home.html', 'Web\index.html', 'Web\products.html', 'Web\reports.html', 'Web\modules.html')) {
    if (-not (Test-Path -LiteralPath (Join-Path $packageRoot $asset) -PathType Leaf)) { throw "Pacote incompleto: $asset" }
}
$manifest = @(Import-Csv -LiteralPath $manifestFile)
if ($manifest.Count -lt 10) { throw 'Manifesto do pacote incompleto.' }
$manifestPaths = @{}
foreach ($asset in @('CaixaLume.exe', 'Instalar-CaixaLume.ps1', 'Instalar-CaixaLume.cmd', 'Iniciar-Piloto.ps1', 'Iniciar-Piloto.cmd', 'COMO_EXECUTAR.md', 'ROTEIRO_PILOTO.md', 'VALIDACAO_MANUAL.md', 'VERSAO.txt')) {
    if ($asset -notin @($manifest | ForEach-Object { $_.Arquivo })) { throw "Arquivo fora do manifesto: $asset" }
}
foreach ($record in $manifest) {
    if ([string]::IsNullOrWhiteSpace($record.Arquivo) -or $record.Hash -notmatch '^[0-9a-fA-F]{64}$' -or [System.IO.Path]::IsPathRooted($record.Arquivo)) {
        throw 'Manifesto do pacote inválido.'
    }
    $file = Assert-Within $packageRoot (Join-Path $packageRoot $record.Arquivo)
    $relative = $file.Substring($packageRoot.TrimEnd('\').Length + 1)
    if ($relative -eq 'manifesto-sha256.csv' -or $manifestPaths.ContainsKey($relative)) { throw "Arquivo repetido no manifesto: $relative" }
    $manifestPaths[$relative] = $true
    if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $record.Hash) {
        throw "Arquivo ausente ou alterado no pacote: $($record.Arquivo)"
    }
    if ($targetExists) {
        $installedFile = Assert-Within $target (Join-Path $target $record.Arquivo)
        if (-not (Test-Path -LiteralPath $installedFile -PathType Leaf) -or
            (Get-FileHash -LiteralPath $installedFile -Algorithm SHA256).Hash -ne $record.Hash) {
            throw "A versão $version já está instalada, mas difere do pacote: $($record.Arquivo)"
        }
    }
}
foreach ($file in Get-ChildItem -LiteralPath $packageRoot -Recurse -Force -File) {
    $relative = $file.FullName.Substring($packageRoot.TrimEnd('\').Length + 1)
    if ($relative -ne 'manifesto-sha256.csv' -and -not $manifestPaths.ContainsKey($relative)) {
        throw "Arquivo fora do manifesto: $relative"
    }
}

$current = Read-Version $currentFile
if ($null -ne $current -and [version]$version -lt [version]$current) {
    throw 'O pacote é mais antigo que a versão ativa. Use -Reverter para voltar a uma versão anterior registrada.'
}
New-Item -ItemType Directory -Path $versionsRoot -Force | Out-Null
$stage = $null
try {
    if (-not $targetExists) {
        $stage = Assert-Within $versionsRoot (Join-Path $versionsRoot ('.instalando-' + [Guid]::NewGuid().ToString('N')))
        New-Item -ItemType Directory -Path $stage | Out-Null
        Get-ChildItem -LiteralPath $packageRoot -Force | Copy-Item -Destination $stage -Recurse -Force
        if (-not (Test-Path -LiteralPath (Join-Path $stage 'CaixaLume.exe') -PathType Leaf)) { throw 'A cópia do programa falhou.' }
    }

    # O banco fica fora das pastas de versão. A cópia ocorre apenas com o aplicativo fechado.
    $database = if ([string]::IsNullOrWhiteSpace($env:MERCADINHOPDV_DATABASE)) {
        Join-Path $env:LOCALAPPDATA 'MercadinhoPdv\dados\mercadinho.db'
    } else { [System.IO.Path]::GetFullPath($env:MERCADINHOPDV_DATABASE) }
    if (Test-Path -LiteralPath $database -PathType Leaf) {
        $backupRoot = Join-Path $installRoot 'backups-antes-atualizacao'
        New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
        $backup = Join-Path $backupRoot ("antes-$version-$((Get-Date).ToString('yyyyMMdd-HHmmss'))-$([Guid]::NewGuid().ToString('N'))")
        New-Item -ItemType Directory -Path $backup | Out-Null
        foreach ($source in @($database, ($database + '-wal'), ($database + '-shm'))) {
            if (Test-Path -LiteralPath $source -PathType Leaf) {
                $copy = Join-Path $backup (Split-Path -Leaf $source)
                Copy-Item -LiteralPath $source -Destination $copy -ErrorAction Stop
                if ((Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash -ne
                    (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash) {
                    throw 'A cópia de segurança do banco não passou na verificação SHA-256.'
                }
            }
        }
        Write-Output "Cópia de segurança do banco: $backup"
    }

    # A versão antiga permanece intacta; somente a pasta nova é ativada.
    if (-not $targetExists) {
        Assert-Within $versionsRoot $stage | Out-Null
        Assert-Within $versionsRoot $target | Out-Null
        if (Test-Path -LiteralPath $target) { throw 'Outra instalação criou esta versão ao mesmo tempo. Feche o outro instalador e tente novamente.' }
        Move-Item -LiteralPath $stage -Destination $target
    }
    Set-Shortcut $version
    if ($current -ne $version) {
        if ($null -ne $current) { [System.IO.File]::WriteAllText($previousFile, $current + [Environment]::NewLine) }
        [System.IO.File]::WriteAllText($currentFile, $version + [Environment]::NewLine)
    }
    Write-Output "CaixaLume $version instalado. Abra o atalho CaixaLume no menu Iniciar ou na área de trabalho."
    if ($null -ne $current) { Write-Output "A versão anterior $current foi preservada. Para voltar, execute Instalar-CaixaLume.ps1 -Reverter." }
}
finally {
    if ($null -ne $stage -and (Test-Path -LiteralPath $stage)) {
        Assert-Within $versionsRoot $stage | Out-Null
        Remove-Item -LiteralPath $stage -Recurse -Force
    }
}
