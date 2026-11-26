// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexLogoAsync(JsonObject report)
        {
            var images=new JsonArray();
            async Task Check(RandomAccessStreamReference logo,string name)
            {
                using(var source=await logo.OpenReadAsync())
                {
                    var target=await ApplicationData.Current.LocalFolder.CreateFileAsync(name,CreationCollisionOption.ReplaceExisting);
                    using(var output=await target.OpenAsync(FileAccessMode.ReadWrite))await RandomAccessStream.CopyAsync(source,output);
                    source.Seek(0);var decoder=await BitmapDecoder.CreateAsync(source);var data=await decoder.GetPixelDataAsync(BitmapPixelFormat.Rgba8,BitmapAlphaMode.Straight,new BitmapTransform(),ExifOrientationMode.IgnoreExifOrientation,ColorManagementMode.DoNotColorManage);var pixels=data.DetachPixelData();int orange=0,violet=0,cyan=0,minX=(int)decoder.PixelWidth,maxX=-1,minY=(int)decoder.PixelHeight,maxY=-1;
                    for(int i=0;i<pixels.Length;i+=4){bool o=pixels[i]>160&&pixels[i+1]<150&&pixels[i+2]<100,v=pixels[i]>60&&pixels[i]<220&&pixels[i+1]<140&&pixels[i+2]>150;if(o)orange++;if(pixels[i]<100&&pixels[i+1]>120&&pixels[i+2]>120)cyan++;if(v)violet++;if((o||v)&&pixels[i+3]>20){int x=i/4%(int)decoder.PixelWidth,y=i/4/(int)decoder.PixelWidth;minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);}}
                    if(orange<2||violet<2||cyan>0)throw new InvalidOperationException("Windows returned the old logo: "+name);
                    double occupancy=(maxX-minX+1)/(double)decoder.PixelWidth;if(occupancy<.86||occupancy>.94)throw new InvalidOperationException("Incorrect icon glyph occupancy: "+occupancy);
                    images.Add(new JsonObject{{"file",JsonValue.CreateStringValue(name)},{"width",JsonValue.CreateNumberValue(decoder.PixelWidth)},{"height",JsonValue.CreateNumberValue(decoder.PixelHeight)},{"orangePixels",JsonValue.CreateNumberValue(orange)},{"violetPixels",JsonValue.CreateNumberValue(violet)},{"oldCyanPixels",JsonValue.CreateNumberValue(cyan)},{"glyphWidthFraction",JsonValue.CreateNumberValue(occupancy)},{"glyphHeightFraction",JsonValue.CreateNumberValue((maxY-minY+1)/(double)decoder.PixelHeight)}});
                }
            }
            var app=(await Package.Current.GetAppListEntriesAsync()).Single(x=>x.AppUserModelId.EndsWith("!App",StringComparison.Ordinal));
            await Check(app.DisplayInfo.GetLogo(new Size(16,16)),"onex-registered-logo-16.png");await Check(app.DisplayInfo.GetLogo(new Size(44,44)),"onex-registered-logo-44.png");
            var manifest=await FileIO.ReadTextAsync(await Package.Current.InstalledLocation.GetFileAsync("AppxManifest.xml"));
            if(!manifest.Contains("Id=\"GamingWidget\"")||!manifest.Contains("OnexSquare44x44Logo"))throw new InvalidOperationException("Game Bar extension/brand assets missing from manifest");
            report["registeredAppLogos"]=images;report["gameBarExtensionPackaged"]=JsonValue.CreateBooleanValue(true);report["packageVersion"]=JsonValue.CreateStringValue(Package.Current.Id.Version.Major+"."+Package.Current.Id.Version.Minor+"."+Package.Current.Id.Version.Build+"."+Package.Current.Id.Version.Revision);
        }
    }
}
