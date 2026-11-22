using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Auremu {
 public sealed class DesignNode {
  public string id,kind,name,text,icon,fill,color;
  public double x,y,w,h,radius,fontSize,iconSize;
 }
 public sealed class DesignFrame {
  public string id;public double w,h,radius;public List<DesignNode> nodes;
  public DesignNode Node(string id){return nodes.First(n=>n.id==id);}
 }
 public sealed class ApprovedDesign {
  public List<DesignFrame> frames;
  public static ApprovedDesign Load(){using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("ApprovedLayout.json")){if(stream==null)throw new Exception("Approved Auremu layout is missing.");using(var reader=new StreamReader(stream))return new JavaScriptSerializer().Deserialize<ApprovedDesign>(reader.ReadToEnd());}}
  public DesignFrame Player(string mode,string theme){return frames.First(f=>f.id==(mode=="mobile"?"mobile":"tablet")+"-"+theme);}
 }
 public sealed partial class Desktop {
  readonly ApprovedDesign approvedDesign=ApprovedDesign.Load();
  Canvas approvedCanvas;
  DesignFrame approvedFrame;
  readonly Dictionary<string,FrameworkElement> approvedElements=new Dictionary<string,FrameworkElement>();
  DesignFrame PlayerDesign {get{return approvedDesign.Player(runtime.Engine.Settings.Form,Dark?"dark":"light");}}

  void BuildApprovedPlayer(){
   player=new Grid{Visibility=Visibility.Collapsed};approvedCanvas=new Canvas{ClipToBounds=true,Background=Brushes.Transparent};player.Children.Add(approvedCanvas);
   display=new Grid();loadText=Text("Android is starting…",18,true);loadText.Margin=new Thickness(16);loadText.HorizontalAlignment=HorizontalAlignment.Center;loadText.VerticalAlignment=VerticalAlignment.Center;
   display.Children.Add(loadText);host.Visibility=Visibility.Collapsed;host.HorizontalAlignment=HorizontalAlignment.Center;host.VerticalAlignment=VerticalAlignment.Center;display.Children.Add(host);approvedCanvas.Children.Add(display);
   display.SizeChanged+=(s,e)=>FitDisplay();player.SizeChanged+=(s,e)=>LayoutApprovedPlayer();
   approvedCanvas.MouseLeftButtonDown+=(s,e)=>{if(e.GetPosition(approvedCanvas).Y>=64||Ancestor<Button>(e.OriginalSource as DependencyObject)!=null)return;if(e.ClickCount==2)ToggleMaximize();else Window.DragMove();};
  }
  void ToggleMaximize(){Window.WindowState=Window.WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;}
  Brush DesignBrush(string token){
   if(string.IsNullOrEmpty(token)||token=="transparent")return Brushes.Transparent;
   switch(token){case"$surface":return Resource("Surface");case"$card":return Resource("Card");case"$soft":return Resource("Soft");case"$ink":return Resource("Ink");case"$muted":return Resource("Muted");case"$icon":return Resource("Icon");case"$accent":return Resource("Accent");case"$accenttext":return Brush("#EDE8FF");default:return Brush(token);}
  }
  void BuildApprovedChrome(){
   approvedFrame=PlayerDesign;
   foreach(var element in approvedElements.Values)approvedCanvas.Children.Remove(element);approvedElements.Clear();
   foreach(var n in approvedFrame.nodes){
    if(n.id=="android")continue;
    FrameworkElement element;
    if(n.kind=="brand"){element=(FrameworkElement)Logo();element.Margin=new Thickness(0);}
    else if(n.kind=="rect")element=new Border{CornerRadius=new CornerRadius(n.radius),Background=DesignBrush(n.fill)};
    else if(n.id=="metrics"){
     stats=Text("",n.fontSize>0?n.fontSize:12,true);stats.TextAlignment=TextAlignment.Right;stats.VerticalAlignment=VerticalAlignment.Center;stats.Margin=new Thickness(0);stats.TextTrimming=TextTrimming.CharacterEllipsis;element=stats;
    }else if(n.id=="version"){
     var label=new TextBlock{Text=n.text,FontSize=n.fontSize,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,Foreground=DesignBrush(n.color)};
     element=new Border{CornerRadius=new CornerRadius(n.radius),Background=DesignBrush(n.fill),Child=label};
    }else{
     string id=n.id;object content=string.IsNullOrEmpty(n.icon)?(object)n.text:string.IsNullOrEmpty(n.text)?Icon(DesignIcon(n),n.iconSize):Label(DesignIcon(n),n.text,n.iconSize);
     var button=Btn(content,async()=>await ApprovedAction(id),n.fill=="$accent");button.Padding=new Thickness(0);button.Margin=new Thickness(0);button.Background=DesignBrush(n.fill);button.Foreground=DesignBrush(n.color);button.FontSize=n.fontSize>0?n.fontSize:13;button.ToolTip=n.name;
     element=button;
    }
    element.Width=n.w;element.Height=n.h;element.ToolTip=n.name;approvedElements.Add(n.id,element);approvedCanvas.Children.Add(element);
   }
   LayoutApprovedPlayer();
  }
  string DesignIcon(DesignNode n){
   switch(n.icon){case"smartphone":return"phone";case"layout-grid":return"grid";case"minus":return"minimize";case"square":return n.id=="maximize"?"maximize":"recents";case"x":return"close";case"volume-2":return"volume";case"rotate-cw":return"rotate";case"map-pin":return"gps";case"triangle":return"back";case"circle":return"home";default:return n.icon;}
  }
  async System.Threading.Tasks.Task ApprovedAction(string id){
   switch(id){
    case"mode-phone":await SetMode("mobile");break;
    case"mode-tablet":await SetMode("tablet");break;
    case"manager-return":case"tool-8":ShowManager();break;
    case"theme":await Work(async()=>{await runtime.SetTheme(Dark?"light":"dark");BuildHeader();},"Theme changed");break;
    case"minimize":Window.WindowState=WindowState.Minimized;break;
    case"maximize":ToggleMaximize();break;
    case"close":Window.Close();break;
    case"tool-0":host.FocusAndroid();break;
    case"tool-1":VolumeDialog();break;
    case"tool-2":await SetMode(runtime.Engine.Settings.Form=="mobile"?"tablet":"mobile");break;
    case"tool-3":await Capture();break;
    case"tool-4":await Record();break;
    case"tool-5":await InstallApk();break;
    case"tool-6":await Transfer();break;
    case"tool-7":Gps();break;
    case"tool-9":case"tool-10":case"tool-11":if(runtime.Ready){string key=id=="tool-9"?"4":id=="tool-10"?"3":"187";await Work(async()=>await runtime.Adb("shell","input","keyevent",key),"Android");}break;
   }
  }
  void LayoutApprovedPlayer(){
   if(approvedCanvas==null||approvedFrame==null||!isPlayer||player.ActualWidth<1||player.ActualHeight<1)return;
   var frame=approvedFrame;double dw=player.ActualWidth-frame.w,dh=player.ActualHeight-frame.h;
   var screen=frame.Node("android");display.Width=Math.Max(1,screen.w+dw);display.Height=Math.Max(1,screen.h+dh);Canvas.SetLeft(display,screen.x);Canvas.SetTop(display,screen.y);
   foreach(var n in frame.nodes){FrameworkElement element;if(!approvedElements.TryGetValue(n.id,out element))continue;double x=n.x,y=n.y;
    if(n.id.StartsWith("tool-")){x+=dw;int i=int.Parse(n.id.Substring(5));y+=frame.id.StartsWith("tablet")&&i>=9?dh:dh/2;}
    else if(new[]{"metrics","theme","minimize","maximize","close"}.Contains(n.id))x+=dw;
    Canvas.SetLeft(element,x);Canvas.SetTop(element,y);
   }
   FitDisplay();
  }
 }
}
