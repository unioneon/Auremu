using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Auremu;
public static class ApprovedDesignCheck {
 static void Assert(bool value,string message){if(!value)throw new Exception(message);}
 public static void Main(string[] args){
  using(var sha=SHA256.Create())using(var embedded=typeof(ApprovedDesign).Assembly.GetManifestResourceStream("ApprovedLayout.json"))using(var expected=File.OpenRead(args[0]))Assert(sha.ComputeHash(embedded).SequenceEqual(sha.ComputeHash(expected)),"Build does not contain the approved design");
  var design=ApprovedDesign.Load();
  foreach(string type in new[]{"mobile","tablet"}){
   var light=design.Player(type,"light");var dark=design.Player(type,"dark");Assert(light.w==dark.w&&light.h==dark.h,"Themes have different window sizes");
   foreach(var n in light.nodes){var d=dark.Node(n.id);Assert(n.x==d.x&&n.y==d.y&&n.w==d.w&&n.h==d.h,"Themes have different node geometry: "+n.id);}
   foreach(var n in light.nodes)Assert(n.x>=0&&n.y>=0&&n.x+n.w<=light.w&&n.y+n.h<=light.h,"Approved control exceeds frame: "+n.id);
  }
  var tablet=design.Player("tablet","light");for(int i=0;i<12;i++){var n=tablet.Node("tool-"+i);Assert(n.x==1280&&n.w==32&&n.h==32,"Tablet button column differs from approved layout");if(i>0&&i!=9)Assert(n.y-tablet.Node("tool-"+(i-1)).y==36,"Tablet button spacing differs");}
  Assert(design.Player("mobile","light").Node("brand").w==72,"User logo dimensions lost");
  Console.WriteLine("PASS: approved resource matches source, user geometry preserved, theme parity, aligned tablet buttons and in-frame controls.");
 }
}
