// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexNavInkAsync(JsonObject report)
        {
            var geometry=new JsonArray();report["inkGeometry"]=geometry;
            bool centered=true;int index=0;
            try
            {
                foreach(var tab in GetVisibleNavigationItems())
                {
                    foreach(bool selected in new[]{true,false})
                    {
                        tab.IsChecked=selected;await Task.Delay(180);tab.UpdateLayout();
                        var selection=(Border)FindDescendantByName(tab,"SelectionBackground");
                        var focus=FindOnexNavFocusBorder(tab);
                        double selectionOpacity=selection.Opacity,focusOpacity=focus.Opacity;
                        string name="onex-nav-ink-"+index+(selected?"-on":"-off")+".png";
                        try
                        {
                            // Capture actual glyph ink, excluding both decorative frames.
                            selection.Opacity=0;focus.Opacity=0;
                            await SaveOnexImage(name,tab);
                        }
                        finally{selection.Opacity=selectionOpacity;focus.Opacity=focusOpacity;}
                        var file=await ApplicationData.Current.LocalFolder.GetFileAsync(name);
                        using(var stream=await file.OpenReadAsync())
                        {
                            var decoder=await BitmapDecoder.CreateAsync(stream);
                            var data=await decoder.GetPixelDataAsync(BitmapPixelFormat.Rgba8,BitmapAlphaMode.Straight,new BitmapTransform(),ExifOrientationMode.IgnoreExifOrientation,ColorManagementMode.DoNotColorManage);
                            byte[] pixels=data.DetachPixelData();int w=(int)decoder.PixelWidth,h=(int)decoder.PixelHeight,minX=w,minY=h,maxX=-1,maxY=-1;
                            for(int y=0;y<h;y++)for(int x=0;x<w;x++)if(pixels[(y*w+x)*4+3]>20){minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);}
                            if(maxX<0||maxY<0)throw new InvalidOperationException("Empty tab glyph: "+tab.Tag);
                            double dx=(minX+maxX+1-w)/2d,dy=(minY+maxY+1-h)/2d;
                            centered&=Math.Abs(dx)<=.51&&Math.Abs(dy)<=.51;
                            geometry.Add(new JsonObject{{"tab",JsonValue.CreateStringValue(tab.Tag.ToString())},{"selected",JsonValue.CreateBooleanValue(selected)},{"pixelWidth",JsonValue.CreateNumberValue(w)},{"pixelHeight",JsonValue.CreateNumberValue(h)},{"minX",JsonValue.CreateNumberValue(minX)},{"maxX",JsonValue.CreateNumberValue(maxX)},{"minY",JsonValue.CreateNumberValue(minY)},{"maxY",JsonValue.CreateNumberValue(maxY)},{"dxPixels",JsonValue.CreateNumberValue(dx)},{"dyPixels",JsonValue.CreateNumberValue(dy)},{"pixelsPerDip",JsonValue.CreateNumberValue(w/tab.ActualWidth)}});
                        }
                        if(selected)await SaveOnexImage("onex-nav-selected-"+index+".png",MainNavPanel);
                    }
                    index++;
                }
                report["allEightInkCentered"]=JsonValue.CreateBooleanValue(centered&&index==8);
                report["alphaThreshold"]=JsonValue.CreateNumberValue(20);
                if(!centered||index!=8)throw new InvalidOperationException("Visible tab glyph ink is not centered within half a physical pixel");
            }
            finally{PerformanceNavItem.IsChecked=true;FocusFirstTabContentElement();}
        }
    }
}
