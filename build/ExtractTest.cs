// Testhulpje: laadt vc-rdp-setup.exe als assembly en schrijft de embedded
// launcher-resource weg — exact de extractie-code uit de installer.
using System;using System.IO;using System.Reflection;
class ExtractTest{
  static int Main(string[] a){
    var asm = Assembly.LoadFile(a[0]);
    Console.WriteLine("resources: " + string.Join(",", asm.GetManifestResourceNames()));
    using(var rs = asm.GetManifestResourceStream("vc-rdp-launch.exe")){
      if(rs==null){ Console.WriteLine("MISSING"); return 2; }
      using(var fs = File.Create(a[1])) rs.CopyTo(fs);
    }
    Console.WriteLine("extracted -> " + a[1]);
    return 0;
  }
}
