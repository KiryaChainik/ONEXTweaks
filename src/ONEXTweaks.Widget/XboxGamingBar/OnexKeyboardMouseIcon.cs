// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private static readonly string[] OnexKeyboardMouseGlyphPaths={
   "F0 M4 5 H22 Q23.4 5 24 6.5 C19.2 6.5 16.2 9.8 16.2 14.2 V20.7 C16.2 23.3 17.3 25.6 19.2 27.2 H4 C2.34 27.2 1 25.86 1 24.2 V8 C1 6.34 2.34 5 4 5 Z M4 8.5 H7 V12 H4 Z M8.5 8.5 H11.5 V12 H8.5 Z M13 8.5 H16 V12 H13 Z M4 14 H7 V17.5 H4 Z M8.5 14 H11.5 V17.5 H8.5 Z M13 14 H15.2 V17.5 H13 Z M4 20 H7 V23.5 H4 Z M8.5 20 H15.2 V23.5 H8.5 Z",
   "F0 M17.2 14.5 V14 C17.2 10.46 20.06 7.6 23.6 7.6 C27.14 7.6 30 10.46 30 14 V14.5 Z M23.6 9.1 C24.26 9.1 24.8 9.64 24.8 10.3 V12.7 C24.8 13.36 24.26 13.9 23.6 13.9 C22.94 13.9 22.4 13.36 22.4 12.7 V10.3 C22.4 9.64 22.94 9.1 23.6 9.1 Z",
   "M17.2 15.5 H30 V20.7 C30 24.29 27.13 27.2 23.6 27.2 C20.07 27.2 17.2 24.29 17.2 20.7 Z"
  };
  private Viewbox CreateOnexKeyboardMouseNavIcon(RadioButton owner)
  {
   // Filled overlapping silhouettes follow the owner's keyboard+mouse reference.
   // Geometry stays inside32x32; tint remains owned bythe navigation theme.
   var canvas=new Canvas{Name="OnexKeyboardMouseGlyphCanvas",Width=32,Height=32};
   void Add(string data){
    var shape=(Path)Windows.UI.Xaml.Markup.XamlReader.Load("<Path xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Data=\""+data+"\" />");
    shape.Width=32;shape.Height=32;shape.Stretch=Stretch.None;shape.StrokeThickness=0;Canvas.SetLeft(shape,0);Canvas.SetTop(shape,0);
    shape.SetBinding(Shape.FillProperty,new Binding{Source=owner,Path=new PropertyPath("Foreground")});canvas.Children.Add(shape);
   }
   foreach(string data in OnexKeyboardMouseGlyphPaths)Add(data);
   return new Viewbox{Name="OnexKeyboardMouseNavArtwork",Width=24,Height=24,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Child=canvas};
  }

  private async Task VerifyOnexKeyboardMouseIconAsync(JsonObject report)
  {
   int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new System.InvalidOperationException(why);}
   var original=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);EnsureOnexKeyboardMouseFrontend();
   try{
    var navigation=GetVisibleNavigationItems();Check(navigation.IndexOf(_onexKeyboardMouseNav)==navigation.IndexOf(LegionNavItem)+1,"Keyboard/mouse tab order changed");Check(_onexKeyboardMouseNav.Content is Viewbox,"Keyboard/mouse artwork has no bounded Viewbox");var icon=(Viewbox)_onexKeyboardMouseNav.Content;var canvas=icon.Child as Canvas;Check(icon.Width==24&&icon.Height==24&&canvas!=null&&canvas.Width==32&&canvas.Height==32,"Keyboard/mouse icon viewport invalid");
    Check(canvas.Children.Count==3&&canvas.Children.OfType<Path>().Count()==3,"Filled keyboard/overlappingmouse layers missing");
    foreach(var shape in canvas.Children.OfType<Path>()){var bounds=shape.Data.Bounds;Check(bounds.X>=0&&bounds.Y>=0&&bounds.Right<=32&&bounds.Bottom<=32&&shape.StrokeThickness==0,"Keyboard/mouse silhouette clipped");}

    _onexKeyboardMouseNav.IsChecked=true;ApplyOnexTabPalette();UpdateLayout();await Task.Delay(140);Check(icon.ActualWidth<=24.1&&icon.ActualHeight<=24.1,"Keyboard/mouse icon grew past navigation viewport");Check(_onexKeyboardMouseNav.ActualWidth==38&&_onexKeyboardMouseNav.ActualHeight==38,"Tab frame size changed");
    var art=icon.TransformToVisual(_onexKeyboardMouseNav).TransformBounds(new Windows.Foundation.Rect(0,0,icon.ActualWidth,icon.ActualHeight));Check(System.Math.Abs(art.X+art.Width/2-_onexKeyboardMouseNav.ActualWidth/2)<1&&System.Math.Abs(art.Y+art.Height/2-_onexKeyboardMouseNav.ActualHeight/2)<1,"Keyboard/mouse artwork not centred in tab frame");
    await SaveOnexImage("onex-kbm328-selected.png",_onexKeyboardMouseNav,80);LegionNavItem.IsChecked=true;ApplyOnexTabPalette();await Task.Delay(140);Check(!_onexKeyboardMouseNav.IsChecked.GetValueOrDefault()&&(!OnexBrandTheme||icon.Opacity<1),"Inactive keyboard/mouse artwork not dimmed");await SaveOnexImage("onex-kbm328-idle.png",_onexKeyboardMouseNav,80);
    var inkBitmap=new Windows.UI.Xaml.Media.Imaging.RenderTargetBitmap();await inkBitmap.RenderAsync(_onexKeyboardMouseNav);var buffer=await inkBitmap.GetPixelsAsync();var bytes=new byte[buffer.Length];using(var reader=Windows.Storage.Streams.DataReader.FromBuffer(buffer))reader.ReadBytes(bytes);
    int minX=inkBitmap.PixelWidth,maxX=-1,minY=inkBitmap.PixelHeight,maxY=-1;
    for(int y=0;y<inkBitmap.PixelHeight;y++)for(int x=0;x<inkBitmap.PixelWidth;x++){int n=(y*inkBitmap.PixelWidth+x)*4;int b=bytes[n],g=bytes[n+1],r=bytes[n+2],alpha=bytes[n+3];if(alpha<30||r<80||System.Math.Abs(r-g)>20||System.Math.Abs(g-b)>20)continue;minX=System.Math.Min(minX,x);maxX=System.Math.Max(maxX,x);minY=System.Math.Min(minY,y);maxY=System.Math.Max(maxY,y);}
    double inkWidth=(maxX-minX+1)*_onexKeyboardMouseNav.ActualWidth/inkBitmap.PixelWidth;
    Check(maxX>=minX&&inkWidth>=20.5&&inkWidth<=24.5,"Actual visiblekeyboard/mouse ink isclipped: "+inkWidth);
    report["visibleInkWidthDip"]=JsonValue.CreateNumberValue(inkWidth);
    await SaveOnexImage("onex-kbm328-tabs.png",_onexKeyboardMouseNav.Parent as FrameworkElement,80);
    report["checks"]=JsonValue.CreateNumberValue(checks);report["vectorShapes"]=JsonValue.CreateNumberValue(canvas.Children.Count);report["bounded24pxArtwork"]=JsonValue.CreateBooleanValue(true);report["selectedAndInactiveRendered"]=JsonValue.CreateBooleanValue(true);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);report["settingsWrites"]=JsonValue.CreateNumberValue(0);
   }finally{if(original!=null)original.IsChecked=true;}
  }
 }
}
