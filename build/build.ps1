<#
  Bouwt beide binaries met de in-box .NET Framework C#-compiler (geen SDK nodig):
    1) vc-rdp-launch.exe  — de .rdp/.rdpw-handler (self-healing).
    2) vc-rdp-setup.exe    — de zelfstandige installer; bevat (1) als embedded
                             resource + admin-manifest. Dit bestand geef je klanten.

  Signen (productie, op een box met signtool + VC-cert):
    signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 `
       /n "Virtual Computing" build\vc-rdp-launch.exe build\vc-rdp-setup.exe
  Teken vc-rdp-launch.exe VOOR het embedden, en daarna vc-rdp-setup.exe.
  Op WDAC-locked werkplekken zijn gesignde + allowlisted binaries vereist.
#>
param(
    [string]$Csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
)
$ErrorActionPreference = 'Stop'
$root   = Split-Path $PSScriptRoot -Parent
$src    = Join-Path $root 'src'
$outDir = $PSScriptRoot
$launch = Join-Path $outDir 'vc-rdp-launch.exe'
$setup  = Join-Path $outDir 'vc-rdp-setup.exe'

if (-not (Test-Path $Csc)) { throw "csc niet gevonden: $Csc" }

$icon    = Join-Path $src 'red.ico'
$argIco  = if (Test-Path $icon) { "/win32icon:$icon" } else { $null }

# 1) Launcher
$srcLaunch = Join-Path $src 'VcRdpLaunch.cs'
& $Csc /nologo /target:winexe /platform:x64 "/out:$launch" $argIco /r:System.Windows.Forms.dll $srcLaunch
if ($LASTEXITCODE -ne 0) { throw "launcher-build faalde (exit $LASTEXITCODE)" }
Write-Host "OK  -> $launch ($((Get-Item $launch).Length) bytes)"

# 2) Setup, met launcher embedded (logische naam moet 'vc-rdp-launch.exe' zijn)
$srcSetup = Join-Path $src 'VcRdpSetup.cs'
$manifest = Join-Path $src 'setup.manifest'
$argOut   = "/out:$setup"
$argMan   = "/win32manifest:$manifest"
$argRes   = "/resource:$launch,vc-rdp-launch.exe"
& $Csc /nologo /target:winexe /platform:x64 $argOut $argMan $argRes $argIco /r:System.Windows.Forms.dll $srcSetup
if ($LASTEXITCODE -ne 0) { throw "setup-build faalde (exit $LASTEXITCODE)" }
Write-Host "OK  -> $setup ($((Get-Item $setup).Length) bytes)"
Write-Host "Klaar. Geef vc-rdp-setup.exe aan de klant (run als admin / Intune /silent)."
