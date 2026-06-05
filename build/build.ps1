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
    # Klassiek cert in de cert-store:
    [string]$SignThumbprint,
    [string]$TimestampUrl = "http://timestamp.digicert.com",
    # Azure Artifact Signing (Trusted Signing): pad naar dlib + metadata.json
    [string]$SignDlib,
    [string]$SignMetadata,
    [string]$AcsTimestampUrl = "http://timestamp.acs.microsoft.com"
)
$ErrorActionPreference = 'Stop'
$root   = Split-Path $PSScriptRoot -Parent
$src    = Join-Path $root 'src'
$outDir = $PSScriptRoot
$launch = Join-Path $outDir 'vc-rdp-launch.exe'
$setup  = Join-Path $outDir 'vc-rdp-setup.exe'

if (-not (Test-Path $Csc)) { throw "csc niet gevonden: $Csc" }

function Get-SignTool {
    # Artifact Signing client tools leveren een eigen signtool; anders de Windows SDK.
    foreach ($base in @("$env:LOCALAPPDATA\Microsoft\WinGet\Packages", (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'))) {
        $st = Get-ChildItem $base -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
              Where-Object { $_.FullName -match '\\x64\\' } | Select-Object -Last 1
        if ($st) { return $st.FullName }
    }
    throw "signtool.exe niet gevonden (installeer de Windows SDK of de Artifact Signing client tools)"
}

function Invoke-Sign([string]$file) {
    if ($SignDlib -and $SignMetadata) {
        # Azure Artifact Signing (Trusted Signing)
        & (Get-SignTool) sign /v /fd SHA256 /tr $AcsTimestampUrl /td SHA256 /dlib $SignDlib /dmdf $SignMetadata $file
        if ($LASTEXITCODE -ne 0) { throw "Artifact Signing faalde voor $file" }
        Write-Host "  getekend (Artifact Signing): $file"
    }
    elseif ($SignThumbprint) {
        # Klassiek cert in de cert-store
        & (Get-SignTool) sign /sha1 $SignThumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 $file
        if ($LASTEXITCODE -ne 0) { throw "signtool faalde voor $file" }
        Write-Host "  getekend: $file"
    }
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

if (-not $SignThumbprint -and -not ($SignDlib -and $SignMetadata)) {
    Write-Host "LET OP: ongesignd gebouwd. Productie (Artifact Signing): .\build.ps1 -SignDlib <dll> -SignMetadata <metadata.json>  (zie SIGNING.md)"
}
Write-Host "Klaar. Geef vc-rdp-setup.exe aan de klant (run als admin / Intune /silent)."
