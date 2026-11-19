# Builds dist\Bananarama.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
#   powershell -File tray\build.ps1          build
#   powershell -File tray\build.ps1 -Test    build, then compile and run the tray unit tests
param([switch] $Test, [string] $Out)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $Out) { $Out = Join-Path $root 'dist' }
$obj = Join-Path $root 'obj'
New-Item -ItemType Directory -Force $Out, $obj | Out-Null

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw 'csc.exe (.NET Framework 4.x) not found' }

$version = (Get-Content (Join-Path $root 'package.json') -Raw | ConvertFrom-Json).version
$major = $version.Split('.')[0]
@"
[assembly: System.Reflection.AssemblyTitle("Bananarama")]
[assembly: System.Reflection.AssemblyProduct("Bananarama")]
[assembly: System.Reflection.AssemblyDescription("Gemini Nano Banana images for Claude")]
[assembly: System.Reflection.AssemblyCompany("GunGreenEyes")]
[assembly: System.Reflection.AssemblyCopyright("MIT License")]
[assembly: System.Reflection.AssemblyVersion("$major.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("$major.0.0.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("v$major")]
"@ | Set-Content -Encoding UTF8 (Join-Path $obj 'AssemblyInfo.cs')

$refs = '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Web.Extensions.dll'
$common = @('/nologo', '/codepage:65001', '/optimize+', '/platform:anycpu') + $refs
$tray = Join-Path $PSScriptRoot ''

& $csc @common /target:winexe "/out:$Out\Bananarama.exe" "/win32icon:${tray}banana.ico" "/win32manifest:${tray}app.manifest" `
  "/resource:${tray}banana.ico,banana.ico" "/resource:${tray}banana-off.ico,banana-off.ico" `
  "${tray}Core.cs" "${tray}Tray.cs" "${tray}Forms.cs" (Join-Path $obj 'AssemblyInfo.cs')
if ($LASTEXITCODE) { throw "tray build failed ($LASTEXITCODE)" }
Write-Host "built $Out\Bananarama.exe (v$major)"

if ($Test) {
  & $csc @common /target:exe "/out:$obj\TrayTests.exe" "${tray}Core.cs" "${tray}tests\TrayTests.cs" (Join-Path $obj 'AssemblyInfo.cs')
  if ($LASTEXITCODE) { throw "test build failed ($LASTEXITCODE)" }
  & "$obj\TrayTests.exe" $root
  if ($LASTEXITCODE) { throw 'tray tests failed' }
}
