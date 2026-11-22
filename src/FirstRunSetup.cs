using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;

namespace Auremu {
 public sealed class AndroidSetup {
  public const string ToolsUrl="https://dl.google.com/android/repository/commandlinetools-win-15859902_latest.zip";
  public const string ToolsSha256="90ae805d20434428bffcb699c290860f19bb5f66a67e6b330067e3de801fb04a";
  readonly Runtime runtime;readonly CancellationToken token;public Action<string> Progress=delegate{};
  public AndroidSetup(Runtime runtime,CancellationToken token){this.runtime=runtime;this.token=token;}
  public static bool Needed(Runtime runtime){return !File.Exists(runtime.Engine.Emulator)||!File.Exists(runtime.Engine.Adb)||!Directory.Exists(Path.Combine(runtime.Engine.Settings.Sdk,"system-images","android-37.0","google_apis_playstore","x86_64"));}
  public static void Extract(string zip,string target){
   string root=Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;Directory.CreateDirectory(root);
   using(var archive=ZipFile.OpenRead(zip))foreach(var entry in archive.Entries){string file=Path.GetFullPath(Path.Combine(root,entry.FullName));if(!file.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new IOException("Unsafe archive entry.");if(string.IsNullOrEmpty(entry.Name)){Directory.CreateDirectory(file);continue;}Directory.CreateDirectory(Path.GetDirectoryName(file));using(var input=entry.Open())using(var output=File.Create(file))input.CopyTo(output);}
  }
  async Task Download(string url,string file,string sha){
   token.ThrowIfCancellationRequested();Directory.CreateDirectory(Path.GetDirectoryName(file));
   ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
   using(var client=new WebClient()){
    client.Headers[HttpRequestHeader.UserAgent]="Auremu/0.1.0";client.DownloadProgressChanged+=(s,e)=>Progress("Загрузка: "+e.ProgressPercentage+"% · "+(e.BytesReceived/1048576)+" МБ");
    using(token.Register(()=>client.CancelAsync()))await client.DownloadFileTaskAsync(new Uri(url),file);
   }
   token.ThrowIfCancellationRequested();string actual;using(var stream=File.OpenRead(file))using(var algorithm=SHA256.Create())actual=BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-","");
   if(!actual.Equals(sha,StringComparison.OrdinalIgnoreCase))throw new IOException("Контрольная сумма загрузки не совпадает. Повторите установку.");
  }
  public async Task Install(){
   string root=runtime.Root;var drive=new DriveInfo(Path.GetPathRoot(root));if(drive.AvailableFreeSpace<15L*1024*1024*1024)throw new IOException("Для SDK, Android и устройства нужно не менее 15 ГБ свободного места.");
   string downloads=Path.Combine(root,"downloads"),sdk=Path.Combine(root,"sdk");Directory.CreateDirectory(downloads);
   string tools=Path.Combine(sdk,"cmdline-tools","latest");
   if(!File.Exists(Path.Combine(tools,"bin","sdkmanager.bat"))){
    Progress("Скачиваем официальные Android SDK Tools…");string archive=Path.Combine(downloads,"android-tools.zip");await Download(ToolsUrl,archive,ToolsSha256);
    string stage=Path.Combine(downloads,"tools-"+Guid.NewGuid().ToString("N"));await Task.Run(()=>Extract(archive,stage));token.ThrowIfCancellationRequested();Directory.CreateDirectory(Path.GetDirectoryName(tools));if(Directory.Exists(tools))throw new IOException("Неполная установка SDK Tools. Выберите другой каталог SDK или удалите только data/sdk/cmdline-tools/latest и повторите.");Directory.Move(Path.Combine(stage,"cmdline-tools"),tools);
   }
   string javaRoot=Path.Combine(root,"java");
   if(!Directory.Exists(javaRoot)||!Directory.GetDirectories(javaRoot).Any(d=>File.Exists(Path.Combine(d,"bin","java.exe")))){
    Progress("Получаем Eclipse Temurin JDK 21…");string metadata;ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
    using(var client=new WebClient()){client.Headers[HttpRequestHeader.UserAgent]="Auremu/0.1.0";metadata=await client.DownloadStringTaskAsync("https://api.adoptium.net/v3/assets/latest/21/hotspot?architecture=x64&image_type=jdk&os=windows&vendor=eclipse");}
    var items=(object[])new JavaScriptSerializer().DeserializeObject(metadata);var first=(Dictionary<string,object>)items[0];var binary=(Dictionary<string,object>)first["binary"];var package=(Dictionary<string,object>)binary["package"];
    string url=(string)package["link"],checksum=(string)package["checksum"];if(!url.StartsWith("https://github.com/adoptium/",StringComparison.Ordinal)||checksum.Length!=64)throw new IOException("Некорректные метаданные JDK.");
    string archive=Path.Combine(downloads,"temurin-jdk.zip");await Download(url,archive,checksum);Progress("Распаковываем JDK…");await Task.Run(()=>Extract(archive,javaRoot));
   }
   token.ThrowIfCancellationRequested();runtime.Engine.Settings.Sdk=sdk;runtime.Engine.Settings.UseStockAndroid=true;runtime.Engine.Save();
   Progress("Устанавливаем Emulator, ADB и Android 17. Это может занять несколько минут…");
   runtime.Engine.Log=message=>Progress(message);
   // This method is reached only after the user accepts the displayed SDK terms.
   if(runtime.Engine.Cli==null){
    string sdkmanager=runtime.Engine.Tool("sdkmanager");
    var licences=await Aemu.Runner.Run(sdkmanager,new[]{"--sdk_root="+sdk,"--licenses"},sdk,runtime.Engine.AvdRoot,300000,string.Concat(Enumerable.Repeat("y\n",100)),Progress);
    if(licences.Code!=0)throw new Exception("Не удалось принять лицензии SDK. "+licences.Output);
   }
   token.ThrowIfCancellationRequested();await runtime.Engine.Install("emulator","platform-tools",Runtime.StockImage);token.ThrowIfCancellationRequested();
   await runtime.EnsureDevice();Progress("Android установлен. Можно запускать устройство.");
  }
 }

