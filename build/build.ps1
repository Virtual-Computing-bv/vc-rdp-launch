<#
  Bouwt beide binaries met de in-box .NET Framework C#-compiler (geen SDK nodig):
    1) vc-rdp-launch.exe  — de .rdp/.rdpw-handler (self-healing).
    2) vc-rdp-setup.exe    — de zelfstandige installer; bevat (1) als embedded
                             resource + admin-manifest. Dit bestand geef je klanten.

  SIGNEN (verplicht voor productie — anders blokkeert Defender/SmartScreen):
    .\build.ps1 -SignThumbprint <SHA1-thumbprint van je code-signing cert>
  De launcher wordt VOOR het embedden getekend en de setup erna, zodat beide
  een geldige handtekening hebben. Zie SIGNING.md voor cert-keuze + Azure
  Trusted Signing. Zonder -SignThumbprint bouwt 'ie ongesignd (alleen test).
#>
param(
    [string]$Csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
    [string]$SignThumbprint,
    [string]$TimestampUrl = "http://timestamp.digicert.com"
)
$ErrorActionPreference = 'Stop'
$root   = Split-Path $PSScriptRoot -Parent
$src    = Join-Path $root 'src'
$outDir = $PSScriptRoot
$launch = Join-Path $outDir 'vc-rdp-launch.exe'
$setup  = Join-Path $outDir 'vc-rdp-setup.exe'

if (-not (Test-Path $Csc)) { throw "csc niet gevonden: $Csc" }

function Invoke-Sign([string]$file) {
    if (-not $SignThumbprint) { return }
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $st = Get-ChildItem $kits -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
          Where-Object { $_.FullName -match '\\x64\\' } | Select-Object -Last 1
    if (-not $st) { throw "signtool.exe niet gevonden onder '$kits' (installeer de Windows 10/11 SDK)" }
    & $st.FullName sign /sha1 $SignThumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 $file
    if ($LASTEXITCODE -ne 0) { throw "signtool faalde voor $file" }
    Write-Host "  getekend: $file"
}

$icon    = Join-Path $src 'red.ico'
$argIco  = if (Test-Path $icon) { "/win32icon:$icon" } else { $null }

# 1) Launcher  (tekenen VOOR embedden, zodat de embedded copy ook gesigned is)
$srcLaunch = Join-Path $src 'VcRdpLaunch.cs'
& $Csc /nologo /target:winexe /platform:x64 "/out:$launch" $argIco /r:System.Windows.Forms.dll $srcLaunch
if ($LASTEXITCODE -ne 0) { throw "launcher-build faalde (exit $LASTEXITCODE)" }
Invoke-Sign $launch
Write-Host "OK  -> $launch ($((Get-Item $launch).Length) bytes)"

# 2) Setup, met (eventueel al gesignde) launcher embedded
$srcSetup = Join-Path $src 'VcRdpSetup.cs'
$manifest = Join-Path $src 'setup.manifest'
$argOut   = "/out:$setup"
$argMan   = "/win32manifest:$manifest"
$argRes   = "/resource:$launch,vc-rdp-launch.exe"
& $Csc /nologo /target:winexe /platform:x64 $argOut $argMan $argRes $argIco /r:System.Windows.Forms.dll $srcSetup
if ($LASTEXITCODE -ne 0) { throw "setup-build faalde (exit $LASTEXITCODE)" }
Invoke-Sign $setup
Write-Host "OK  -> $setup ($((Get-Item $setup).Length) bytes)"

if (-not $SignThumbprint) {
    Write-Host "LET OP: ongesignd gebouwd. Productie: .\build.ps1 -SignThumbprint <thumbprint>  (zie SIGNING.md)"
}
Write-Host "Klaar. Geef vc-rdp-setup.exe aan de klant (run als admin / Intune /silent)."
