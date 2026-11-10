# TrustTunnel launcher for Windows (Sabi VPN)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$cfg    = Join-Path $PSScriptRoot 'trusttunnel_client.toml'
$bypass = Join-Path $PSScriptRoot 'bypass.txt'

foreach ($f in 'trusttunnel_client.exe', 'setup_wizard.exe', 'wintun.dll') {
    if (-not (Test-Path (Join-Path $PSScriptRoot $f))) { throw "$f is missing. Keep all files in the same folder." }
}

# ---- first run: import the tt:// link ----
if (-not (Test-Path $cfg)) {
    Write-Host ''
    Write-Host '=== First-time setup ===' -ForegroundColor Cyan
    Write-Host 'Copy your tt:// link from the panel, paste it here (right-click or Ctrl+V) and press Enter.'
    do {
        $link = (Read-Host 'Link').Trim().Trim('"').Trim("'")
        if ($link -notlike 'tt://*') { Write-Host 'The link must start with tt://' -ForegroundColor Yellow }
    } until ($link -like 'tt://*')
    & (Join-Path $PSScriptRoot 'setup_wizard.exe') --mode non-interactive --deeplink $link --settings $cfg
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $cfg)) { throw 'Could not import the link. Check that you copied all of it.' }
    Write-Host 'Settings saved.' -ForegroundColor Green
}

# ---- apply bypass list (sites that should NOT go through the VPN) on every start ----
if (Test-Path $bypass) {
    $items = Get-Content -LiteralPath $bypass | ForEach-Object { $_.Trim() } |
             Where-Object { $_ -and -not $_.StartsWith('#') } |
             ForEach-Object { '"' + ($_ -replace '["\\]', '') + '"' }
    $text = [IO.File]::ReadAllText($cfg)
    $line = 'exclusions = [' + ($items -join ', ') + ']'
    $text = [regex]::Replace($text, '(?m)^exclusions = .*$', { param($m) $line })
    [IO.File]::WriteAllText($cfg, $text)
}

Write-Host ''
Write-Host 'Connecting...  keep this window open.  Press Ctrl+C to disconnect.' -ForegroundColor Green
Write-Host ''
& (Join-Path $PSScriptRoot 'trusttunnel_client.exe') -c $cfg
