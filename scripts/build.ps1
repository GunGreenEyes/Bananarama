# Builds dist\Bananarama-Setup-v<N>.exe:
#   1. downloads the pinned Node.js runtime (checksum-verified, cached in build\cache)
#   2. builds the tray app (tray\build.ps1), optionally running its tests
#   3. stages Bananarama.exe + node\node.exe + app\ (src, skills, package.json)
#   4. compiles installer\Bananarama.iss with Inno Setup
param([switch] $Test)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$root = Split-Path $PSScriptRoot -Parent
$NodeVersion = 'v26.13.1'   # Node.js LTS bundled with the installer

$pkg = Get-Content (Join-Path $root 'package.json') -Raw | ConvertFrom-Json
$major = $pkg.version.Split('.')[0]
$build = Join-Path $root 'build'; $cache = Join-Path $build 'cache'; $stage = Join-Path $build 'stage'
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $cache, $dist | Out-Null

# 1. Node.js runtime
$zipName = "node-$NodeVersion-win-x64.zip"
$zip = Join-Path $cache $zipName
$nodeExe = Join-Path $cache "node-$NodeVersion.exe"
if (-not (Test-Path $nodeExe)) {
  [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
  $base = "https://nodejs.org/dist/$NodeVersion"
  Write-Host "downloading $zipName"
  Invoke-WebRequest "$base/$zipName" -OutFile $zip -UseBasicParsing
  $sums = (Invoke-WebRequest "$base/SHASUMS256.txt" -UseBasicParsing).Content
  $want = ($sums -split "`n" | Where-Object { $_ -match "\s$([regex]::Escape($zipName))$" }) -split '\s+' | Select-Object -First 1
  $got = (Get-FileHash $zip -Algorithm SHA256).Hash
  if (-not $want -or $got -ne $want.ToUpper()) { Remove-Item $zip; throw "checksum mismatch for $zipName" }
  Add-Type -AssemblyName System.IO.Compression.FileSystem
  $z = [IO.Compression.ZipFile]::OpenRead($zip)
  try { [IO.Compression.ZipFileExtensions]::ExtractToFile(($z.Entries | Where-Object { $_.FullName -like '*/node.exe' }), $nodeExe, $true) }
  finally { $z.Dispose() }
  Remove-Item $zip
}

# 2. tray app
$trayArgs = @{}; if ($Test) { $trayArgs.Test = $true }
& (Join-Path $root 'tray\build.ps1') @trayArgs -Out (Join-Path $build 'tray')

# 3. stage
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force "$stage\node", "$stage\app" | Out-Null
Copy-Item (Join-Path $build 'tray\Bananarama.exe') $stage
Copy-Item $nodeExe "$stage\node\node.exe"
Copy-Item (Join-Path $root 'package.json'), (Join-Path $root 'LICENSE'), (Join-Path $root 'README.md') "$stage\app"
Copy-Item -Recurse (Join-Path $root 'src'), (Join-Path $root 'skills') "$stage\app"

# 4. installer
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
  Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found (winget install JRSoftware.InnoSetup)' }
& $iscc /Q "/DMajor=$major" "/DStage=$stage" "/O$dist" (Join-Path $root 'installer\Bananarama.iss')
if ($LASTEXITCODE) { throw "installer build failed ($LASTEXITCODE)" }
$out = Join-Path $dist "Bananarama-Setup-v$major.exe"
Write-Host ("built {0} ({1:N1} MB)" -f $out, ((Get-Item $out).Length / 1MB))
