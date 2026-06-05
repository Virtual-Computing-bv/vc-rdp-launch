# VcRdpLaunch — RD Gateway voor de Windows App-engine

Een mini-launcher die `.rdp`/`.rdpw`-bestanden opent met de **msrdc-engine uit het
Windows App (Windows365) package**, mét RD Gateway-support — iets wat de Windows
App-GUI zelf niet biedt.

## Download

| Bestand | |
|---|---|
| [**vc-rdp-setup.exe**](https://github.com/Virtual-Computing-bv/vc-rdp-launch/releases/latest/download/vc-rdp-setup.exe) | De installer — dit ene bestand is alles wat je nodig hebt (run als admin, of `/silent`). |
| [vc-rdp-setup.intunewin](https://github.com/Virtual-Computing-bv/vc-rdp-launch/releases/latest/download/vc-rdp-setup.intunewin) | Hetzelfde, verpakt als Intune Win32-app (instellingen: `intune/INTUNE.md`). |

Alle versies: [Releases](https://github.com/Virtual-Computing-bv/vc-rdp-launch/releases).

## Het probleem
- De **Windows App** (RDP-client voor klanten) heeft **geen gateway-veld** in de GUI.
- De interne `*.model`-pipeline **negeert** geïnjecteerde gateway-keys (bewezen: de
  gegenereerde launch-`.rdp` blijft `gatewayhostname:s:` leeg; het verkeer gaat
  direct naar `:3389`, niet via de gateway).
- De onderliggende **msrdc-engine kán gateway wel** — maar mag niet *in-place*
  vanuit `C:\Program Files\WindowsApps\...` door een gebruikersproces gestart
  worden (ACL → "Toegang geweigerd"). *Kopiëren* uit het package mag wél, en de
  gekopieerde engine verbindt netjes via de gateway.

## De oplossing (bewezen end-to-end)
```
.rdp/.rdpw ──assoc──▶ vc-rdp-launch.exe "%1"
                         │ 1. engine gestaged & actueel?  (ProgramData → LocalAppData)
                         │ 2. zo nee: package-pad uit register, kopieer msrdc → stage
                         ▼
   ...\VirtualComputing\RdpEngine\msrdc\msrdc.exe  "%1"
                         ▼
              gateway.example.com:443  ✅   (géén directe :3389)
```
- **Geen MsRdpEx-fork.** MsRdpEx (Devolutions, MIT) gebruikt fragiele API-hooking
  die door MSRDC-updates breekt; de maker zelf is ermee gestopt en standalone
  MSRDC is sinds **27-03-2026 end-of-support**. Wij hebben de hooking niet nodig —
  gateway was nooit een engine-beperking, alleen de GUI verbergt het veld.
- **Update-proof.** Het package-pad wordt per start uit de user-leesbare key
  `HKLM\SOFTWARE\Classes\Local Settings\...\AppModel\PackageRepository\Packages\<full>\Path`
  gelezen; een versie-marker zorgt dat de engine na een Windows App-update
  automatisch herstaget.

## Onderdelen
**Op de client draait géén PowerShell** — installatie en launcher zijn beide
native exe's. De `.ps1`/`.cmd` hieronder zijn puur dev-side (compileren) en
worden nooit bij de klant uitgevoerd.

| Pad | Wat |
|---|---|
| `src/VcRdpLaunch.cs` → `build/vc-rdp-launch.exe` | De `.rdp`/`.rdpw`-handler (self-healing engine-stage). |
| `src/VcRdpSetup.cs` + `src/setup.manifest` → `build/vc-rdp-setup.exe` | **De installer.** Eén bestand: bevat de launcher als embedded resource, requireAdministrator-manifest. |
| `build/build.ps1` / `build/build.cmd` | Compileren met in-box csc (geen SDK). Dev-side. Signen = productievereiste. |
| `deploy/DefaultAssociations.xml` | Intune Settings-Catalog profiel om `.rdp`/`.rdpw` *als default* per gebruiker te forceren (spiegelt Markteffect `bb5b7768`). |

## De installer (`vc-rdp-setup.exe`)
Eén gesignd bestand dat je aan de klant geeft. Doet alles native (geen PowerShell):
launcher → `C:\Program Files\VirtualComputing\VcRdpLaunch\`, engine machine-breed
stagen → `C:\ProgramData\VirtualComputing\RdpEngine` (+ icacls Users:RX),
`.rdp`/`.rdpw`-associatie (HKLM), Apps-en-onderdelen vermelding, en optioneel het
werkplek-icoon op het gedeelde bureaublad.

```
vc-rdp-setup.exe                         interactief (UAC + bevestiging)
vc-rdp-setup.exe /silent                 stil (Intune/SYSTEM)
vc-rdp-setup.exe /silent /host:rds01.example.com ^
   /gateway:gateway.example.com /name:"Werkplek"               + werkplek-icoon
vc-rdp-setup.exe /uninstall [/silent]    de-install (incl. icoon + stage)
```

## Deploy (Intune)
1. **Win32-app**: `vc-rdp-setup.exe` (gesigned). Install = `vc-rdp-setup.exe /silent [/host:.. /gateway:.. /name:..]`, uninstall = `vc-rdp-setup.exe /uninstall /silent`, detectie = ARP-key `...\Uninstall\VcRdpLaunch`.
2. **Settings-Catalog profiel** uit `DefaultAssociations.xml` → All Devices (zet de default-association authoritatief; omzeilt UserChoice).

Verifiëren op device: `assoc .rdpw` / `ftype VcRdpLaunch` ; logs in
`%LOCALAPPDATA%\VirtualComputing\vc-rdp-launch.log` en
`C:\ProgramData\VirtualComputing\Logs\setup.log`.

## Caveats
- **Code-signing (verplicht voor productie)**: ongesignd blokkeert Defender/
  SmartScreen de exe op reputatie (geen virus — geen reputatie). Tekenen lost het
  op: `build\build.ps1 -SignThumbprint <thumbprint>`. Cert-keuze + Azure Trusted
  Signing: zie [SIGNING.md](SIGNING.md).
- **MSIX-identity**: de gestagede engine draait buiten z'n package-identity. Pure
  graphics/multimon/codec werken (getest). **Teams-media-optimalisatie op VDI** kan
  degraderen — apart valideren als klanten dat binnen de sessie nodig hebben.
- **`.rdpw`** is Devolutions' eigen extensie; voor de msrdc-engine is de inhoud
  gewoon RDP-tekst. De associatie dekt beide extensies.

## Status
Launcher + keten **end-to-end bewezen** (05-06-2026) in een interne testopstelling
(verbinding aantoonbaar via de RD Gateway op :443, geen directe :3389).
Deploy-kit klaar; openstaand: signing-cert +
Intune-tenant/scope (VC-breed vs per klant).
