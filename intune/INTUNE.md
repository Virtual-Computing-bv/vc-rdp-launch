# Intune-deploy — VC Remote Desktop (Win32-app)

Pakket: **`vc-rdp-setup.intunewin`** (in deze map). Gemaakt met Microsofts
Win32 Content Prep Tool (`IntuneWinAppUtil.exe -c source -s vc-rdp-setup.exe -o . -q`).

## App toevoegen
Intune → **Apps → Windows → Toevoegen → App-type: Windows-app (Win32)** →
selecteer `vc-rdp-setup.intunewin`.

### Programma
| Veld | Waarde |
|---|---|
| **Installeeropdracht** | `vc-rdp-setup.exe /silent` |
| Installeeropdracht (+ werkplek-icoon) | `vc-rdp-setup.exe /silent /host:rds01.example.com /gateway:gateway.example.com /name:"Werkplek"` |
| **Verwijderopdracht** | `vc-rdp-setup.exe /uninstall /silent` |
| **Installatiegedrag** | **Systeem** |
| Apparaatherstartgedrag | Geen specifieke actie |
| Retourcodes | Standaard (0 = succes). De installer geeft 0 ok / 1603 fout. |

### Vereisten
- Besturingssysteemarchitectuur: **x64**
- Minimaal besturingssysteem: **Windows 10 1809** of hoger

### Detectieregel (handmatig → Register)
| Veld | Waarde |
|---|---|
| Regeltype | Register |
| Sleutelpad | `HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\VcRdpLaunch` |
| Waarde | `DisplayVersion` |
| Detectiemethode | Tekenreeksvergelijking — **gelijk aan** `1.0.0` |

(Alternatief: "Sleutel/waarde bestaat" op dezelfde sleutel.)

## Default-associatie forceren (aanrader)
Win32-install zet de associatie machine-wide, maar Windows' **UserChoice** kan de
default per gebruiker overrulen. Forceer 'm met het profiel:
Intune → **Apparaten → Configuratie → Settings Catalog** →
*Configure Default Apps* (DefaultAssociationsConfiguration) → plak de inhoud van
`..\deploy\DefaultAssociations.xml` → toewijzen aan **All Devices**.
(Spiegelt het Markteffect-profiel `bb5b7768`.)

## Signing (productie!)
De `.intunewin` bevat nu de **ongesignde** exe. Op WDAC-locked werkplekken moet
de binary gesigned + allowlisted zijn, anders weigert hij te starten.
Werkwijze: teken `vc-rdp-launch.exe` → embed → teken `vc-rdp-setup.exe` → **opnieuw
verpakken** met IntuneWinAppUtil. Zie `..\build\build.ps1` voor de signtool-stap.

## Opnieuw verpakken (na codewijziging of signing)
```
build\build.ps1
intune\IntuneWinAppUtil.exe -c intune\source -s vc-rdp-setup.exe -o intune -q
```
(Kopieer eerst de nieuwe `build\vc-rdp-setup.exe` naar `intune\source\`.)
