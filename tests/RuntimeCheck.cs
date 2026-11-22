using System;
using System.IO;
using System.Threading.Tasks;
namespace Auremu {
 public static class RuntimeCheck {
  [STAThread] public static int Main(){try{Run().GetAwaiter().GetResult();return 0;}catch(Exception ex){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"runtime-check-error.txt"),ex.ToString());return 1;}}
  static async Task Run(){
   var runtime=new Runtime();var report=Path.Combine(runtime.Root,"auremu-runtime-check.txt");object gate=new object();File.WriteAllText(report,"Auremu real runtime check: "+runtime.AndroidLabel+" / "+runtime.Name+"\n");
   runtime.Log=s=>{lock(gate)File.AppendAllText(report,s+"\n");};runtime.Status=runtime.Log;
   await runtime.Start();
   Exception failure=null;try{
    await Task.Delay(4000);await runtime.Capture(Path.Combine(runtime.Root,runtime.Stock?"stock-android-home.png":"auremu-home.png"));
    string home=await runtime.Adb("shell","cmd","package","resolve-activity","--brief","-a","android.intent.action.MAIN","-c","android.intent.category.HOME");
    File.AppendAllText(report,"Resolved HOME: "+home+"\n");
    if(runtime.Stock){
     if(home.Contains("com.auremu.launcher"))throw new Exception("Stock Android is using custom Auremu HOME");
     string play=await runtime.Adb("shell","pm","path","com.android.vending");
     if(!play.Contains("package:"))throw new Exception("Stock Google Play package is absent");
     string custom=await runtime.Adb("shell","pm","list","packages","com.auremu.launcher");
     if(custom.Contains("package:"))throw new Exception("Custom Auremu launcher unexpectedly installed in stock profile");
    }else{
     var permissions=await runtime.Adb("shell","dumpsys","package","com.auremu.launcher");File.WriteAllText(Path.Combine(runtime.Root,"launcher-permissions.txt"),permissions);
     if(permissions.Contains("android.permission.INTERNET"))throw new Exception("Launcher unexpectedly requests INTERNET");
    }
   }catch(Exception ex){failure=ex;}await runtime.Stop();if(failure!=null)throw failure;
   File.AppendAllText(report,runtime.Stock?"PASS: stock Android boot, original HOME, Google Play present, no Auremu launcher, screenshot, graceful shutdown.\n":"PASS: AOSP boot, Auremu HOME, no INTERNET permission, screenshot, graceful shutdown.\n");
  }
 }
}
