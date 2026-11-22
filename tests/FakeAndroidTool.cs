using System;
using System.IO;
using System.Linq;

// Protocol fixture only: no virtual machine and no network access.
class FakeAndroidTool
{
 static int Main(string[] args)
 {
  var tool = Path.GetFileNameWithoutExtension(Environment.GetCommandLineArgs()[0]);
  var sdk = Environment.GetEnvironmentVariable("ANDROID_HOME");
  var avds = Environment.GetEnvironmentVariable("ANDROID_AVD_HOME");
  File.AppendAllText(Path.Combine(sdk,"calls.txt"),tool+" "+string.Join(" | ",args)+"\n");
  if(tool=="emulator")
  {
   if(args.Contains("-list-avds")) foreach(var dir in Directory.GetDirectories(avds,"*.avd")) Console.WriteLine(Path.GetFileNameWithoutExtension(dir));
   if(args.Contains("-accel-check")) Console.WriteLine("WHPX is installed and usable.");
   if(args.Contains("-avd")) System.Threading.Thread.Sleep(6000);
   return 0;
  }
  if(tool=="adb")
  {
   if(args.Contains("devices")) Console.WriteLine("List of devices attached\nemulator-5554\tdevice\n");
   else if(args.Contains("name")) Console.WriteLine("Test_Device\nOK");
   else Console.WriteLine("Success");
   return 0;
  }
  if(args.Length>0 && args[0]=="avdmanager")
  {
   var name=args[Array.IndexOf(args,"-n")+1]; var image=args[Array.IndexOf(args,"-k")+1];
   Directory.CreateDirectory(Path.Combine(avds,name+".avd"));
   File.WriteAllText(Path.Combine(avds,name+".avd","config.ini"),"image.sysdir.1="+image.Replace(';','\\')+"\nhw.ramSize=512\n");
   File.WriteAllText(Path.Combine(avds,name+".ini"),"path="+Path.Combine(avds,name+".avd")+"\n");
   Console.WriteLine("Created AVD"); return 0;
  }
  if(args.Any(x=>x.StartsWith("--list"))) Console.WriteLine("Installed packages:\n system-images;android-36;google_apis;x86_64 | 1 | Test image\nAvailable Packages:\n system-images;android-37;google_apis;x86_64 | 1 | New image");
  else if(args.Contains("invalid-package")) { Console.Error.WriteLine("Package not found"); return 7; }
  else Console.WriteLine("Installed successfully");
  return 0;
 }
}
