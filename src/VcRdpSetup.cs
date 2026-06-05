// VcRdpSetup — zelfstandige installer voor VcRdpLaunch.
// Eén gesignd .exe dat je aan klanten geeft. Bevat de launcher als embedded
// resource, staget de msrdc-engine machine-breed, registreert .rdp/.rdpw en
// schrijft een Apps-en-onderdelen (uninstall) vermelding. Geen PowerShell nodig.
//
// Modi:
//   (geen args)         interactieve install (UAC + bevestiging + resultaat)
//   /install [/silent]  install (silent = geen dialogen, voor Intune/SYSTEM)
//   /uninstall [/silent] de-install
//
// Bouw: zie build/build.ps1 (embed launcher via /resource, manifest via /win32manifest).

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class VcRdpSetup
{
    private const string ProductVersion = "1.0.0";
    private const string ProgId     = "VcRdpLaunch";
    private const string LauncherEx = "vc-rdp-launch.exe";
    private const string SetupExe   = "vc-rdp-setup.exe";
    private const string Publisher  = "Virtual Computing";
    private const string DisplayNm  = "VC Remote Desktop (gateway-launcher)";

    private const string PackagePrefix = "MicrosoftCorporationII.Windows365_";
    private const string PackageFamily  = "8wekyb3d8bbwe";
    private const string RepoKey =
        @"SOFTWARE\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\PackageRepository\Packages";
    private const string ArpKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\VcRdpLaunch";

    private static bool _silent;
    private static string _log;
    private static string _host;     // optioneel: zet werkplek-icoon neer
    private static string _gateway;
    private static string _iconName = "Werkplek";

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr a, IntPtr b);

    [STAThread]
    private static int Main(string[] args)
    {
        bool uninstall = false;
        foreach (string a in args)
        {
            string raw = a.TrimStart('/', '-');
            string s = raw.ToLowerInvariant();
            int colon = raw.IndexOf(':');
            if (s == "silent" || s == "quiet" || s == "s" || s == "q") _silent = true;
            else if (s == "uninstall" || s == "remove" || s == "x") uninstall = true;
            else if (colon > 0)
            {
                string k = raw.Substring(0, colon).ToLowerInvariant();
                string v = raw.Substring(colon + 1).Trim('"');
                if (k == "host") _host = v;
                else if (k == "gateway") _gateway = v;
                else if (k == "name") _iconName = v;
            }
        }

        string logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "VirtualComputing", "Logs");
        try { Directory.CreateDirectory(logDir); _log = Path.Combine(logDir, "setup.log"); } catch { }

        try
        {
            if (uninstall) { DoUninstall(); Info("VC Remote Desktop is verwijderd."); return 0; }

            if (!_silent)
            {
                var r = MessageBox.Show(
                    "VC Remote Desktop installeren?\n\n" +
                    "Dit koppelt .rdp/.rdpw-bestanden aan de Windows App-engine\n" +
                    "(msrdc) mét RD Gateway-ondersteuning.",
                    "VC Remote Desktop " + ProductVersion, MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
                if (r != DialogResult.OK) { Log("Door gebruiker geannuleerd."); return 1602; }
            }

            DoInstall();
            Info("VC Remote Desktop is geïnstalleerd.\n.rdp/.rdpw openen nu via de Windows App-engine met gateway.");
            return 0;
        }
        catch (Exception ex)
        {
            Log("FOUT: " + ex);
            if (!_silent) MessageBox.Show("Installatie mislukt:\n" + ex.Message,
                "VC Remote Desktop", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1603; // ERROR_INSTALL_FAILURE
        }
    }

    private static void DoInstall()
    {
        string instDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "VirtualComputing", "VcRdpLaunch");
        Directory.CreateDirectory(instDir);
        string launcher = Path.Combine(instDir, LauncherEx);

        // 1) Launcher uit embedded resource schrijven.
        ExtractResource(LauncherEx, launcher);
        Log("Launcher geplaatst: " + launcher);

        // 2) Zichzelf kopiëren voor uninstall.
        string selfDst = Path.Combine(instDir, SetupExe);
        try { File.Copy(Assembly.GetExecutingAssembly().Location, selfDst, true); } catch (Exception ex) { Log("self-copy: " + ex.Message); }

        // 3) Engine machine-breed stagen (we zijn elevated).
        StageEngine();

        // 4) File-associaties (HKLM\SOFTWARE\Classes).
        RegisterAssociations(launcher);

        // 5) Apps-en-onderdelen vermelding.
        WriteArp(instDir, selfDst);

        // 5b) Optioneel: werkplek-icoon op het bureaublad (alle gebruikers).
        if (!string.IsNullOrEmpty(_host) && !string.IsNullOrEmpty(_gateway))
        {
            string icon = CreateWerkplekIcon();
            if (icon != null)
                using (var k = Registry.LocalMachine.CreateSubKey(ArpKey)) k.SetValue("IconFile", icon);
        }

        // 6) Shell verversen zodat iconen/associaties direct landen.
        try { SHChangeNotify(0x08000000 /*SHCNE_ASSOCCHANGED*/, 0x0000 /*SHCNF_IDLIST*/, IntPtr.Zero, IntPtr.Zero); } catch { }
        Log("Install klaar.");
    }

    private static void DoUninstall()
    {
        // associaties terug
        using (var cls = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes", true))
        {
            if (cls != null)
            {
                TryDeleteTree(cls, ProgId);
                foreach (string ext in new[] { ".rdp", ".rdpw" })
                {
                    using (var ek = cls.OpenSubKey(ext, true))
                    {
                        if (ek == null) continue;
                        using (var ow = ek.OpenSubKey("OpenWithProgids", true)) { if (ow != null) ow.DeleteValue(ProgId, false); }
                        var def = ek.GetValue(null) as string;
                        if (string.Equals(def, ProgId, StringComparison.OrdinalIgnoreCase)) ek.DeleteValue(null, false);
                    }
                }
            }
        }
        // ProgramData-stage
        string stage = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "VirtualComputing", "RdpEngine");
        try { if (Directory.Exists(stage)) Directory.Delete(stage, true); } catch (Exception ex) { Log("stage del: " + ex.Message); }
        // werkplek-icoon (pad uit ARP) + ARP
        try
        {
            using (var k = Registry.LocalMachine.OpenSubKey(ArpKey))
            {
                string icon = k == null ? null : k.GetValue("IconFile") as string;
                if (!string.IsNullOrEmpty(icon) && File.Exists(icon)) File.Delete(icon);
            }
        }
        catch (Exception ex) { Log("icoon del: " + ex.Message); }
        try { Registry.LocalMachine.DeleteSubKeyTree(ArpKey, false); } catch { }
        // Program Files (laat de draaiende exe niet zichzelf slopen: best effort)
        string instDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "VirtualComputing", "VcRdpLaunch");
        try { File.Delete(Path.Combine(instDir, LauncherEx)); } catch { }
        try { SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero); } catch { }
        Log("Uninstall klaar.");
    }

    // Schrijft een gateway-.rdpw op het gedeelde bureaublad. Retourneert het pad.
    private static string CreateWerkplekIcon()
    {
        try
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            string safe = _iconName;
            foreach (char c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
            string path = Path.Combine(desktop, safe + ".rdpw");
            string[] lines = {
                "full address:s:" + _host,
                "gatewayhostname:s:" + _gateway,
                "gatewayusagemethod:i:1",
                "gatewayprofileusagemethod:i:1",
                "gatewaycredentialssource:i:4",
                "promptcredentialonce:i:1",
                "authentication level:i:2",
                "enablerdsaadauth:i:0",
                "networkautodetect:i:1",
                "bandwidthautodetect:i:1",
                "redirectclipboard:i:1",
                "redirectprinters:i:1",
                "audiomode:i:0",
                "screen mode id:i:2",
                "use multimon:i:1",
                "smart sizing:i:1"
            };
            File.WriteAllLines(path, lines, new UTF8Encoding(false));
            Log("Werkplek-icoon: " + path + "  (" + _host + " via " + _gateway + ")");
            return path;
        }
        catch (Exception ex) { Log("icoon-fout: " + ex.Message); return null; }
    }

    // ---- engine staging (zelfde resolve als de launcher) ----
    private static void StageEngine()
    {
        string pkgRoot = FindPackageRoot();
        if (pkgRoot == null) { Log("Windows App niet gevonden; launcher self-healt later naar LocalAppData."); return; }
        string src = Path.Combine(pkgRoot, "msrdc");
        if (!File.Exists(Path.Combine(src, "msrdc.exe"))) { Log("msrdc ontbreekt in package."); return; }

        string baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "VirtualComputing", "RdpEngine");
        string dst = Path.Combine(baseDir, "msrdc");
        string tag = Path.GetFileName(pkgRoot);
        string marker = Path.Combine(baseDir, ".engine-version");

        string current = File.Exists(marker) ? File.ReadAllText(marker).Trim() : "";
        if (current == tag && File.Exists(Path.Combine(dst, "msrdc.exe"))) { Log("Engine al actueel (" + tag + ")."); return; }

        Log("Stage engine " + tag);
        if (Directory.Exists(baseDir)) { try { Directory.Delete(baseDir, true); } catch { } }
        CopyDir(src, dst);
        File.WriteAllText(marker, tag);

        // Users: lezen+uitvoeren; schrijven alleen admin/SYSTEM.
        RunIcacls(baseDir);
    }

    private static void RunIcacls(string dir)
    {
        try
        {
            var psi = new ProcessStartInfo("icacls.exe",
                "\"" + dir + "\" /inheritance:r " +
                "/grant:r \"*S-1-5-32-545:(OI)(CI)RX\" \"*S-1-5-32-544:(OI)(CI)F\" \"*S-1-5-18:(OI)(CI)F\" /T /C")
            { UseShellExecute = false, CreateNoWindow = true };
            var p = Process.Start(psi); p.WaitForExit(30000);
            Log("icacls exit " + p.ExitCode);
        }
        catch (Exception ex) { Log("icacls: " + ex.Message); }
    }

    private static void RegisterAssociations(string launcher)
    {
        string cmd = "\"" + launcher + "\" \"%1\"";
        using (var cls = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Classes"))
        {
            using (var p = cls.CreateSubKey(ProgId))
            {
                p.SetValue(null, "Remote Desktop (VC)");
                // Klassiek rood RDP-icoon (crisp, multi-size) uit mstsc.exe.
                using (var di = p.CreateSubKey("DefaultIcon"))
                    di.SetValue(null, @"%SystemRoot%\System32\mstsc.exe,0", RegistryValueKind.ExpandString);
                using (var c = p.CreateSubKey(@"shell\open\command")) c.SetValue(null, cmd);
            }
            foreach (string ext in new[] { ".rdp", ".rdpw" })
            {
                using (var e = cls.CreateSubKey(ext))
                {
                    e.SetValue(null, ProgId);
                    using (var ow = e.CreateSubKey("OpenWithProgids")) ow.SetValue(ProgId, new byte[0], RegistryValueKind.None);
                }
            }
        }
        Log("Associaties gezet: .rdp/.rdpw -> " + cmd);
    }

    private static void WriteArp(string instDir, string uninstaller)
    {
        using (var k = Registry.LocalMachine.CreateSubKey(ArpKey))
        {
            k.SetValue("DisplayName", DisplayNm);
            k.SetValue("DisplayVersion", ProductVersion);
            k.SetValue("Publisher", Publisher);
            k.SetValue("InstallLocation", instDir);
            k.SetValue("DisplayIcon", Path.Combine(instDir, LauncherEx) + ",0");
            k.SetValue("UninstallString", "\"" + uninstaller + "\" /uninstall");
            k.SetValue("QuietUninstallString", "\"" + uninstaller + "\" /uninstall /silent");
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }
        Log("ARP-vermelding geschreven.");
    }

    // ---- helpers (gedeeld met launcher) ----
    private static string FindPackageRoot()
    {
        try
        {
            using (var key = Registry.LocalMachine.OpenSubKey(RepoKey))
            {
                if (key == null) return null;
                string bestPath = null; Version best = null;
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
        catch { return null; }
    }

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
        foreach (string f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
        foreach (string d in Directory.GetDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }

    private static void ExtractResource(string name, string dstPath)
    {
        var asm = Assembly.GetExecutingAssembly();
        using (Stream rs = asm.GetManifestResourceStream(name))
        {
            if (rs == null) throw new Exception("Embedded resource ontbreekt: " + name);
            using (var fs = new FileStream(dstPath, FileMode.Create, FileAccess.Write))
                rs.CopyTo(fs);
        }
    }

    private static void TryDeleteTree(RegistryKey parent, string sub)
    {
        try { parent.DeleteSubKeyTree(sub, false); } catch { }
    }

    private static void Info(string msg) { Log(msg.Replace(Environment.NewLine, " | ")); if (!_silent) MessageBox.Show(msg, "VC Remote Desktop", MessageBoxButtons.OK, MessageBoxIcon.Information); }

    private static void Log(string m)
    {
        try { if (_log != null) File.AppendAllText(_log, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + m + Environment.NewLine, Encoding.UTF8); }
        catch { }
    }
}
