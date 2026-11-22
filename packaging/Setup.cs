using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace AuremuInstaller {
 public static class Setup {
  const string RegistryPath="Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Auremu";
  static string Target {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","Auremu");}}
  static string Shortcut {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"Auremu.lnk");}}
  static void Extract(string destination){
   string root=Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
   Directory.CreateDirectory(root);
   using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Payload.zip"))
   using(var archive=new ZipArchive(stream,ZipArchiveMode.Read))foreach(var entry in archive.Entries){
    string path=Path.GetFullPath(Path.Combine(root,entry.FullName));
    if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new IOException("Unsafe archive entry.");
    if(entry.Name.Length==0){Directory.CreateDirectory(path);continue;}
    Directory.CreateDirectory(Path.GetDirectoryName(path));using(var input=entry.Open())using(var output=File.Create(path))input.CopyTo(output);
   }
  }
  static void Install(){
   // Never accept an arbitrary removal target or follow a directory junction.
   if(Directory.Exists(Target)&&(File.GetAttributes(Target)&FileAttributes.ReparsePoint)!=0)throw new IOException("The install folder must not be a junction.");
   foreach(var p in Process.GetProcessesByName("Auremu")){try{if(p.MainModule.FileName.StartsWith(Target+"\\",StringComparison.OrdinalIgnoreCase))throw new IOException("Close Auremu before installing an update.");}catch(System.ComponentModel.Win32Exception){}}
   Extract(Target);
   string uninstaller=Path.Combine(Target,"bin","Uninstall.exe");File.Copy(Assembly.GetExecutingAssembly().Location,uninstaller,true);
   Type shellType=Type.GetTypeFromProgID("WScript.Shell");dynamic shell=Activator.CreateInstance(shellType);dynamic shortcut=shell.CreateShortcut(Shortcut);
   shortcut.TargetPath=Path.Combine(Target,"bin","Auremu.exe");shortcut.WorkingDirectory=Target;shortcut.Description="Auremu Android emulator";shortcut.Save();
   using(var key=Registry.CurrentUser.CreateSubKey(RegistryPath)){
    key.SetValue("DisplayName","Auremu");key.SetValue("DisplayVersion","0.1.0");key.SetValue("Publisher","unioneon");key.SetValue("InstallLocation",Target);key.SetValue("DisplayIcon",Path.Combine(Target,"bin","Auremu.exe"));
    key.SetValue("UninstallString","\""+uninstaller+"\" --uninstall");key.SetValue("NoModify",1);key.SetValue("NoRepair",1);
   }
  }
  static int Uninstall(){
   string actual=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,".."));
   if(!actual.Equals(Target,StringComparison.OrdinalIgnoreCase)||!File.Exists(Path.Combine(Target,"installed.flag")))throw new IOException("Uninstaller is outside the Auremu install folder.");
   if(MessageBox.Show("Remove Auremu? Android devices and accounts in your local application data will be kept.","Auremu",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return 0;
   string helper=Path.Combine(Path.GetTempPath(),"Auremu-uninstall-"+Guid.NewGuid().ToString("N")+".exe");File.Copy(Assembly.GetExecutingAssembly().Location,helper);
   Process.Start(new ProcessStartInfo(helper,"--cleanup "+Process.GetCurrentProcess().Id){UseShellExecute=false,CreateNoWindow=true});return 0;
  }
  static int Cleanup(int parent){
   try{Process.GetProcessById(parent).WaitForExit(15000);}catch(ArgumentException){}
   if(!File.Exists(Path.Combine(Target,"installed.flag")))throw new IOException("Installation marker missing.");
   if((File.GetAttributes(Target)&FileAttributes.ReparsePoint)!=0)throw new IOException("Install folder is a junction.");
   // A recursive delete may follow child junctions on some .NET versions. Reject all of them first.
   RejectLinks(Target);Directory.Delete(Target,true);
   if(File.Exists(Shortcut))File.Delete(Shortcut);Registry.CurrentUser.DeleteSubKeyTree(RegistryPath,false);
   MessageBox.Show("Auremu removed. Your Android data has been kept.","Auremu");return 0;
  }
  static void RejectLinks(string directory){foreach(var child in Directory.GetDirectories(directory)){if((File.GetAttributes(child)&FileAttributes.ReparsePoint)!=0)throw new IOException("Unexpected linked folder: "+child);RejectLinks(child);}}
  [STAThread] public static int Main(string[] args){
   try{
    if(args.Length==2&&args[0]=="--extract"){if(Directory.Exists(args[1]))throw new IOException("Extraction target must be a new directory.");Extract(args[1]);return 0;}
    if(args.Length==1&&args[0]=="--uninstall")return Uninstall();
    if(args.Length==2&&args[0]=="--cleanup")return Cleanup(int.Parse(args[1]));
    var app=new Application();var stack=new StackPanel{Margin=new Thickness(32)};
    stack.Children.Add(new TextBlock{Text="Auremu",FontSize=36,Foreground=new SolidColorBrush(Color.FromRgb(27,25,53))});
    stack.Children.Add(new TextBlock{Text="Android. On your desktop.",FontSize=19,Margin=new Thickness(0,8,0,24)});
    stack.Children.Add(new TextBlock{Text="Installs for your Windows account. Android SDK and the system image are downloaded separately on first launch after you accept Google's terms. No Android sessions or accounts are included.",TextWrapping=TextWrapping.Wrap,FontSize=14});
    stack.Children.Add(new TextBlock{Text=Target,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,18,0,18),Foreground=Brushes.SlateGray});
    var status=new TextBlock{Text="Windows 10/11 x64 · .NET Framework 4.8 · WHPX",TextWrapping=TextWrapping.Wrap};stack.Children.Add(status);
    var button=new Button{Content="Install Auremu",FontSize=16,Height=44,Margin=new Thickness(0,24,0,0)};stack.Children.Add(button);
    var window=new Window{Title="Install Auremu 0.1.0",Width=580,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterScreen,Background=new SolidColorBrush(Color.FromRgb(246,244,255)),Content=stack};
    bool busy=false,done=false;window.Closing+=(s,e)=>{if(busy)e.Cancel=true;};
    button.Click+=async(s,e)=>{if(done){Process.Start(Path.Combine(Target,"bin","Auremu.exe"));window.Close();return;}busy=true;button.IsEnabled=false;status.Text="Installing…";try{await Task.Run((Action)Install);done=true;button.Content="Launch Auremu";status.Text="Installed. Your first launch will guide you through Android setup.";}catch(Exception ex){status.Text=ex.Message;}finally{busy=false;button.IsEnabled=true;}};
    return app.Run(window);
   }catch(Exception ex){if(args.Length>0){Console.Error.WriteLine(ex);return 1;}MessageBox.Show(ex.Message,"Auremu setup");return 1;}
  }
 }
}
