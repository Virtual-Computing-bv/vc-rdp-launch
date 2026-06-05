# Code-signing — waarom en hoe

Defender/SmartScreen blokkeert `vc-rdp-setup.exe` zolang het **ongesignd** is:
een verse, onbekende exe met installer-gedrag heeft geen reputatie. Dit is geen
virusdetectie — het is reputatie. **De fix is code-signing.** Geen obfuscatie of
Defender-uitzetten; dat is precies wat je bij je eigen security-stack niet wilt.

## Cert-keuze (VC heeft er nog geen)

| Optie | SmartScreen | Kosten/gemak | Advies |
|---|---|---|---|
| **Azure Trusted Signing** | Goede reputatie, chaint naar Microsoft-beheerde CA | ~$10/mnd, geen hardware-token, signtool-integratie | **Aanrader** — modern, goedkoop, past bij VC-op-Azure |
| **EV code-signing cert** | **Directe** reputatie (geen waarschuwing, ook bij verse download) | Duurder + hardware-token/HSM | Beste als je veel los aan klanten distribueert |
| **OV / standaard cert** | Reputatie bouwt op over downloads/tijd (kan eerst nog waarschuwen) | Goedkoopst | Minimum; genoeg voor *allow-by-publisher* in Defender |

Voor een Defender-**beheerde** fleet (zoals VC) volstaat ook OV: na signen kun je in
Defender for Endpoint *allow-by-publisher* zetten, dan vertrouwen je eigen devices
het direct — zónder per-build hash-indicator.

## Signen (zodra het cert er is)

Cert in de Windows cert-store? Dan in één commando, launcher + setup:
```powershell
build\build.ps1 -SignThumbprint <SHA1-thumbprint van het cert>
```
(Het script tekent de launcher vóór het embedden en de setup erna. signtool wordt
automatisch onder de Windows SDK gevonden; installeer anders de Windows 10/11 SDK.)

### Azure Trusted Signing
Gebruikt geen lokale thumbprint maar de Trusted Signing dlib + een `metadata.json`:
```
signtool sign /v /debug /fd SHA256 /tr http://timestamp.acs.microsoft.com /td SHA256 ^
  /dlib <pad>\Azure.CodeSigning.Dlib.dll /dmdf <pad>\metadata.json vc-rdp-setup.exe
```
Teken in dezelfde volgorde (launcher → embed → setup) of bouw eerst ongesignd en
teken daarna beide losse exe's + verpak opnieuw (zie hieronder).

## Daarna: opnieuw verpakken + release
```
build\build.ps1 -SignThumbprint <thumbprint>
copy build\vc-rdp-setup.exe intune\source\
intune\IntuneWinAppUtil.exe -c intune\source -s vc-rdp-setup.exe -o intune -q
gh release create v1.0.1 build\vc-rdp-setup.exe build\vc-rdp-launch.exe intune\vc-rdp-setup.intunewin --title "v1.0.1 — gesigned"
```

## Verifiëren
```powershell
Get-AuthenticodeSignature build\vc-rdp-setup.exe   # Status moet 'Valid' zijn
```
