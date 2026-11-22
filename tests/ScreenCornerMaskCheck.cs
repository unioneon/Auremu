using System;
using Auremu;
public static class ScreenCornerMaskCheck {
 static void Check(bool value,string label){if(!value)throw new Exception(label);}
 public static void Main(){
  foreach(int radius in new[]{24,32,40,48}){
   int w=380,h=674;var mask=ScreenCornerMask.Render(w,h,radius,246,244,255);int partial=0;
   Check(mask[3]==255,"Outer corner must cover video");Check(mask[((h/2)*w+w/2)*4+3]==0,"Screen centre must remain transparent");
   for(int y=0;y<h;y++)for(int x=0;x<w;x++){
    int p=(y*w+x)*4,a=mask[p+3];if(a>0&&a<255)partial++;
    Check(mask[p]<=a&&mask[p+1]<=a&&mask[p+2]<=a,"RGB must be premultiplied to avoid dark fringes");
    Check(a==mask[(y*w+w-1-x)*4+3]&&a==mask[((h-1-y)*w+x)*4+3],"All four corners must have equal coverage");
   }
   Check(partial>0,"Curves must include fractional alpha for antialiasing");
   var dark=ScreenCornerMask.Render(w,h,radius,25,25,47);
   for(int p=3;p<mask.Length;p+=4)Check(dark[p]==mask[p],"Theme changes must preserve the edge shape");
  }
  Console.WriteLine("PASS: smooth fractional edges, symmetric corners, premultiplied pixels, transparent centre, light/dark geometry.");
 }
}
