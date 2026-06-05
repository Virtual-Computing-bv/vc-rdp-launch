// Build-helper: haalt het rode RDP-icoon uit mstsc.exe en schrijft src\red.ico,
// zodat we het via /win32icon in de launcher en de setup kunnen bakken.
using System;using System.Drawing;using System.IO;
class MakeIcon{
  static int Main(string[] a){
    string mstsc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "mstsc.exe");
    using (Icon ic = Icon.ExtractAssociatedIcon(mstsc))
    using (FileStream fs = File.Create(a[0])) ic.Save(fs);
    Console.WriteLine("red.ico -> " + a[0]);
    return 0;
  }
}
