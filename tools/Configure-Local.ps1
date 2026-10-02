$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Push-Location (Join-Path $repo 'backend')
try { $status = supabase status --output json 2>$null | ConvertFrom-Json } finally { Pop-Location }
if (!$status.API_URL -or !$status.ANON_KEY) { throw 'Start local Supabase first: cd backend; supabase start' }
$config = Get-Content (Join-Path $repo 'docs/config.example.json') -Raw | ConvertFrom-Json
$config.backend.supabaseUrl = $status.API_URL
$config.backend.supabaseAnonKey = $status.ANON_KEY
$config.backend.eventAccessCode = 'CHANGE-ME-BEFORE-EVENT'
$config.backend.stationId = 'LOCAL-DEV'
$folder = Join-Path $repo 'MultiTravelValizChallenge/Assets/StreamingAssets'
New-Item -ItemType Directory -Force -Path $folder | Out-Null
$json = $config | ConvertTo-Json -Depth 8
[IO.File]::WriteAllText((Join-Path $folder 'multitravel.config.json'), $json)
$web = @{SUPABASE_URL=$status.API_URL;SUPABASE_ANON_KEY=$status.ANON_KEY;EVENT_SLUG='multitravel-2026';REFRESH_SECONDS=5;TOP_N=20} | ConvertTo-Json -Compress
[IO.File]::WriteAllText((Join-Path $repo 'leaderboard-web/config.local.js'), "window.MT_CONFIG = $web;")
Write-Host 'Local-only Unity and leaderboard configuration written (git-ignored). No production deployment configured.'
