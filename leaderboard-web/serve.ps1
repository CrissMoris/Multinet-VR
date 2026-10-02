<#
.SYNOPSIS
  Serves the MultiTravel leaderboard folder on the local network (static files only).

.DESCRIPTION
  Starts a tiny static web server in this folder and prints the URLs to open on
  the TV / tablet. Uses `npx serve` when Node.js is installed, otherwise
  `python -m http.server`. Stop it with Ctrl+C.

.PARAMETER Port
  TCP port to listen on (default 8080).

.EXAMPLE
  .\serve.ps1
  .\serve.ps1 -Port 9000
#>
[CmdletBinding()]
param(
  [ValidateRange(1, 65535)]
  [int]$Port = 8080
)

$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot

$lanAddresses = @()
try {
  $lanAddresses = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction Stop |
    Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } |
    Sort-Object -Property InterfaceMetric, IPAddress |
    Select-Object -ExpandProperty IPAddress
} catch {
  $lanAddresses = @()
}

Write-Host ''
Write-Host 'MultiTravel Valiz Challenge - Liderlik Tablosu' -ForegroundColor Cyan
Write-Host "Bu bilgisayarda : http://localhost:$Port/"
foreach ($ip in $lanAddresses) {
  Write-Host "Ağdaki cihazlar : http://$($ip):$Port/"
}
Write-Host 'Belirli bir etkinlik için: ...?event=<etkinlik-slug>'
Write-Host 'Durdurmak için Ctrl+C.' -ForegroundColor DarkGray
Write-Host ''

$npx = Get-Command npx -ErrorAction SilentlyContinue
$python = Get-Command python -ErrorAction SilentlyContinue
if (-not $python) { $python = Get-Command py -ErrorAction SilentlyContinue }

if ($npx) {
  & $npx.Source --yes serve -l $Port --no-clipboard .
} elseif ($python) {
  & $python.Source -m http.server $Port --bind 0.0.0.0
} else {
  Write-Host 'Node.js (npx) veya Python bulunamadı. https://nodejs.org veya https://python.org adresinden birini kurun.' -ForegroundColor Red
  exit 1
}