 public sealed partial class Desktop {
  Task<bool> FirstRunDialog(){
   if(runtime.Ready||runtime.Starting){status.Text="Остановите Android перед настройкой SDK.";return Task.FromResult(false);}
   bool success=false,installing=false;var cancel=new CancellationTokenSource();
   var description=Text("Установите официальный Android 17 с Google Play или подключите уже установленный SDK. Ваши приложения и аккаунты появятся только в вашем новом устройстве.",14,true);
   var info=Text("Для первой установки нужны интернет, WHPX и минимум 15 ГБ свободного места.",13,true);
   var terms=new CheckBox{Content="Принимаю Android SDK License Agreement и условия образа Google Play",Margin=new Thickness(0,12,0,12)};
   var existing=new TextBox{Text=runtime.Engine.Settings.Sdk,ToolTip="Каталог Android SDK"};
   var progress=Text("Android не входит в установочный пакет; он скачивается с серверов Google.",13,true);progress.MaxHeight=120;
   Window dialog=null;Button install=null,connect=null;var buttons=new StackPanel();
   Action<string> update=value=>Window.Dispatcher.BeginInvoke(new Action(()=>progress.Text=value));
   install=Btn("Установить Android",async()=>{
    if(terms.IsChecked!=true){progress.Text="Для загрузки отметьте согласие с условиями SDK.";return;}
    installing=true;install.IsEnabled=false;connect.IsEnabled=false;terms.IsEnabled=false;
    try{var setup=new AndroidSetup(runtime,cancel.Token){Progress=update};await setup.Install();success=true;dialog.Close();}
    catch(Exception ex){Log(ex.ToString());progress.Text="Установка не завершена: "+ex.Message;}
    finally{installing=false;install.IsEnabled=true;connect.IsEnabled=true;terms.IsEnabled=true;}
   },true);
   connect=Btn("Подключить существующий SDK",async()=>{
    try{string sdk=Path.GetFullPath(existing.Text.Trim());if(!File.Exists(Path.Combine(sdk,"emulator","emulator.exe"))||!Directory.Exists(Path.Combine(sdk,"system-images","android-37.0","google_apis_playstore","x86_64")))throw new Exception("В этом SDK нет Emulator или образа Android 17 Google Play.");runtime.Engine.Settings.Sdk=sdk;runtime.Engine.Save();await runtime.EnsureDevice();success=true;dialog.Close();}
    catch(Exception ex){progress.Text=ex.Message;}
   });
   buttons.Children.Add(install);buttons.Children.Add(connect);buttons.Children.Add(Btn("Условия Android SDK",()=>Process.Start(new ProcessStartInfo("https://developer.android.com/studio/terms"){UseShellExecute=true})));
   dialog=Dialog("Первый запуск",Stack(Text("Добро пожаловать в Auremu",24),description,info,terms,existing,buttons,progress),620);
   dialog.Closing+=(s,e)=>{if(installing&&!success){e.Cancel=true;progress.Text="Установка выполняется. Дождитесь завершения текущего шага.";}};
   dialog.ShowDialog();runtime.Engine.Log=Log;BuildManager();return Task.FromResult(success);
  }
 }
}
