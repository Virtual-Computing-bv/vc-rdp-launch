# Code-signing met Azure Artifact Signing (Trusted Signing)

Defender/SmartScreen blokkeert `vc-rdp-setup.exe` zolang het **ongesignd** is:
een verse, onbekende exe heeft geen reputatie (geen virus — geen reputatie). Op een
Defender-**beheerde** fleet doet meestal de ASR-regel *"block executable unless
prevalence/age/trusted"* dit. **De fix is code-signing.** Geen obfuscatie of
Defender uitzetten.

> **Naam:** "Trusted Signing" heet nu **Azure Artifact Signing** (zelfde dienst).
> Sommige tooling heet nog "trusted-signing" (winget client-tools id
> `Microsoft.Azure.ArtifactSigningClientTools`, GitHub Action `azure/trusted-signing-action`).

## Verwachting kloppend zetten
- Artifact Signing geeft **géén directe** consumenten-SmartScreen-trust; reputatie
  bouwt op over weken + honderden schone installs (zoals OV). Alleen een **EV-cert**
  geeft directe SmartScreen-trust.
- **Voor VC's beheerde fleet is dat irrelevant:** na signen heb je een gevalideerde
  uitgevers-identiteit → zet in Defender for Endpoint **allow-by-publisher** (indicator
  op het certificaat) en je eigen devices vertrouwen **elke** build direct. Dat lost
  het blok op. ~$10/mnd i.p.v. een dure EV-token.

## Eligibility & kosten
- **Public Trust** is beschikbaar voor organisaties in **USA, Canada, EU, UK**.
  VC = NL = EU → **komt in aanmerking**.
- Kosten: Basic-tier ~**$10/mnd** incl. maandelijkse handtekening-quota
  (verifieer op <https://azure.microsoft.com/pricing/details/artifact-signing/>).
- CN/O staan **vast** op de gevalideerde legal entity (geen custom CN/O).

## Setup (eenmalig) — in de VC Azure-tenant
1. Azure-subscription + Entra-tenant (heeft VC).
2. Registreer de resource provider **`Microsoft.CodeSigning`**.
3. Maak een **Artifact Signing-account** in een EU-regio (bijv. West/North Europe).
   Onthoud de regio — het signing-**endpoint moet die regio matchen** (anders 403).
4. Ken jezelf de rol **Artifact Signing Identity Verifier** toe (anders is de
   "New identity validation"-knop grijs).
5. **Identity validation (ALLEEN in de portal, 1–20 werkdagen):**
   Objects → **Identity validations** → **Organization** → **Public**. Nodig:
   legal entity-naam (zoals KvK), website, **primair + secundair e-mail** (secundair
   domein moet matchen), business identifier (KvK-nummer), bedrijfsadres, en een
   **persoon** die zich met gov-ID valideert (exacte naam zoals op ID). Houd publieke
   records actueel voor snellere goedkeuring.
6. Maak een **certificate profile** type **Public Trust** met de `identity-validation-id`.
7. Ken de rol **Trusted Signing Certificate Profile Signer** toe aan wie tekent
   (jij, of een service principal voor CI).

## Tekenen (zodra account + profiel er zijn)
Installeer de client tools (dlib + compatibele signtool + .NET 8):
```powershell
winget install -e --id Microsoft.Azure.ArtifactSigningClientTools
```
Maak `metadata.json` (endpoint-regio = account-regio!):
```json
{
  "Endpoint": "https://weu.codesigning.azure.net/",
  "CodeSigningAccountName": "<account-naam>",
  "CertificateProfileName": "<profiel-naam>"
}
```
Bouw + teken in één keer (launcher vóór embed, setup erna — regelt build.ps1):
```powershell
build\build.ps1 -SignDlib "<pad>\Azure.CodeSigning.Dlib.dll" -SignMetadata "<pad>\metadata.json"
```

## Daarna: opnieuw verpakken + release
```
copy build\vc-rdp-setup.exe intune\source\
intune\IntuneWinAppUtil.exe -c intune\source -s vc-rdp-setup.exe -o intune -q
gh release create v1.0.1 build\vc-rdp-setup.exe build\vc-rdp-launch.exe intune\vc-rdp-setup.intunewin --title "v1.0.1 — gesigned"
```

## Defender allow-by-publisher (beheerde fleet → directe trust)
Na de eerste gesignde build: in Defender for Endpoint een **allow-indicator op het
certificaat** (uitgever) zetten. Dan vertrouwt de fleet elke volgende build zonder
per-build hash. (Kan via de Defender-MCP zodra de uitgevers-CN bekend is.)

## Verifiëren
```powershell
Get-AuthenticodeSignature build\vc-rdp-setup.exe   # Status moet 'Valid' zijn
```
