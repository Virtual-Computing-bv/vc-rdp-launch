// VcRdpLaunch — self-healing launcher die .rdp/.rdpw opent met de msrdc-engine
// uit het Windows App (Windows365) MSIX-package, mét RD Gateway-support.
//
// Achtergrond: de Windows App-GUI biedt geen gateway-veld en de model->rdp
// pipeline negeert geinjecteerde gateway-keys. De onderliggende msrdc-engine
// kan gateway wel — maar mag niet in-place vanuit WindowsApps draaien (ACL).
// Daarom: engine eenmalig per versie naar een uitvoerbare locatie stagen en
// daar starten. SYSTEM (Intune) staget naar ProgramData; als dat ontbreekt of
// verouderd is, staget deze launcher zelf naar LocalAppData (self-healing).
//
// Bouw: csc /target:winexe /out:vc-rdp-launch.exe VcRdpLaunch.cs (+ refs in build.ps1)

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class VcRdpLaunch
{
    private const string PackagePrefix = "MicrosoftCorporationII.Windows365_";
    private const string PackageFamily = "8wekyb3d8bbwe"; // publisher hash
    private const string RepoKey =
        @"SOFTWARE\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\PackageRepository\Packages";

    private static string _logPath;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            string localBase = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VirtualComputing");
            Directory.CreateDirectory(localBase);
            _logPath = Path.Combine(localBase, "vc-rdp-launch.log");

            if (args.Length < 1 || string.IsNullOrWhiteSpace(args[0]))
            {
                Fail("Geen .rdp-bestand meegegeven. Gebruik: vc-rdp-launch.exe <bestand.rdp>");
                return 2;
            }

            string rdpPath = args[0].Trim('"');
            if (!File.Exists(rdpPath))
            {
                Fail("RDP-bestand niet gevonden:\n" + rdpPath);
                return 3;
            }

            Log("Start. rdp=" + rdpPath);

            string engine = ResolveEngine(localBase);
            if (engine == null)
            {
                Fail("Kon de msrdc-engine niet vinden of stagen.\n" +
                     "Is de Windows App (Windows365) geinstalleerd?\nZie log: " + _logPath);
                return 4;
            }

            Log("Engine: " + engine);

            var psi = new ProcessStartInfo
            {
                FileName = engine,
                Arguments = "\"" + rdpPath + "\"",
                UseShellExecute = false
            };
            Process.Start(psi);
            Log("msrdc gestart.");
            return 0;
        }
        catch (Exception ex)
        {
            Fail("Onverwachte fout:\n" + ex.Message);
            return 1;
        }
    }

    // Kies de engine: 1) verse ProgramData-stage (SYSTEM), 2) verse LocalAppData-stage,
    // anders zelf stagen vanuit het package naar LocalAppData.
    private static string ResolveEngine(string localBase)
    {
        string pkgRoot = FindPackageRoot();           // ...\WindowsApps\...Windows365_<ver>_x64_...
        string currentTag = pkgRoot == null ? null : Path.GetFileName(pkgRoot);
        Log("Package root: " + (pkgRoot ?? "(niet gevonden)"));

        string programData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "VirtualComputing", "RdpEngine");
        string pdEngine = StagedEngineIfCurrent(programData, currentTag);
        if (pdEngine != null) { Log("Gebruik ProgramData-stage."); return pdEngine; }

        string localStage = Path.Combine(localBase, "RdpEngine");
        string lsEngine = StagedEngineIfCurrent(localStage, currentTag);
        if (lsEngine != null) { Log("Gebruik LocalAppData-stage."); return lsEngine; }

        // Niets verse staging beschikbaar -> zelf stagen vanuit package.
        if (pkgRoot == null) return null;
        string srcMsrdc = Path.Combine(pkgRoot, "msrdc");
        if (!Directory.Exists(srcMsrdc)) { Log("msrdc-map ontbreekt in package."); return null; }

        Log("Self-heal: stage naar LocalAppData...");
        string dstMsrdc = Path.Combine(localStage, "msrdc");
        try
        {
            if (Directory.Exists(localStage)) Directory.Delete(localStage, true);
            CopyDir(srcMsrdc, dstMsrdc);
            File.WriteAllText(Path.Combine(localStage, ".engine-version"), currentTag);
        }
        catch (Exception ex) { Log("Stage-fout: " + ex.Message); return null; }

        string staged = Path.Combine(dstMsrdc, "msrdc.exe");
        return File.Exists(staged) ? staged : null;
    }

    // Geeft het engine-pad terug als de stage bestaat en bij de huidige package-versie hoort.
    private static string StagedEngineIfCurrent(string stageDir, string currentTag)
    {
        try
        {
            string exe = Path.Combine(stageDir, "msrdc", "msrdc.exe");
            if (!File.Exists(exe)) return null;
            if (currentTag == null) return exe; // package onvindbaar: gebruik wat er staat
            string marker = Path.Combine(stageDir, ".engine-version");
            string tag = File.Exists(marker) ? File.ReadAllText(marker).Trim() : null;
            return string.Equals(tag, currentTag, StringComparison.OrdinalIgnoreCase) ? exe : null;
        }
        catch { return null; }
    }

    // Hoogste Windows365-versie uit de user-leesbare PackageRepository-key.
    private static string FindPackageRoot()
    {
        try
        {
            using (var key = Registry.LocalMachine.OpenSubKey(RepoKey))
            {
                if (key == null) { Log("RepoKey niet leesbaar."); return null; }
                string bestPath = null;
                Version best = null;
                foreach (string sub in key.GetSubKeyNames())
                {
                    if (sub.IndexOf(PackagePrefix, StringComparison.OrdinalIgnoreCase) != 0) continue;
                    if (sub.IndexOf(PackageFamily, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    Version v = ParseVersion(sub);
                    using (var sk = key.OpenSubKey(sub))
                    {
                        string path = sk == null ? null : sk.GetValue("Path") as string;
                        if (string.IsNullOrEmpty(path)) continue;
                        if (best == null || (v != null && v > best)) { best = v; bestPath = path; }
                    }
                }
                return bestPath;
            }
        }
        catch (Exception ex) { Log("FindPackageRoot-fout: " + ex.Message); return null; }
    }

    // "MicrosoftCorporationII.Windows365_2.0.1186.0_x64__8wekyb..." -> 2.0.1186.0
    private static Version ParseVersion(string fullName)
    {
        try
        {
            string rest = fullName.Substring(PackagePrefix.Length);
            int us = rest.IndexOf('_');
            string ver = us > 0 ? rest.Substring(0, us) : rest;
            Version v; return Version.TryParse(ver, out v) ? v : null;
        }
        catch { return null; }
    }

    private static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (string f in Directory.GetFiles(src))
            File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
        foreach (string d in Directory.GetDirectories(src))
            CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }

    private static void Log(string msg)
    {
        try
        {
            if (_logPath == null) return;
            File.AppendAllText(_logPath,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine,
                Encoding.UTF8);
        }
        catch { }
    }

    private static void Fail(string msg)
    {
        Log("FAIL: " + msg.Replace(Environment.NewLine, " | "));
        try { MessageBox.Show(msg, "VC RDP Launch", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        catch { }
    }
}
