using System;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task SaveOnexOwnedFocusCropAsync(Control control,string filename)
  {
   var scope=_onexControlLayers.TryGetValue(control,out var found)?found:_onexMainFocusLayer;
   if(scope==null||!control.IsLoaded)throw new InvalidOperationException("Owned focus capture has no live scope");
   RefreshOnexFocusLayer(scope);await Task.Delay(35);
   // This legacy diagnostic captures XAML only. Target-local composition
   // adorners require a real compositor/window capture for pixel evidence.
   var bitmap=new RenderTargetBitmap();await bitmap.RenderAsync(scope.Host);
   var rect=control.TransformToVisual(scope.Host).TransformBounds(new Rect(0,0,control.ActualWidth,control.ActualHeight));
   double scaleX=bitmap.PixelWidth/scope.Host.ActualWidth,scaleY=bitmap.PixelHeight/scope.Host.ActualHeight;
   int left=Math.Max(0,(int)Math.Floor(rect.Left*scaleX)-2),top=Math.Max(0,(int)Math.Floor(rect.Top*scaleY)-2);
   int right=Math.Min(bitmap.PixelWidth,(int)Math.Ceiling(rect.Right*scaleX)+2),bottom=Math.Min(bitmap.PixelHeight,(int)Math.Ceiling(rect.Bottom*scaleY)+2);
   if(right<=left||bottom<=top)throw new InvalidOperationException("Owned focus capture is outside viewport");
   var source=(await bitmap.GetPixelsAsync()).ToArray();int width=right-left,height=bottom-top;var crop=new byte[width*height*4];
   for(int row=0;row<height;row++)Buffer.BlockCopy(source,((top+row)*bitmap.PixelWidth+left)*4,crop,row*width*4,width*4);
   var file=await ApplicationData.Current.LocalFolder.CreateFileAsync(filename,CreationCollisionOption.ReplaceExisting);
   using(var stream=await file.OpenAsync(FileAccessMode.ReadWrite)){var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,stream);encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,(uint)width,(uint)height,96,96,crop);await encoder.FlushAsync();}
  }
 }
}
