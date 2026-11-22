using System;
using System.IO;
using System.IO.Compression;
using Aemu;
using Auremu;
public static class ReleaseCheck {
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 public static void Main(string[] args){
  string root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);
  Environment.SetEnvironmentVariable("AUREMU_DATA_DIR",Path.Combine(root,"isolated"));
  var engine=new Engine();Check(engine.Root==Path.Combine(root,"isolated"),"Explicit data root ignored");
  Check(engine.Settings.StockName=="Auremu_Android_17"&&engine.Settings.UseStockAndroid,"Public defaults reference old personal device");
  Check(!File.Exists(Path.Combine(engine.Root,"settings.json")),"Settings were pre-seeded");
  var runtime=new Runtime();Check(AndroidSetup.Needed(runtime),"Missing SDK must trigger onboarding");
  Check(AndroidSetup.ToolsSha256.Length==64,"SDK SHA-256 is malformed");
  var zip=Path.Combine(root,"unsafe.zip");using(var a=ZipFile.Open(zip,ZipArchiveMode.Create)){var e=a.CreateEntry("../escape.txt");using(var w=new StreamWriter(e.Open()))w.Write("escape");}
  bool rejected=false;try{AndroidSetup.Extract(zip,Path.Combine(root,"extract"));}catch(IOException){rejected=true;}
  Check(rejected&&!File.Exists(Path.Combine(root,"escape.txt")),"SDK extractor permits path traversal");
  Console.WriteLine("PASS: public profile defaults, isolated data root, first-run detection, checksum format, ZIP traversal rejection.");
 }
}
