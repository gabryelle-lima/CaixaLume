function Get-CaixaLumeDatabase {
    if (-not [string]::IsNullOrWhiteSpace($env:CAIXALUME_DATABASE)) { return [IO.Path]::GetFullPath($env:CAIXALUME_DATABASE) }
    if (-not [string]::IsNullOrWhiteSpace($env:MERCADINHOPDV_DATABASE)) { return [IO.Path]::GetFullPath($env:MERCADINHOPDV_DATABASE) }
    $legacy = Join-Path $env:LOCALAPPDATA 'MercadinhoPdv\dados\mercadinho.db'
    if (Test-Path -LiteralPath $legacy) { return $legacy }
    return (Join-Path $env:LOCALAPPDATA 'CaixaLume\dados\caixalume.db')
}
