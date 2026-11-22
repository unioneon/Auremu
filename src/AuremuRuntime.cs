using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Aemu;

namespace Auremu
{
 public sealed class Runtime
 {
  public const string CleanImage="system-images;android-37.0;default;x86_64";
  public const string StockImage="system-images;android-37.0;google_apis_playstore;x86_64";
  public const string Home="com.auremu.launcher/.HomeActivity";
  public readonly Engine Engine=new Engine();
  public bool Stock {get{return Engine.Settings.UseStockAndroid;}}
  public string Image {get{return Stock?StockImage:CleanImage;}}
  public string AndroidLabel {get{return Stock?"Android 17 · Google Play":"Android 17 · AOSP";}}
  public string Name {get{return Stock?(string.IsNullOrWhiteSpace(Engine.Settings.StockName)?"Auremu_Android_17":Engine.Settings.StockName):(string.IsNullOrWhiteSpace(Engine.Settings.CleanName)?"Auremu_Android_17":Engine.Settings.CleanName);}}
  public string PreviewPath {get{return Path.Combine(Root,"preview-"+Name+".png");}}
  public string Serial; public bool Ready,Starting,Stopping; public Process Emulator,Mirror; public int Fps;
  public double AspectRatio;
  bool stopRequested;
  volatile bool mirrorFrameReady;
  public Action<string> Log=delegate{};public Action<string> Status=delegate{};
  public string Root {get{return Path.GetFullPath(Engine.Root);}}
  public string MirrorTitle {get{return "AuremuDisplay-"+Serial;}}
  public Runtime(){Engine.Log=value=>Log(value);AspectRatio=Engine.Settings.Form=="mobile"?9d/16:16d/9;}
  public Task<string> Adb(params string[] args){if(Serial==null)throw new Exception("Android is not running yet.");return Engine.Exec(Engine.Adb,new[]{"-s",Serial}.Concat(args).ToArray());}
  public async Task EnsureDevice()
  {
   var config=Path.Combine(Engine.AvdRoot,Name+".avd","config.ini");if(File.Exists(config)){var existingLines=File.ReadAllLines(config).ToList();var desired=new Dictionary<string,string>{{"hw.cpu.ncore",Engine.Settings.Cores.ToString()},{"hw.ramSize",Engine.Settings.Memory.ToString()}};bool changed=false;foreach(var v in desired)if(!existingLines.Contains(v.Key+"="+v.Value)){existingLines.RemoveAll(x=>x.StartsWith(v.Key+"="));existingLines.Add(v.Key+"="+v.Value);changed=true;}if(changed)File.WriteAllLines(config,existingLines,new UTF8Encoding(false));return;}
   await Engine.Catalog(false);if(!Engine.Packages.Any(p=>p.Id==Image&&p.Installed))throw new Exception("Install "+AndroidLabel+" from SDK images.");
   Status("Creating Android 17 device…");await Engine.Create(Name,Image);
   var values=new Dictionary<string,string>{{"hw.lcd.width",Engine.Settings.Form=="mobile"?"1080":"1920"},{"hw.lcd.height",Engine.Settings.Form=="mobile"?"1920":"1080"},{"hw.lcd.density",Engine.Settings.Form=="mobile"?"420":"240"},{"hw.initialOrientation",Engine.Settings.Form=="mobile"?"portrait":"landscape"},{"hw.camera.back","none"},{"hw.camera.front","none"},{"hw.audioInput","no"},{"hw.keyboard.lid","no"},{"disk.dataPartition.size","6G"}};
   var lines=File.ReadAllLines(config).Where(x=>!values.Keys.Any(k=>x.StartsWith(k+"="))).ToList();lines.AddRange(values.Select(v=>v.Key+"="+v.Value));File.WriteAllLines(config,lines,new UTF8Encoding(false));Engine.Save();
  }
  static int FreePort(){for(int port=5580;port<5680;port+=2){TcpListener a=null,b=null;try{a=new TcpListener(IPAddress.Loopback,port);b=new TcpListener(IPAddress.Loopback,port+1);a.Start();b.Start();return port;}catch(SocketException){}finally{if(a!=null)a.Stop();if(b!=null)b.Stop();}}throw new Exception("No free Android Emulator port was found.");}
  public async Task Start(bool cold=false)
  {
   if(Ready)return;if(Starting||Stopping)throw new Exception("Wait for Android to finish starting or stopping.");Starting=true;stopRequested=false;
   try
   {
    await EnsureDevice();Status("Checking WHPX…");var check=await Runner.Run(Engine.Emulator,new[]{"-accel-check"},Engine.Settings.Sdk,Engine.AvdRoot,15000);Log(check.Output);if(check.Code!=0)throw new Exception("Hardware acceleration is unavailable. Check Windows Hypervisor Platform and CPU virtualization.");
    // A brand-new device has no Quick Boot state. Do not interrupt its first cold boot after 40 seconds.
    string snapshot=Path.Combine(Engine.AvdRoot,Name+".avd","snapshots","default_boot");
    if(!File.Exists(Path.Combine(snapshot,"snapshot.pb"))||File.Exists(Path.Combine(snapshot,"ram.img.dirty")))cold=true;
    int port=FreePort();Serial="emulator-"+port;var flags=new List<string>{"-avd",Name,"-port",port.ToString(),"-no-window","-no-boot-anim","-no-audio","-no-metrics","-gpu",Engine.Settings.Gpu,"-accel","on"};if(cold)flags.Add("-no-snapshot-load");
    Status("Starting Android 17…");Emulator=StartProcess(Engine.Emulator,flags);var clock=Stopwatch.StartNew();
    while(clock.Elapsed<TimeSpan.FromMinutes(5))
    {
     if(stopRequested)throw new OperationCanceledException("Android startup cancelled.");
     if(!cold&&(Emulator.HasExited||clock.Elapsed>=TimeSpan.FromSeconds(40))){
      Log("Quick Boot did not finish; retrying a cold boot without loading a snapshot or wiping data.");
      Status("Quick Boot failed · starting without a snapshot…");
      if(!Emulator.HasExited){Runner.KillTree(Emulator);await Task.Run(()=>Emulator.WaitForExit(10000));}
      if(stopRequested)throw new OperationCanceledException("Android startup cancelled.");
      cold=true;flags.Add("-no-snapshot-load");Emulator=StartProcess(Engine.Emulator,flags);clock.Restart();continue;
     }
     if(Emulator.HasExited)throw new Exception("Android Emulator exited with code "+Emulator.ExitCode+". Open the log.");
     var r=await Runner.Run(Engine.Adb,new[]{"-s",Serial,"shell","getprop","sys.boot_completed"},Engine.Settings.Sdk,Engine.AvdRoot,6000);
     if(r.Code==0&&r.Output.Trim()=="1")break;await Task.Delay(350);
    }
    if(clock.Elapsed>=TimeSpan.FromMinutes(5))throw new Exception("Android did not finish booting within five minutes. Check the log.");
    if(stopRequested)throw new OperationCanceledException("Android startup cancelled.");
    Status(Stock?"Opening the Android home screen…":"Preparing the Auremu home screen…");await Provision();if(stopRequested)throw new OperationCanceledException("Android startup cancelled.");Ready=true;Log("Android ready in "+clock.Elapsed.TotalSeconds.ToString("F2",CultureInfo.InvariantCulture)+" seconds (including initial setup).");Status(AndroidLabel+" · WHPX");
   }
   catch{if(Emulator!=null&&!Emulator.HasExited){try{Runner.KillTree(Emulator);}catch{}}Ready=false;Serial=null;throw;}
   finally{Starting=false;}
  }
  Process StartProcess(string file,IEnumerable<string> args)
  {
   var info=Runner.Info(file,args,Engine.Settings.Sdk,Engine.AvdRoot);info.WorkingDirectory=Path.GetDirectoryName(file);if(Path.GetFileName(file).Equals("scrcpy.exe",StringComparison.OrdinalIgnoreCase)){info.EnvironmentVariables["ADB"]=Engine.Adb;}
   var p=new Process{StartInfo=info,EnableRaisingEvents=true};p.OutputDataReceived+=(s,e)=>{if(e.Data!=null)ReadLog(e.Data);};p.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)ReadLog(e.Data);};p.Start();p.StandardInput.Close();p.BeginOutputReadLine();p.BeginErrorReadLine();return p;
  }
  void ReadLog(string line){if(line.StartsWith("INFO: Texture"))mirrorFrameReady=true;var fps=Regex.Match(line,@"(\d+) fps");if(fps.Success)Fps=int.Parse(fps.Groups[1].Value);Log(line);}
  public string MirrorExecutable {get{foreach(var tools in new[]{Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","runtime","tools")),Path.Combine(Root,"tools")})if(Directory.Exists(tools)){var dir=Directory.GetDirectories(tools,"scrcpy-win64-*").OrderByDescending(x=>x).FirstOrDefault(x=>File.Exists(Path.Combine(x,"scrcpy.exe")));if(dir!=null)return Path.Combine(dir,"scrcpy.exe");}throw new Exception("scrcpy display module is missing. Extract the complete Auremu package.");}}
  public void StartMirror(){if(!Ready)throw new Exception("Android is still starting.");if(Mirror!=null&&!Mirror.HasExited)return;mirrorFrameReady=false;Mirror=StartProcess(MirrorExecutable,new[]{"-s",Serial,"--window-title",MirrorTitle,"--window-borderless","--window-width=640","--window-height=360","--render-driver=direct3d11","--hwdec=auto","--render-fit=stretched","--max-fps=60","--video-bit-rate=12M","--no-clipboard-autosync","--print-fps","--stay-awake"});}
  public async Task WaitMirrorReady(){for(int i=0;i<200;i++){if(stopRequested)throw new OperationCanceledException();if(Mirror==null||Mirror.HasExited)throw new Exception("Display module exited before receiving an Android frame.");if(mirrorFrameReady)return;await Task.Delay(100);}throw new Exception("Display module did not receive the first Android frame within 20 seconds.");}
  public async Task Provision()
  {
   if(Stock){
    // Keep the existing Android installation and its launcher; no Auremu APK or privacy provisioning.
    string size=await Adb("shell","wm","size");var sizes=Regex.Matches(size,@"(\d+)x(\d+)");
    if(sizes.Count>0){var last=sizes[sizes.Count-1];AspectRatio=double.Parse(last.Groups[1].Value,CultureInfo.InvariantCulture)/double.Parse(last.Groups[2].Value,CultureInfo.InvariantCulture);}
    string home=await Adb("shell","cmd","package","resolve-activity","--brief","-a","android.intent.action.MAIN","-c","android.intent.category.HOME");
    if(home.Contains("com.auremu.launcher"))throw new Exception("В стандартном профиле выбран лаунчер Auremu. Выберите системный домашний экран в настройках Android.");
    await Adb("shell","am","start","-a","android.intent.action.MAIN","-c","android.intent.category.HOME");
    return;
   }
   var apk=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"AuremuLauncher.apk");if(!File.Exists(apk))throw new Exception("Не найден AuremuLauncher.apk рядом с приложением.");string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(apk))).Replace("-","");
   hash="privacy-v3-"+hash;var receipt=Path.Combine(Engine.AvdRoot,Name+".avd","auremu-setup.txt");
   string guestBuild=(await Adb("shell","settings","get","secure","auremu_launcher_build")).Trim();
   string launcherPath=await Adb("shell","pm","path","com.auremu.launcher");
   bool configured=guestBuild==hash&&launcherPath.TrimStart().StartsWith("package:");
   if(!configured)
   {
    await Adb("install","-r",apk);await Adb("shell","settings","put","global","device_provisioned","1");await Adb("shell","settings","put","secure","user_setup_complete","1");await Adb("shell","settings","put","global","captive_portal_mode","0");
    await Adb("shell","settings","put","system","screen_off_timeout","2147483647");
    string packages=await Adb("shell","pm","list","packages");var blocked=new[]{"com.android.adservices.api","com.google.android.adservices.api","com.android.ondevicepersonalization.services","com.google.android.ondevicepersonalization.services","com.android.federatedcompute","com.android.federatedcompute.services","com.android.providers.partnerbookmarks"};
    foreach(var pkg in blocked)if(packages.Split('\n').Any(x=>x.Trim()=="package:"+pkg))await Adb("shell","pm","disable-user","--user","0",pkg);
    if(packages.Contains("package:com.google.android.gms")||packages.Contains("package:com.android.vending"))throw new Exception("Для чистого профиля выбран образ с Google-сервисами. Создайте AOSP-устройство.");
    await Adb("shell","cmd","package","set-home-activity",Home);
    await Adb("shell","settings","put","secure","immersive_mode_confirmations","confirmed");
    await Adb("shell","am","start","-a","android.settings.SETTINGS");
    await SetMode(Engine.Settings.Form);
    await Adb("shell","cmd","alarm","set-timezone","Asia/Tashkent");
    await Adb("shell","settings","put","system","screen_brightness",Engine.Settings.Brightness.ToString());await Adb("shell","cmd","media_session","volume","--stream","3","--set",((int)(Engine.Settings.Volume*15/100d)).ToString());
    await Adb("shell","settings","put","secure","auremu_launcher_build",hash);
    File.WriteAllText(receipt,hash);File.WriteAllText(Path.Combine(Root,"privacy-packages.txt"),packages+"\nDisabled advertising modules:\n"+await Adb("shell","pm","list","packages","-d"));
   }
   string expectedSize=Engine.Settings.Form=="mobile"?"1080x1920":"1920x1080";
   string actualSize=await Adb("shell","wm","size");
   var sizeLines=actualSize.Trim().Split('\n');if(sizeLines.Length==0||!sizeLines[sizeLines.Length-1].Trim().EndsWith(expectedSize))await SetMode(Engine.Settings.Form);
   await ApplySystemBars();
   await Adb("shell","cmd","package","set-home-activity",Home);
   await SetTheme(Engine.Settings.PlayerTheme);await Adb("shell","am","start","-n",Home);
   string resolved=await Adb("shell","cmd","package","resolve-activity","--brief","-a","android.intent.action.MAIN","-c","android.intent.category.HOME");
   if(!resolved.Contains("com.auremu.launcher"))throw new Exception("Не удалось назначить Auremu домашним экраном Android.");
  }
  public async Task SetTheme(string theme){Engine.Settings.PlayerTheme=theme;Engine.Save();if(Serial!=null&&(Ready||Starting)){if(Stock)await Adb("shell","cmd","uimode","night",theme=="dark"?"yes":"no");else await Adb("shell","am","broadcast","-n","com.auremu.launcher/.ControlReceiver","--es","theme",theme);}}
  public async Task SetMode(string mode)
  {
   Engine.Settings.Form=mode=="mobile"?"mobile":"tablet";Engine.Save();if(Serial==null)return;
   await Adb("shell","settings","put","system","accelerometer_rotation","0");
   await Adb("shell","settings","put","system","user_rotation","0");
   await Adb("shell","wm","size",mode=="mobile"?"1080x1920":"1920x1080");await Adb("shell","wm","density",mode=="mobile"?"420":"240");AspectRatio=mode=="mobile"?9d/16:16d/9;
   await ApplySystemBars();
   if(Ready){if(Stock)await Adb("shell","am","start","-a","android.intent.action.MAIN","-c","android.intent.category.HOME");else await Adb("shell","am","start","-n",Home);}
   File.WriteAllText(Path.Combine(Engine.AvdRoot,Name+".avd","auremu-form.txt"),Engine.Settings.Form);
  }
  public Task<string> ApplySystemBars(){return Stock?Task.FromResult(""):Adb("shell","settings","put","global","policy_control","immersive.full=*");}
  public async Task Capture(string path)
  {
   var pi=Runner.Info(Engine.Adb,new[]{"-s",Serial,"exec-out","screencap","-p"},Engine.Settings.Sdk,Engine.AvdRoot);
   using(var p=new Process{StartInfo=pi}){p.Start();p.StandardInput.Close();var error=p.StandardError.ReadToEndAsync();using(var output=File.Create(path))await p.StandardOutput.BaseStream.CopyToAsync(output);await Task.Run(()=>p.WaitForExit(15000));if(!p.HasExited||p.ExitCode!=0)throw new Exception("Could not save screenshot: "+await error);await error;}
  }
  public async Task Stop()
  {
   if(Stopping)return;Stopping=true;stopRequested=true;bool saved=false;
   try{
    if(Ready){try{await Capture(PreviewPath);}catch{}}
    if(Mirror!=null&&!Mirror.HasExited)Runner.KillTree(Mirror);Mirror=null;
    if(Emulator!=null&&!Emulator.HasExited){
     if(Ready&&Serial!=null){try{Status("Saving Quick Boot…");await Adb("emu","kill");saved=await Task.Run(()=>Emulator.WaitForExit(45000));}catch(Exception ex){Log("Graceful shutdown failed: "+ex.Message);}}
     if(!Emulator.HasExited){Runner.KillTree(Emulator);await Task.Run(()=>Emulator.WaitForExit(10000));}
    }
    Status(saved?"Android stopped · Quick Boot saved":"Android stopped");
   }finally{Ready=false;Serial=null;Stopping=false;}
  }
 }

 public sealed class AndroidHost:HwndHost
 {
  IntPtr holder,child,cornerMask;int maskWidth,maskHeight,maskRadius;bool maskDirty=true;
  System.Windows.Media.Color frameColor=System.Windows.Media.Color.FromRgb(246,244,255);
  public bool Attached {get{return child!=IntPtr.Zero&&GetParent(child)==holder;}}
  public Action<string> Log=delegate{};
  protected override HandleRef BuildWindowCore(HandleRef parent){holder=CreateWindowEx(0,"static","",0x56000000,0,0,100,100,parent.Handle,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);if(holder==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());return new HandleRef(this,holder);}
  protected override void DestroyWindowCore(HandleRef hwnd){if(cornerMask!=IntPtr.Zero){DestroyWindow(cornerMask);cornerMask=IntPtr.Zero;}if(child!=IntPtr.Zero){SetParent(child,IntPtr.Zero);child=IntPtr.Zero;}DestroyWindow(hwnd.Handle);holder=IntPtr.Zero;maskDirty=true;}
  public void SetFrameColor(System.Windows.Media.Color color){if(color==frameColor)return;frameColor=color;maskDirty=true;ResizeNative();}
  public async Task Attach(string title,int pid)
  {
   for(int i=0;i<100;i++){IntPtr found=IntPtr.Zero;EnumWindows((hwnd,l)=>{uint owner;GetWindowThreadProcessId(hwnd,out owner);if(owner==(uint)pid){var text=new StringBuilder(512);GetWindowText(hwnd,text,512);if(text.ToString()==title){found=hwnd;return false;}}return true;},IntPtr.Zero);
    if(found!=IntPtr.Zero&&holder!=IntPtr.Zero){
     ShowWindow(found,0);long style=GetWindowLong(found,-16);SetWindowLong(found,-16,(int)((style&~0x80c40000L)|0x46000000L));
     SetWindowLong(found,-20,(GetWindowLong(found,-20)&~0x00060201)|0x00000080);
     SetParent(found,holder);int error=Marshal.GetLastWin32Error();if(GetParent(found)!=holder)throw new Exception("Экран Android не встроен (SetParent, код "+error+").");
     child=found;ResizeNative();ShowWindow(child,5);ResizeNative();
     Log("Display attached: child="+child+" parent="+GetParent(child)+" size="+ActualWidth+"x"+ActualHeight);return;}await Task.Delay(50);}
   throw new Exception("Не удалось встроить экран Android. Проверьте журнал отображения.");
  }
  protected override void OnRenderSizeChanged(SizeChangedInfo info){base.OnRenderSizeChanged(info);ResizeNative();}
  protected override void OnWindowPositionChanged(Rect bounds){base.OnWindowPositionChanged(bounds);ResizeNative();}
  public void SyncBounds(){if(Attached)ResizeNative();}
  void ResizeNative(){
   if(holder==IntPtr.Zero||child==IntPtr.Zero)return;
   RECT rect,childRect,holderPosition,childPosition;GetClientRect(holder,out rect);GetClientRect(child,out childRect);GetWindowRect(holder,out holderPosition);GetWindowRect(child,out childPosition);
   int w=Math.Max(1,rect.Right),h=Math.Max(1,rect.Bottom);
   if(childRect.Right!=w||childRect.Bottom!=h||holderPosition.Left!=childPosition.Left||holderPosition.Top!=childPosition.Top)SetWindowPos(child,IntPtr.Zero,0,0,w,h,0x34);
   int radius=Math.Min(Math.Min(w,h)/2,Math.Max(1,(int)Math.Round(32*VisualTreeHelperDpi())));
   if(cornerMask==IntPtr.Zero){cornerMask=CreateWindowEx(0x08080020,"static","",0x50000000,0,0,w,h,holder,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);if(cornerMask==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());maskDirty=true;}
   if(maskDirty||maskWidth!=w||maskHeight!=h||maskRadius!=radius){UploadMask(w,h,radius);maskWidth=w;maskHeight=h;maskRadius=radius;maskDirty=false;}
   SetWindowPos(cornerMask,IntPtr.Zero,0,0,w,h,0x50);
  }
  void UploadMask(int w,int h,int radius){
   var pixels=ScreenCornerMask.Render(w,h,radius,frameColor.R,frameColor.G,frameColor.B);
   IntPtr screen=GetDC(IntPtr.Zero),dc=IntPtr.Zero,bitmap=IntPtr.Zero,previous=IntPtr.Zero;
   try{
    dc=CreateCompatibleDC(screen);var header=new BITMAPINFOHEADER{Size=40,Width=w,Height=-h,Planes=1,BitCount=32,SizeImage=(uint)pixels.Length};IntPtr bits;
    bitmap=CreateDIBSection(screen,ref header,0,out bits,IntPtr.Zero,0);if(bitmap==IntPtr.Zero||bits==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    Marshal.Copy(pixels,0,bits,pixels.Length);previous=SelectObject(dc,bitmap);var size=new SIZE{Width=w,Height=h};var origin=new POINT();var blend=new BLENDFUNCTION{SourceConstantAlpha=255,AlphaFormat=1};
    if(!UpdateLayeredWindow(cornerMask,screen,IntPtr.Zero,ref size,dc,ref origin,0,ref blend,2))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
   }finally{if(previous!=IntPtr.Zero)SelectObject(dc,previous);if(bitmap!=IntPtr.Zero)DeleteObject(bitmap);if(dc!=IntPtr.Zero)DeleteDC(dc);if(screen!=IntPtr.Zero)ReleaseDC(IntPtr.Zero,screen);}
  }
  double VisualTreeHelperDpi(){var source=PresentationSource.FromVisual(this);return source!=null&&source.CompositionTarget!=null?source.CompositionTarget.TransformToDevice.M11:1;}
  public void FocusAndroid(){if(child!=IntPtr.Zero)SetFocus(child);}
  public void Detach(){child=IntPtr.Zero;}
  delegate bool EnumProc(IntPtr hwnd,IntPtr l);
  [StructLayout(LayoutKind.Sequential)]struct RECT{public int Left,Top,Right,Bottom;}
  [StructLayout(LayoutKind.Sequential)]struct SIZE{public int Width,Height;}
  [StructLayout(LayoutKind.Sequential)]struct POINT{public int X,Y;}
  [StructLayout(LayoutKind.Sequential,Pack=1)]struct BLENDFUNCTION{public byte BlendOp,BlendFlags,SourceConstantAlpha,AlphaFormat;}
  [StructLayout(LayoutKind.Sequential)]struct BITMAPINFOHEADER{public uint Size;public int Width,Height;public ushort Planes,BitCount;public uint Compression,SizeImage;public int XPelsPerMeter,YPelsPerMeter;public uint ClrUsed,ClrImportant;}
  [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr CreateWindowEx(int ex,string cls,string name,int style,int x,int y,int w,int h,IntPtr parent,IntPtr menu,IntPtr inst,IntPtr param);
  [DllImport("user32.dll")]static extern bool DestroyWindow(IntPtr hwnd);
  [DllImport("user32.dll")]static extern bool EnumWindows(EnumProc callback,IntPtr l);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr hwnd,StringBuilder text,int max);
  [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
  [DllImport("user32.dll")]static extern IntPtr GetParent(IntPtr hwnd);
  [DllImport("user32.dll")]static extern int GetWindowLong(IntPtr hwnd,int index);
  [DllImport("user32.dll")]static extern int SetWindowLong(IntPtr hwnd,int index,int value);
  [DllImport("user32.dll",SetLastError=true)]static extern IntPtr SetParent(IntPtr child,IntPtr parent);
  [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int w,int h,uint flags);
  [DllImport("user32.dll")]static extern bool GetClientRect(IntPtr hwnd,out RECT rect);
  [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr hwnd,out RECT rect);
  [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr hwnd,int cmd);
  [DllImport("user32.dll")]static extern IntPtr SetFocus(IntPtr hwnd);
  [DllImport("user32.dll")]static extern IntPtr GetDC(IntPtr hwnd);
  [DllImport("user32.dll")]static extern int ReleaseDC(IntPtr hwnd,IntPtr dc);
  [DllImport("gdi32.dll")]static extern IntPtr CreateCompatibleDC(IntPtr dc);
  [DllImport("gdi32.dll")]static extern bool DeleteDC(IntPtr dc);
  [DllImport("gdi32.dll",SetLastError=true)]static extern IntPtr CreateDIBSection(IntPtr dc,ref BITMAPINFOHEADER info,uint usage,out IntPtr bits,IntPtr section,uint offset);
  [DllImport("gdi32.dll")]static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
  [DllImport("user32.dll",SetLastError=true)]static extern bool UpdateLayeredWindow(IntPtr hwnd,IntPtr dst,IntPtr position,ref SIZE size,IntPtr src,ref POINT origin,uint key,ref BLENDFUNCTION blend,uint flags);
  [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr obj);
 }

 public static class ScreenCornerMask {
  // Premultiplied BGRA. Only the four corners cover the native video surface.
  public static byte[] Render(int width,int height,int radius,byte red,byte green,byte blue){
   var pixels=new byte[checked(width*height*4)];int r=Math.Min(radius,Math.Min(width,height)/2);if(r<=0)return pixels;
   const int samples=8,total=samples*samples;
   for(int y=0;y<r;y++)for(int x=0;x<r;x++){
    int outside=0;for(int sy=0;sy<samples;sy++)for(int sx=0;sx<samples;sx++){double dx=r-x-(sx+.5)/samples,dy=r-y-(sy+.5)/samples;if(dx*dx+dy*dy>r*r)outside++;}
    int alpha=(outside*255+total/2)/total;if(alpha==0)continue;
    Put(pixels,width,x,y,red,green,blue,alpha);Put(pixels,width,width-1-x,y,red,green,blue,alpha);
    Put(pixels,width,x,height-1-y,red,green,blue,alpha);Put(pixels,width,width-1-x,height-1-y,red,green,blue,alpha);
   }
   return pixels;
  }
  static void Put(byte[] pixels,int width,int x,int y,byte red,byte green,byte blue,int alpha){int i=(y*width+x)*4;pixels[i]=(byte)((blue*alpha+127)/255);pixels[i+1]=(byte)((green*alpha+127)/255);pixels[i+2]=(byte)((red*alpha+127)/255);pixels[i+3]=(byte)alpha;}
 }
}
