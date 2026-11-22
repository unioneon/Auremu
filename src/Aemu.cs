using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Aemu
{
 public class Settings { public bool UseStockAndroid=true; public string StockName="Auremu_Android_17"; public string Sdk = ""; public int Cores = 4; public int Memory = 4096; public string Gpu = "auto"; public string CleanName="Auremu_Android_17"; public string PlayerTheme="light"; public string Form="mobile"; public int Volume=65; public int Brightness=140; }
 public class Result { public int Code; public string Output; }
 public class Device { public string Name; public string Image; public string Serial; public bool Starting; }
 public class Package { public string Id; public bool Installed; public override string ToString() { return Id + (Installed ? "  ✓" : ""); } }

 public static class Runner
 {
  // Windows CRT command-line escaping. Arguments never pass through a shell for .exe tools.
  public static string Quote(string value) { return "\"" + Regex.Replace(value, "(\\\\*)\"", "$1$1\\\"").TrimEnd('\\') + new string('\\', value.Reverse().TakeWhile(c => c == '\\').Count() * 2) + "\""; }
  public static void SafeBatch(string value) { if (value.IndexOfAny(new char[] { '"', '%', '!', '&', '|', '<', '>', '^', '\r', '\n' }) >= 0) throw new Exception("Путь или аргумент содержит неподдерживаемые символы оболочки."); }
  public static ProcessStartInfo Info(string tool, IEnumerable<string> args, string sdk, string avd)
  {
   var list = args.ToArray();
   var pi = new ProcessStartInfo(tool, string.Join(" ", list.Select(Quote))) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
   if (tool.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) || tool.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
   {
    SafeBatch(tool); foreach (var a in list) SafeBatch(a);
    pi.FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
    pi.Arguments = "/d /s /c \"" + Quote(tool) + " " + string.Join(" ", list.Select(Quote)) + "\"";
   }
   if (!string.IsNullOrEmpty(sdk)) { pi.EnvironmentVariables["ANDROID_HOME"] = sdk; pi.EnvironmentVariables["ANDROID_SDK_ROOT"] = sdk; }
   if (!string.IsNullOrEmpty(avd)) { pi.EnvironmentVariables["ANDROID_AVD_HOME"] = avd; pi.EnvironmentVariables["ANDROID_USER_HOME"] = Path.Combine(Path.GetDirectoryName(avd),"android-user"); }
   var javaRoot = !string.IsNullOrWhiteSpace(sdk) ? Path.GetFullPath(Path.Combine(sdk,"..","java")) : Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","data","java"));
   if (Directory.Exists(javaRoot)) { var java = Directory.GetDirectories(javaRoot).FirstOrDefault(x=>File.Exists(Path.Combine(x,"bin","java.exe"))); if(java!=null) pi.EnvironmentVariables["JAVA_HOME"] = java; }
   return pi;
  }
  static async Task<string> Read(StreamReader reader, Action<string> progress)
  {
   var output=new StringBuilder(); string line;
   while((line=await reader.ReadLineAsync())!=null) { output.AppendLine(line); if(progress!=null) progress(line); }
   return output.ToString();
  }
  public static void KillTree(Process target)
  {
   if(target==null || target.HasExited) return;
   using(var kill=Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"taskkill.exe"),"/PID "+target.Id+" /T /F") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true })) kill.WaitForExit(5000);
  }
  public static async Task<Result> Run(string tool, IEnumerable<string> args, string sdk, string avd, int timeout, string input = null, Action<string> progress = null, Action<Process> active = null)
  {
   using (var process = new Process { StartInfo = Info(tool, args, sdk, avd) })
   {
    process.Start(); if(active!=null) active(process); var stdout = Read(process.StandardOutput,progress); var stderr = Read(process.StandardError,progress);
    try { if (input != null) await process.StandardInput.WriteAsync(input); process.StandardInput.Close(); } catch (IOException) { /* Tool may finish without requesting input; inspect its exit code below. */ }
    bool exited = await Task.Run(() => process.WaitForExit(timeout));
    if (!exited) { try { KillTree(process); } catch { } throw new TimeoutException("Инструмент превысил время ожидания. Проверьте журнал и соединение с интернетом."); }
    return new Result { Code = process.ExitCode, Output = (await stdout) + "\n" + (await stderr) };
   }
  }
 }

 public class Engine
 {
  public static string DefaultRoot(){string explicitRoot=Environment.GetEnvironmentVariable("AUREMU_DATA_DIR");if(!string.IsNullOrWhiteSpace(explicitRoot))return Path.GetFullPath(explicitRoot);string app=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,".."));if(File.Exists(Path.Combine(app,"installed.flag"))&&!File.Exists(Path.Combine(app,"portable.flag")))return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Auremu","data");return Path.Combine(app,"data");}
  public readonly string Root = DefaultRoot();
  public string AvdRoot { get { return Path.GetFullPath(Path.Combine(Root, "avd")); } }
  public Settings Settings;
  public List<Device> Devices = new List<Device>();
  public List<Package> Packages = new List<Package>();
  public Dictionary<string, Process> Launches = new Dictionary<string, Process>();
  public Process Active;
  public Action<string> Log = delegate { };
  public string Emulator { get { return Path.Combine(Settings.Sdk, "emulator", "emulator.exe"); } }
  public string Adb { get { return Path.Combine(Settings.Sdk, "platform-tools", "adb.exe"); } }
  public string Tool(string name)
  {
   var root = Path.Combine(Settings.Sdk, "cmdline-tools");
   var latest = Path.Combine(root, "latest", "bin", name + ".bat");
   if (File.Exists(latest)) return latest;
   if (Directory.Exists(root)) foreach (var dir in Directory.GetDirectories(root).OrderByDescending(x => x)) { var path = Path.Combine(dir, "bin", name + ".bat"); if (File.Exists(path)) return path; }
   throw new Exception("Не найден " + name + ". Установите Android SDK Command-line Tools и укажите папку SDK в настройках.");
  }
  public string Cli { get { try { var path=Path.Combine(Path.GetDirectoryName(Tool("sdkmanager")),"android.exe"); return File.Exists(path) ? path : null; } catch { return null; } } }
  public Engine(string dataRoot = null)
  {
   if (dataRoot != null) Root = dataRoot;
   Directory.CreateDirectory(Root); Directory.CreateDirectory(AvdRoot);
   try { Settings = new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(Path.Combine(Root, "settings.json"))); } catch { Settings = new Settings(); }
   if (string.IsNullOrWhiteSpace(Settings.Sdk) || !Directory.Exists(Settings.Sdk))
   {
    var candidates = new[] { Environment.GetEnvironmentVariable("ANDROID_HOME"), Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT"), Path.GetFullPath(Path.Combine(Root,"sdk")), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk") };
    Settings.Sdk = candidates.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x) && Directory.Exists(x)) ?? "";
   }
  }
  public void Save() { File.WriteAllText(Path.Combine(Root, "settings.json"), new JavaScriptSerializer().Serialize(Settings), Encoding.UTF8); }
  public async Task<string> Exec(string path, params string[] args)
  {
   Log(Path.GetFileName(path) + " " + string.Join(" ", args));
   Result r;
   int timeout=1800000;
   if(path.EndsWith("adb.exe",StringComparison.OrdinalIgnoreCase)) timeout=args.Contains("install") ? 300000 : 30000;
   else if(path.EndsWith("emulator.exe",StringComparison.OrdinalIgnoreCase)) timeout=30000;
   else if(path.EndsWith("avdmanager.bat",StringComparison.OrdinalIgnoreCase) || args.Contains("list")) timeout=120000;
   try { r = await Runner.Run(path, args, Settings.Sdk, AvdRoot, timeout, path.EndsWith("avdmanager.bat", StringComparison.OrdinalIgnoreCase) ? "no\n" : null, Log, p=>Active=p); } finally { Active=null; }
   if (r.Code != 0) throw new Exception("Ошибка " + Path.GetFileName(path) + " (код " + r.Code + "). " + r.Output.Trim().Substring(0, Math.Min(400, r.Output.Trim().Length)));
   return r.Output;
  }
  public static List<Package> ParsePackages(string text)
  {
   var result = new Dictionary<string, Package>(); bool installed = false;
   foreach (var raw in text.Split('\n'))
   {
    var line = raw.Trim();
    if (line.StartsWith("Installed packages", StringComparison.OrdinalIgnoreCase)) installed = true;
    if (line.StartsWith("Available", StringComparison.OrdinalIgnoreCase)) installed = false;
    line=Regex.Replace(line,@"\x1b\[[0-9;]*m","");
    var match = Regex.Match(line, @"^(system-images[;/]android-[a-zA-Z0-9._-]+[;/][a-zA-Z0-9._-]+[;/][a-zA-Z0-9._-]+)(?:\s|\|)");
    if (!match.Success) continue;
    var id = match.Groups[1].Value.Replace('/',';');
    if (!result.ContainsKey(id)) result[id] = new Package { Id = id };
    result[id].Installed |= installed;
   }
   return result.Values.OrderByDescending(x => ApiLevel(x.Id)).ThenBy(x => x.Id).ToList();
  }
  static int ApiLevel(string id) { int value; return int.TryParse(Regex.Match(id, @"android-(\d+)").Groups[1].Value, out value) ? value : 0; }
  public async Task Catalog(bool online)
  {
   if(Cli!=null) { var args = new List<string>{"--no-metrics","--sdk="+Settings.Sdk,"sdk","list"}; if(online) args.Add("--all"); args.Add("system-images/*"); Packages=ParsePackages(await Exec(Cli,args.ToArray())); }
   else Packages = ParsePackages(await Exec(Tool("sdkmanager"), "--sdk_root=" + Settings.Sdk, online ? "--list" : "--list_installed", "--channel=0"));
  }
  public async Task Install(params string[] packages)
  {
   if(Cli!=null) { var args=new List<string>{"--no-metrics","--sdk="+Settings.Sdk,"sdk","install"}; args.AddRange(packages.Select(x=>x.Replace(';','/'))); await Exec(Cli,args.ToArray()); }
   else { var args=new List<string>{"--sdk_root="+Settings.Sdk}; args.AddRange(packages); await Exec(Tool("sdkmanager"),args.ToArray()); }
  }
  public async Task Refresh()
  {
   if (!File.Exists(Emulator)) { Devices.Clear(); return; }
   var names = await Exec(Emulator, "-list-avds");
   var devices = new List<Device>();
   foreach (var name in names.Split('\n').Select(x => x.Trim()).Where(x => Regex.IsMatch(x, @"^[a-zA-Z0-9_.-]+$")))
   {
    string config = Path.Combine(AvdRoot, name + ".avd", "config.ini");
    string image = "Android";
    if (File.Exists(config)) { var line = File.ReadLines(config).FirstOrDefault(x => x.StartsWith("image.sysdir.1=")); if (line != null) image = line.Substring(15).Replace("system-images", "").Trim('\\', '/').Replace('\\', ' ').Replace('/', ' '); }
    Process p; bool starting = Launches.TryGetValue(name, out p) && !p.HasExited;
    devices.Add(new Device { Name = name, Image = image, Starting = starting });
   }
   if (File.Exists(Adb))
   {
    var connected = await Exec(Adb, "devices");
    foreach (Match m in Regex.Matches(connected, @"(?m)^(emulator-\d+)\s+device\s*$"))
    {
     string serial = m.Groups[1].Value;
     var name = (await Exec(Adb, "-s", serial, "emu", "avd", "name")).Split('\n')[0].Trim();
     var device = devices.FirstOrDefault(x => x.Name == name); if (device != null) { device.Serial = serial; device.Starting = false; }
    }
   }
   Devices = devices;
  }
  public async Task Create(string name, string image)
  {
   if (!Regex.IsMatch(name, @"^[a-zA-Z][a-zA-Z0-9_-]{0,47}$")) throw new Exception("Имя: латинская буква, затем до 47 букв, цифр, _ или -.");
   if (!Packages.Any(p => p.Id == image && p.Installed)) throw new Exception("Сначала установите выбранный образ SDK.");
   if (Directory.Exists(Path.Combine(AvdRoot, name + ".avd"))) throw new Exception("Устройство с этим именем уже существует.");
   await Exec(Tool("avdmanager"), "create", "avd", "-n", name, "-k", image);
   Configure(name,image); await Refresh();
  }
  public void Configure(string name,string image)
  {
   var file = Path.Combine(AvdRoot, name + ".avd", "config.ini");
   if (!File.Exists(file)) throw new Exception("AVD создан без ожидаемой конфигурации. Проверьте журнал.");
   var values = new Dictionary<string, string> { {"avd.id",name}, {"avd.name",name}, {"PlayStore.enabled",image.Contains("google_apis_playstore") ? "yes" : "no"}, {"vm.heapSize","256M"}, {"hw.mainKeys","no"}, {"hw.dPad","no"}, {"hw.trackBall","no"}, {"showDeviceFrame","no"}, {"hw.cpu.ncore", Settings.Cores.ToString()}, {"hw.ramSize", Settings.Memory.ToString()}, {"hw.gpu.enabled", "yes"}, {"hw.gpu.mode", Settings.Gpu}, {"hw.lcd.width", "1080"}, {"hw.lcd.height", "1920"}, {"hw.lcd.density", "420"}, {"hw.keyboard", "yes"} };
   var lines = File.ReadAllLines(file).Where(x => !values.Keys.Any(k => x.StartsWith(k + "="))).ToList();
   lines.AddRange(values.Select(x => x.Key + "=" + x.Value)); File.WriteAllLines(file, lines, new UTF8Encoding(false));
  }
  public async Task Start(Device device, bool headless = false)
  {
   if (device.Starting || device.Serial != null) throw new Exception("Устройство уже запускается или работает.");
   var check = await Runner.Run(Emulator, new[] { "-accel-check" }, Settings.Sdk, AvdRoot, 20000);
   Log(check.Output); if (check.Code != 0) throw new Exception("Аппаратное ускорение недоступно. Включите виртуализацию в BIOS/UEFI и Windows Hypervisor Platform. Подробности в журнале.");
   var launchArgs=new List<string>{ "-avd", device.Name, "-gpu", Settings.Gpu, "-accel", "on", "-no-boot-anim" }; if(headless) launchArgs.Add("-no-window");
   var pi = Runner.Info(Emulator, launchArgs, Settings.Sdk, AvdRoot);
   var process = new Process { StartInfo = pi, EnableRaisingEvents = true };
   process.OutputDataReceived += (s, e) => { if (e.Data != null) Log(e.Data); };
   process.ErrorDataReceived += (s, e) => { if (e.Data != null) Log(e.Data); };
   process.Exited += (s, e) => Log("Эмулятор " + device.Name + " завершён. Код: " + process.ExitCode);
   process.Start(); process.StandardInput.Close(); process.BeginOutputReadLine(); process.BeginErrorReadLine(); Launches[device.Name] = process; device.Starting = true;
  }
 }

 public class Shell
 {
  public Window Window;
  Engine engine = new Engine();
  StackPanel content;
  TextBlock status;
  TextBox journal;
  string page = "home";
  bool busy, compact, preview;
  DispatcherTimer timer;
  static Brush B(string color) { return (Brush)new BrushConverter().ConvertFromString(color); }
  T Find<T>(string name) where T : class { return Window.FindName(name) as T; }
  public Shell()
  {
   using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("MainWindow.xaml")) Window = (Window)XamlReader.Load(stream);
   content = Find<StackPanel>("Content"); status = Find<TextBlock>("Status"); journal = Find<TextBox>("LogBox");
   engine.Log = Log;
   Find<Button>("Close").Click += (s,e) => Window.Close();
   Find<Button>("Minimize").Click += (s,e) => Window.WindowState = WindowState.Minimized;
   Find<Button>("Maximize").Click += (s,e) => Window.WindowState = Window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
   Find<Grid>("TitleBar").MouseLeftButtonDown += (s,e) => { if (e.ClickCount == 2) Window.WindowState = Window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; else Window.DragMove(); };
   var nav = new Dictionary<string,string> { {"HomeNav","home"}, {"DevicesNav","devices"}, {"SdkNav","sdk"}, {"ApkNav","apk"}, {"SettingsNav","settings"}, {"SdkShortcut","sdk"} };
   foreach (var pair in nav) { string p = pair.Value; Find<Button>(pair.Key).Click += (s,e) => Navigate(p); }
   Find<Button>("LogToggle").Click += (s,e) => journal.Visibility = journal.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
   Find<Button>("Cancel").Click += async (s,e) => { try { await Task.Run(()=>Runner.KillTree(engine.Active)); Log("Операция прервана пользователем. Для незавершённой установки повторите команду SDK."); } catch(Exception ex) { Log(ex.Message); } };
   Window.SizeChanged += (s,e) => { var small = Window.ActualWidth < 820; if (compact != small) { compact = small; Find<DockPanel>("Sidebar").Visibility = small ? Visibility.Collapsed : Visibility.Visible; ((ColumnDefinition)Window.FindName("NavColumn")).Width = new GridLength(small ? 0 : 186); Render(); } };
   System.Windows.Shell.WindowChrome.SetWindowChrome(Window,new System.Windows.Shell.WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(7), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(42), UseAeroCaptionButtons = false });
   Window.Loaded += async (s,e) => { Render(); if(!preview) await Work(async () => { await engine.Refresh(); if(File.Exists(engine.Emulator)) await engine.Catalog(false); }, "Устройства и установленные образы обновлены"); };
   timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
   timer.Tick += async (s,e) => { if (!busy && engine.Devices.Any(x => x.Starting || x.Serial != null)) await Work(async () => await engine.Refresh(), "Статус устройств обновлён", false); };
   timer.Start(); Window.Closed += (s,e) => timer.Stop();
   Window.Closing += (s,e) => { if(busy) try { Runner.KillTree(engine.Active); } catch {} };
  }
  void Log(string value)
  {
   if (string.IsNullOrWhiteSpace(value)) return;
   Window.Dispatcher.BeginInvoke(new Action(() => { journal.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + value.Trim() + "\n"); if (journal.Text.Length > 90000) journal.Text = journal.Text.Substring(journal.Text.Length - 60000); journal.ScrollToEnd(); try { File.AppendAllText(Path.Combine(engine.Root,"aemu.log"), value + Environment.NewLine, Encoding.UTF8); } catch {} }));
  }
  async Task Work(Func<Task> action, string success, bool redraw = true)
  {
   if (busy) { status.Text = "Дождитесь завершения текущей операции…"; return; }
   busy = true; content.IsEnabled = false; Find<Button>("Cancel").Visibility=Visibility.Visible; status.Text = "Выполняется операция… Подробности в журнале";
   try { await action(); status.Text = success; }
   catch (Exception ex) { Log(ex.ToString()); status.Text = "Не удалось выполнить операцию — откройте журнал"; MessageBox.Show(Window, ex.Message, "Aemu", MessageBoxButton.OK, MessageBoxImage.Information); }
   finally { busy = false; content.IsEnabled = true; Find<Button>("Cancel").Visibility=Visibility.Collapsed; if (redraw) Render(); }
  }
  void Navigate(string target) { if (busy) return; page = target; Render(); }
  TextBlock Text(string text, double size = 14, string color = "#F5F4FF") { return new TextBlock { Text = text, FontSize = size, Foreground = B(color), Margin = new Thickness(0,0,0,10) }; }
  Button Button(string label, Action action, bool primary = false)
  {
   var button = new Button { Content = label }; if (primary) button.Style = (Style)Window.FindResource("Primary"); button.Click += (s,e) => action(); return button;
  }
  Border Card(UIElement child, bool accent = false)
  {
   return new Border { Child = child, CornerRadius = new CornerRadius(21), BorderThickness = new Thickness(1), BorderBrush = B(accent ? "#9E82EC" : "#343D5A"), Padding = new Thickness(22), Margin = new Thickness(0,0,14,16), Background = new LinearGradientBrush(B(accent ? "#343052" : "#20273C").ToColor(), B("#171D2D").ToColor(), 65) };
  }
  StackPanel Stack(params UIElement[] items) { var panel = new StackPanel(); foreach(var item in items) panel.Children.Add(item); return panel; }
  WrapPanel Row(params UIElement[] items) { var row = new WrapPanel(); foreach(var item in items) row.Children.Add(item); return row; }
  void Heading(string title, string subtitle) { content.Children.Add(Text(title, compact ? 27 : 34)); content.Children.Add(Text(subtitle,14,"#A5ABC6")); }
  void Section(string title) { var text = Text(title,21); text.FontWeight = FontWeights.SemiBold; text.Margin = new Thickness(0,18,0,16); content.Children.Add(text); }
  void Render()
  {
   content.Children.Clear();
   foreach (var name in new[] { "HomeNav", "DevicesNav", "SdkNav", "ApkNav", "SettingsNav" }) Find<Button>(name).Background = B("Transparent");
   var selected = page == "home" ? "HomeNav" : page == "devices" || page == "create" ? "DevicesNav" : page == "sdk" ? "SdkNav" : page == "apk" ? "ApkNav" : "SettingsNav";
   Find<Button>(selected).Background = B("#494180");
   if (compact) content.Children.Add(Row(Button("⌂",()=>Navigate("home")),Button("Эмуляторы",()=>Navigate("devices")),Button("SDK",()=>Navigate("sdk")),Button("⚙",()=>Navigate("settings"))));
   if (page == "home" || page == "devices") Home();
   if (page == "sdk") Sdk();
   if (page == "settings") SettingsPage();
   if (page == "apk") Apk();
   if (page == "create") NewDevice();
  }
  void Home()
  {
   Heading(page == "home" ? "Ваш Android. На большом экране." : "Мои эмуляторы", "Создавайте, запускайте и настраивайте устройства в одном месте.");
   content.Children.Add(Row(Button("＋  Создать эмулятор",()=>NewDevice(),true), Button("↻  Обновить",async ()=>await Work(async ()=>await engine.Refresh(),"Устройства обновлены"))));
   if (page == "home")
   {
    var hero = new Grid { MinHeight = 155, ClipToBounds = true };
    hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1,GridUnitType.Star) }); hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(compact ? 65 : 155) });
    var copy = Stack(Text("ANDROID / OFFICIAL SDK",10,"#C4B4FF"),Text("Официальные образы.\nНовые возможности.",compact ? 23 : 28),Text("Версии из каталога Google.",13,"#C1C7E0"),Button("Открыть каталог  →",()=>Navigate("sdk")));
    copy.HorizontalAlignment = HorizontalAlignment.Left;
    hero.Children.Add(copy);
    if(compact) { var shade = new LinearGradientBrush(); shade.StartPoint = new Point(0,0); shade.EndPoint = new Point(1,0); shade.GradientStops.Add(new GradientStop(Color.FromArgb(235,15,21,40),0)); shade.GradientStops.Add(new GradientStop(Color.FromArgb(200,15,21,40),0.6)); shade.GradientStops.Add(new GradientStop(Color.FromArgb(40,15,21,40),1)); hero.Background=shade; }
    var heroCard = Card(hero,true);
    using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("android-hero.png")) { var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption=BitmapCacheOption.OnLoad; bitmap.StreamSource=stream; bitmap.EndInit(); bitmap.Freeze(); heroCard.Background = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Right }; }
    content.Children.Add(heroCard);
    Section("Мои эмуляторы");
   }
   if (engine.Devices.Count == 0)
   {
    content.Children.Add(Card(Stack(Text("Начнём с первого устройства",23),Text(File.Exists(engine.Emulator) ? "Установите образ Android и создайте эмулятор. Ваши устройства появятся здесь." : "Подключите Android SDK, затем выберите образ и создайте устройство.",14,"#ADB5CF"),Button(File.Exists(engine.Emulator) ? "Выбрать образ SDK" : "Подключить Android SDK",()=>Navigate(File.Exists(engine.Emulator) ? "sdk" : "settings"),true))));
   }
   else
   {
    var row = new WrapPanel(); double width = compact ? Math.Max(280,Window.ActualWidth-95) : Math.Max(310,(Window.ActualWidth-305)/2);
    foreach (var device in engine.Devices)
    {
     var d = device;
     var body = Stack(Text("▱   " + d.Name,21),Text(d.Image,13,"#B9BED7"),Text(d.Serial != null ? "●  Работает · " + d.Serial : d.Starting ? "◌  Запускается…" : "○  Остановлен",12,d.Serial != null ? "#9AE1BD" : "#C3B2F6"));
     var info = AvdInfo(d.Name); body.Children.Add(Text(info,13,"#ABB3CE"));
     body.Children.Add(Row(Button(d.Serial != null ? "■  Остановить" : d.Starting ? "Запускается…" : "▶  Запустить",async ()=>await Work(async ()=> { if (d.Serial != null) { await engine.Exec(engine.Adb,"-s",d.Serial,"emu","kill"); await engine.Refresh(); } else await engine.Start(d); },"Команда отправлена"),true),Button("Папка",()=>Open(Path.Combine(engine.AvdRoot,d.Name+".avd")))));
     var card = Card(body,true); card.Width = width; row.Children.Add(card);
    }
    content.Children.Add(row);
   }
   if (page == "home")
   {
    content.Children.Add(Row(Button("⇧  Установить APK",()=>Navigate("apk")),Button("▧  Образы SDK",()=>Navigate("sdk")),Button("⚙  Настройки",()=>Navigate("settings"))));
    content.Children.Add(Card(Stack(Text("Обновления и ускорение",20),Text("Поддерживайте инструменты SDK в актуальном состоянии. Перед запуском Aemu проверяет аппаратную виртуализацию.",13,"#ADB5CF"),Row(Button("Проверить обновления",async ()=> { Navigate("sdk"); await Work(async ()=>await engine.Catalog(true),"Каталог Google обновлён"); }),Button("Диагностика",async ()=>await Diagnose())))));
   }
  }
  string AvdInfo(string name)
  {
   string path = Path.Combine(engine.AvdRoot,name+".avd","config.ini"); if (!File.Exists(path)) return "";
   var values = File.ReadAllLines(path); Func<string,string> get = key => { var line = values.LastOrDefault(x => x.StartsWith(key+"=")); return line == null ? "—" : line.Substring(key.Length+1); };
   return get("hw.cpu.ncore") + " ядра · " + get("hw.ramSize") + " МБ RAM · " + get("hw.lcd.width") + " × " + get("hw.lcd.height");
  }
  async Task Diagnose()
  {
   await Work(async ()=> { if (!File.Exists(engine.Emulator)) throw new Exception("Сначала подключите SDK с установленным Android Emulator."); var r = await Runner.Run(engine.Emulator,new[]{"-accel-check"},engine.Settings.Sdk,engine.AvdRoot,20000); Log(r.Output); MessageBox.Show(Window,r.Output,"Аппаратное ускорение · код " + r.Code); },"Проверка ускорения завершена");
  }
  void NewDevice()
  {
   page = "create"; content.Children.Clear(); Heading("Новое устройство","Выберите установленный образ. Имена устройств — латиницей.");
   var name = new TextBox { Text = "Aemu_" + DateTime.Now.ToString("MMdd_HHmm") };
   var images = new ComboBox { ItemsSource = engine.Packages.Where(x => x.Installed).ToList(), SelectedIndex = 0 };
   content.Children.Add(Text("Имя устройства")); content.Children.Add(name); content.Children.Add(Text("Установленный образ Android")); content.Children.Add(images);
   content.Children.Add(Text("Ресурсы нового устройства: " + engine.Settings.Cores + " ядра · " + engine.Settings.Memory + " МБ. Изменить можно в настройках.",13,"#A5ABC6"));
   content.Children.Add(Row(Button("Создать",async ()=> { var image = images.SelectedItem as Package; if (image == null) { Navigate("sdk"); return; } await Work(async ()=> { await engine.Create(name.Text.Trim(),image.Id); page = "devices"; },"Устройство создано"); },true),Button("Загрузить установленные образы",async ()=> { await Work(async ()=>await engine.Catalog(false),"Список образов получен",false); NewDevice(); }),Button("Назад",()=>Navigate("home"))));
  }
  void Sdk()
  {
   Heading("Образы Android SDK","Список стабильных версий загружается из официального SDK. Никаких фиксированных ограничений по версии Android.");
   content.Children.Add(Row(Button("↻  Получить каталог",async ()=>await Work(async ()=>await engine.Catalog(true),"Каталог обновлён"),true),Button("Обновить движок и ADB",async ()=>await Work(async ()=> { await engine.Refresh(); EnsureStopped(); await engine.Install("emulator","platform-tools"); },"Движок и ADB обновлены")),Button("Условия SDK ↗",()=>Licenses())));
   content.Children.Add(Text("Установка может занять несколько минут и несколько ГБ. Доступны образы x86_64. " + (engine.Cli!=null ? "Подключён актуальный Android CLI." : "Подключены классические Command-line Tools."),12,"#A5ABC6"));
   if (engine.Packages.Count == 0) content.Children.Add(Card(Stack(Text("Каталог ещё не загружен",21),Text("Нажмите «Получить каталог». Если SDK не установлен — подключите его в настройках.",14,"#A5ABC6"),Button("Настройки SDK",()=>Navigate("settings")))));
   foreach(var package in engine.Packages.Where(x=>x.Id.EndsWith(";x86_64",StringComparison.Ordinal)))
   {
    var p = package; var body = Stack(Text(p.Id.Replace("system-images;", "").Replace(";", "  /  "),17),Text(p.Installed ? "✓ Установлен" : "Доступен для установки",12,p.Installed ? "#9AE1BD" : "#A5ABC6"));
    if(p.Installed) body.Children.Add(Row(Button("Создать устройство",()=>NewDevice(),true),Button("Обновить образ",async ()=>await Work(async ()=> { await engine.Refresh(); EnsureStopped(); await engine.Install(p.Id); await engine.Catalog(true); },"Образ обновлён"))));
    else body.Children.Add(Button("↓  Установить образ",async ()=>await Work(async ()=> { await engine.Install(p.Id); await engine.Catalog(true); },"Образ установлен"),true));
    content.Children.Add(Card(body));
   }
  }
  void EnsureStopped() { if (engine.Devices.Any(x=>x.Serial != null || x.Starting)) throw new Exception("Остановите устройства перед обновлением движка."); }
  void Licenses()
  {
   if(engine.Cli!=null) { Open("https://developer.android.com/studio/terms"); status.Text="В новом Android CLI отдельная команда --licenses не требуется."; return; }
   try { var pi = Runner.Info(engine.Tool("sdkmanager"),new[]{"--sdk_root="+engine.Settings.Sdk,"--licenses"},engine.Settings.Sdk,engine.AvdRoot); pi.CreateNoWindow = false; pi.RedirectStandardOutput = false; pi.RedirectStandardError = false; pi.RedirectStandardInput = false; pi.StandardOutputEncoding = null; pi.StandardErrorEncoding = null; Process.Start(pi); status.Text = "Прочитайте и примите лицензии в окне SDK, затем повторите установку."; } catch(Exception ex) { MessageBox.Show(Window,ex.Message,"Лицензии SDK"); }
  }
  void SettingsPage()
  {
   Heading("Настройки","Подключение SDK и профиль производительности новых устройств.");
   var sdk = new TextBox { Text = engine.Settings.Sdk }; content.Children.Add(Text("Папка Android SDK")); content.Children.Add(sdk);
   content.Children.Add(Text("Например: C:\\Users\\User\\AppData\\Local\\Android\\Sdk. Внутри: emulator, platform-tools, cmdline-tools/latest.",12,"#A5ABC6"));
   content.Children.Add(Row(Button("Выбрать emulator.exe",()=> { var dialog = new OpenFileDialog { Filter="Android Emulator|emulator.exe",Title="Выберите emulator.exe в папке SDK\\emulator" }; if (dialog.ShowDialog(Window) == true) sdk.Text = Directory.GetParent(Path.GetDirectoryName(dialog.FileName)).FullName; }),Button("Скачать официальный SDK ↗",()=>Open("https://developer.android.com/studio#command-tools"))));
   Section("Производительность");
   var cores = new ComboBox { ItemsSource=new[]{2,4,6,8},SelectedItem=engine.Settings.Cores }; var memory = new ComboBox { ItemsSource=new[]{2048,4096,6144,8192},SelectedItem=engine.Settings.Memory }; var gpu = new ComboBox { ItemsSource=new[]{"auto","host","software"},SelectedItem=engine.Settings.Gpu };
   content.Children.Add(Text("Ядра CPU")); content.Children.Add(cores); content.Children.Add(Text("Память RAM, МБ")); content.Children.Add(memory); content.Children.Add(Text("Графика: auto — выбор движка, host — GPU, software — совместимость")); content.Children.Add(gpu);
   content.Children.Add(Text("Быстрый запуск использует штатные снимки Quick Boot. Изменения CPU/RAM применяются к новым устройствам. Не выделяйте эмулятору все ресурсы ПК.",13,"#A5ABC6"));
   content.Children.Add(Row(Button("Сохранить настройки",async ()=>await Work(async ()=> { string path = sdk.Text.Trim().Trim('"'); Runner.SafeBatch(path); if (!Directory.Exists(path)) throw new Exception("Папка SDK не существует."); if(!string.Equals(Path.GetFullPath(path),engine.Settings.Sdk,StringComparison.OrdinalIgnoreCase)) { await engine.Refresh(); EnsureStopped(); } engine.Settings.Sdk=Path.GetFullPath(path); engine.Settings.Cores=(int)(cores.SelectedItem??4); engine.Settings.Memory=(int)(memory.SelectedItem??4096); engine.Settings.Gpu=(string)(gpu.SelectedItem??"auto"); engine.Save(); await engine.Refresh(); },"Настройки сохранены"),true),Button("Проверить ускорение",async ()=>await Diagnose()),Button("Папка данных",()=>Open(Path.GetFullPath(engine.Root)))));
   Section("Первое подключение"); content.Children.Add(Text("1. Установите Command-line Tools и совместимую Java (JDK), либо используйте SDK Android Studio.\n2. Укажите папку SDK и сохраните настройки.\n3. Откройте «Образы SDK», примите лицензии, установите движок и образ.\n4. Создайте устройство и запустите Android.",14,"#A5ABC6"));
  }
  void Apk()
  {
   Heading("Установка APK","Выберите запущенный эмулятор и установите приложение.");
   var targets = new ComboBox { ItemsSource=engine.Devices.Where(x=>x.Serial!=null).Select(x=>x.Name+" / "+x.Serial).ToList(),SelectedIndex=0 };
   content.Children.Add(Text("Устройство")); content.Children.Add(targets);
   content.Children.Add(Button("↻  Найти устройства",async ()=>await Work(async ()=>await engine.Refresh(),"Список обновлён")));
   content.Children.Add(Button("⇧  Выбрать и установить APK",async ()=> {
    if (targets.SelectedItem==null) { MessageBox.Show(Window,"Сначала запустите эмулятор и обновите список.","Aemu"); return; }
    var dialog = new OpenFileDialog { Filter="Android Package|*.apk",Title="Выберите APK" }; if(dialog.ShowDialog(Window)!=true) return;
    var serial = ((string)targets.SelectedItem).Split('/').Last().Trim();
    await Work(async ()=>await engine.Exec(engine.Adb,"-s",serial,"install","-r",dialog.FileName),"APK установлен");
   },true));
   content.Children.Add(Card(Stack(Text("Ваши приложения — под рукой",21),Text("Установка выполняется официальным ADB. Для замены существующей версии используется install -r с сохранением данных приложения.",14,"#A5ABC6"))));
  }
  void Open(string target) { try { Process.Start(new ProcessStartInfo(target) { UseShellExecute=true }); } catch(Exception ex) { MessageBox.Show(Window,ex.Message,"Aemu"); } }
  public void Screenshot(string file, bool narrow, string targetPage = "home")
  {
   preview=true;
   Task.Run(async ()=> { await engine.Refresh(); if(File.Exists(engine.Emulator)) await engine.Catalog(false); }).GetAwaiter().GetResult();
   page = targetPage;
   if(narrow) { Window.Width=500; Window.Height=960; }
   Window.Show(); Window.UpdateLayout();
   status.Text="SDK подключён · " + engine.Devices.Count + " устройств";
   Window.Dispatcher.Invoke(DispatcherPriority.Render,new Action(()=>{}));
   var image = new RenderTargetBitmap((int)Window.ActualWidth,(int)Window.ActualHeight,96,96,PixelFormats.Pbgra32); image.Render(Window);
   var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using(var stream=File.Create(file)) encoder.Save(stream);
   Window.Close();
  }
 }
 public static class BrushExtensions { public static Color ToColor(this Brush brush) { return ((SolidColorBrush)brush).Color; } }
 public static class Program
 {
  [STAThread] public static int Main(string[] args)
  {
   try
   {
    if(args.Length>0 && args[0]=="--self-test") { SelfTest.Run(args.Length>1 ? args[1] : "test-results.txt"); return 0; }
    if(args.Length>1 && args[0]=="--integration-test") { SelfTest.Integration(args[1]).GetAwaiter().GetResult(); return 0; }
    if(args.Length>0 && args[0]=="--verify-android") { SelfTest.VerifyAndroid().GetAwaiter().GetResult(); return 0; }
    var app = new Application();
    if(args.Length>1 && args[0]=="--screenshot") { var old=new Shell(); old.Screenshot(args[1],args.Length>2 && args[2]=="narrow",args.Length>3 ? args[3] : "home"); return 0; }
    if(args.Length>0 && args[0]=="--legacy") {var old=new Shell();return app.Run(old.Window);}
    var shell=new Auremu.Desktop();
    if(args.Length>1 && args[0]=="--capture") {shell.Screenshot(args[1]);return 0;}
    if(args.Length>0 && args[0]=="--launch")shell.StartOnLoad();
    return app.Run(shell.Window);
   }
   catch(Exception ex) { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"error.log"),ex.ToString()); if(args.Length==0) MessageBox.Show(ex.Message,"Auremu — ошибка запуска"); return 1; }
  }
 }
 public static class SelfTest
 {
  static void Assert(bool ok,string message) { if(!ok) throw new Exception("TEST FAILED: "+message); }
  public static void Run(string output)
  {
   var fixture = "Installed packages:\n system-images;android-35;google_apis;x86_64 | 1 | desc\nAvailable Packages:\n system-images;android-36;google_apis_playstore;x86_64 | 2 | desc\n system-images;android-35;google_apis;x86_64 | 2 | desc\n";
   var packages=Engine.ParsePackages(fixture);
   Assert(packages.Count==2,"catalog deduplication"); Assert(packages[0].Id.Contains("android-36"),"numeric API ordering"); Assert(packages[1].Installed,"installed state survives available section"); Assert(!packages[0].Installed,"available image not installed");
   var modern=Engine.ParsePackages("Installed packages:\n system-images/android-37.0/google_apis_playstore/x86_64 8.0.0 Google Play\nAvailable packages:\n system-images/android-36.1/google_apis/x86_64 4.0.0 Google APIs\n");
   Assert(modern.Count==2 && modern[0].Installed && modern[0].Id=="system-images;android-37.0;google_apis_playstore;x86_64","current Android CLI format and decimal API levels");
   Assert(Runner.Quote("a b")=="\"a b\"","spaces quoted"); Assert(Runner.Quote("a\\")=="\"a\\\\\"","trailing slash escaped"); Assert(Runner.Quote("a\"b")=="\"a\\\"b\"","quote escaped");
   bool rejected=false; try { Runner.SafeBatch("C:\\sdk&echo bad"); } catch { rejected=true; } Assert(rejected,"batch metacharacters rejected");
   var dir=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output)),"fake sdk with spaces"); Directory.CreateDirectory(dir); var bat=Path.Combine(dir,"fake.bat"); File.WriteAllText(bat,"@echo off\r\necho ARG1=%~1\r\necho ARG2=%~2\r\necho AVD=%ANDROID_AVD_HOME%\r\nexit /b 0\r\n");
   var result=Runner.Run(bat,new[]{"system-images;android-36;google_apis;x86_64","space value"},dir,dir,10000).GetAwaiter().GetResult();
   Assert(result.Code==0 && result.Output.Contains("ARG1=system-images;android-36;google_apis;x86_64") && result.Output.Contains("ARG2=space value") && result.Output.Contains("AVD="+dir),"SDK batch integration and environment");
   File.WriteAllText(output,"PASS: catalog parsing, version sorting, installed state, Windows quoting, shell validation, real batch invocation with spaces, isolated AVD environment.\n",Encoding.UTF8);
  }
  public static async Task Integration(string root)
  {
   Directory.CreateDirectory(root);
   var sdk=Path.Combine(root,"SDK with spaces"); var bin=Path.Combine(sdk,"cmdline-tools","latest","bin");
   Directory.CreateDirectory(bin); Directory.CreateDirectory(Path.Combine(sdk,"emulator")); Directory.CreateDirectory(Path.Combine(sdk,"platform-tools"));
   var fixture=Path.Combine(Path.GetDirectoryName(root),"fixture.exe");
   File.Copy(fixture,Path.Combine(sdk,"emulator","emulator.exe")); File.Copy(fixture,Path.Combine(sdk,"platform-tools","adb.exe")); File.Copy(fixture,Path.Combine(bin,"fixture.exe"));
   foreach(var tool in new[]{"sdkmanager","avdmanager"}) File.WriteAllText(Path.Combine(bin,tool+".bat"),"@echo off\r\n\"%~dp0fixture.exe\" "+tool+" %*\r\nexit /b %ERRORLEVEL%\r\n");
   var engine=new Engine(Path.Combine(root,"state")); engine.Settings.Sdk=sdk;
   await engine.Catalog(true); Assert(engine.Packages.Count==2 && engine.Packages[0].Id.Contains("android-37"),"catalog through real child process");
   bool refused=false; try { await engine.Create("Test_Device",engine.Packages[0].Id); } catch { refused=true; } Assert(refused,"cannot create uninstalled image");
   refused=false; try { await engine.Create("bad&name",engine.Packages[1].Id); } catch { refused=true; } Assert(refused,"invalid AVD name rejected");
   await engine.Create("Test_Device",engine.Packages[1].Id);
   var config=File.ReadAllText(Path.Combine(engine.AvdRoot,"Test_Device.avd","config.ini"));
   Assert(config.Contains("hw.ramSize=4096") && !config.Contains("hw.ramSize=512") && config.Contains("hw.cpu.ncore=4"),"profile settings replace defaults");
   Assert(engine.Devices.Count==1 && engine.Devices[0].Serial=="emulator-5554","AVD discovered and matched to ADB serial");
   var d=engine.Devices[0]; refused=false; try { await engine.Start(d); } catch { refused=true; } Assert(refused,"duplicate launch prevented");
   d.Serial=null; await engine.Start(d); Assert(d.Starting && engine.Launches.ContainsKey(d.Name),"acceleration check and launch");
   await engine.Exec(engine.Adb,"-s","emulator-5554","install","-r",Path.Combine(root,"Example app.apk"));
   await engine.Exec(engine.Adb,"-s","emulator-5554","emu","kill");
   refused=false; try { await engine.Exec(engine.Tool("sdkmanager"),"invalid-package"); } catch { refused=true; } Assert(refused,"nonzero tool error propagated");
   var calls=File.ReadAllText(Path.Combine(sdk,"calls.txt"));
   Assert(calls.Contains("-s | emulator-5554 | install | -r | "+Path.Combine(root,"Example app.apk")),"APK target and path preserved");
   Assert(calls.Contains("-s | emulator-5554 | emu | kill"),"stop targets one device");
   engine.Launches[d.Name].WaitForExit(10000);
   File.WriteAllText(Path.Combine(root,"results.txt"),"PASS: SDK discovery, live catalog protocol, create validation, AVD configuration, ADB matching, duplicate-launch prevention, hardware-check/launch flow, targeted APK install/stop, nonzero exit propagation. Fixture tools only; real Android boot not tested.\n");
  }
  public static async Task VerifyAndroid()
  {
   var engine=new Engine(); string report=Path.Combine(engine.Root,"android-verification.txt");
   File.WriteAllText(report,"Real Android verification started: "+DateTime.Now.ToString("s")+"\n");
   object gate=new object(); engine.Log=value=> { lock(gate) File.AppendAllText(report,value+"\n"); };
   engine.Save(); await engine.Catalog(false); await engine.Refresh();
   const string name="Auremu_Android_17"; const string image="system-images;android-37.0;google_apis_playstore;x86_64";
   if(!engine.Devices.Any(x=>x.Name==name)) await engine.Create(name,image);
   engine.Configure(name,image);
   var device=engine.Devices.First(x=>x.Name==name); await engine.Start(device,true); string serial=null;
   try
   {
    bool ready=false; var clock=Stopwatch.StartNew();
    while(clock.Elapsed < TimeSpan.FromMinutes(4))
    {
     await Task.Delay(4000); await engine.Refresh(); var current=engine.Devices.FirstOrDefault(x=>x.Name==name); if(current==null || current.Serial==null) continue;
     serial=current.Serial; if((await engine.Exec(engine.Adb,"-s",serial,"shell","getprop","sys.boot_completed")).Trim()=="1") { ready=true; break; }
    }
    Assert(ready,"real Android boot completed within four minutes");
    var release=await engine.Exec(engine.Adb,"-s",serial,"shell","getprop","ro.build.version.release"); var api=await engine.Exec(engine.Adb,"-s",serial,"shell","getprop","ro.build.version.sdk");
    var apkPaths=await engine.Exec(engine.Adb,"-s",serial,"shell","pm","path","com.android.egg");
    var apkPath=apkPaths.Split('\n').Select(x=>x.Trim()).FirstOrDefault(x=>x.StartsWith("package:")); Assert(apkPath!=null,"official Easter Egg APK available for install test");
    var apkLocal=Path.GetFullPath(Path.Combine(engine.Root,"Android Easter Egg.apk"));
    await engine.Exec(engine.Adb,"-s",serial,"pull",apkPath.Substring(8),apkLocal);
    Assert((await engine.Exec(engine.Adb,"-s",serial,"install","-r",apkLocal)).Contains("Success"),"real APK installation");
    await engine.Exec(engine.Adb,"-s",serial,"shell","am","start","-a","android.intent.action.MAIN","-c","android.intent.category.HOME"); await Task.Delay(10000);
    using(var shot=new Process { StartInfo=Runner.Info(engine.Adb,new[]{"-s",serial,"exec-out","screencap","-p"},engine.Settings.Sdk,engine.AvdRoot) })
    { shot.Start(); shot.StandardInput.Close(); var error=shot.StandardError.ReadToEndAsync(); using(var output=File.Create(Path.Combine(engine.Root,"android-screen.png"))) await shot.StandardOutput.BaseStream.CopyToAsync(output); shot.WaitForExit(); Assert(shot.ExitCode==0,"real screenshot captured"); await error; }
    File.AppendAllText(report,"\nPASS: REAL ANDROID BOOT AND APK INSTALL. Release="+release.Trim()+", API="+api.Trim()+", elapsed="+clock.Elapsed.TotalSeconds.ToString("F1",CultureInfo.InvariantCulture)+"s including install and UI wait. Not a benchmark.\n");
   }
   finally
   {
    if(serial!=null) engine.Exec(engine.Adb,"-s",serial,"emu","kill").GetAwaiter().GetResult();
    else { Process p; if(engine.Launches.TryGetValue(name,out p) && !p.HasExited) p.Kill(); }
   }
  }
 }
}
