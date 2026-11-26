// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Media;
using Windows.Graphics.Display;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexBrightnessDimAsync(JsonObject report)
        {
            var folder=ApplicationData.Current.LocalFolder;var before=await ReadOnexMediaAsync();string run=Guid.NewGuid().ToString("N");int step=0;
            async Task Set(int value){var reply=await App.SendMessageAsync(new ValueSet{{"SetBrightnessLevel",value},{"OnexMediaUserInput",true}});if(reply==null||reply.ContainsKey("Error"))throw new InvalidOperationException("Brightness setup failed");ApplyOnexMediaLevels((string)reply["MediaLevels"]);}
            async Task Wait(bool dimmed,int value)
            {for(int n=0;n<60;n++){await Task.Delay(100);var current=await ReadOnexMediaAsync();ApplyOnexMediaLevels(current.Stringify());if(current.GetNamedBoolean("brightnessDimmed")==dimmed&&current.GetNamedNumber("brightness")==value&&!_onexBrightnessDimBusy)return;}throw new InvalidOperationException("Brightness dim state not confirmed");}
            async Task Click(bool icon,Func<bool> expected)
            {
                string id=run+":"+(++step);double scale=DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel;var nav=new RadioButtonAutomationPeer(PerformanceNavItem).GetBoundingRectangle();var relative=icon?RailBrightnessIcon.TransformToVisual(PerformanceNavItem).TransformBounds(new Rect(0,0,RailBrightnessIcon.ActualWidth,RailBrightnessIcon.ActualHeight)):new Rect(0,0,PerformanceNavItem.ActualWidth,PerformanceNavItem.ActualHeight);
                var data=new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"button",JsonValue.CreateStringValue("focus")},{"x",JsonValue.CreateNumberValue(nav.X+(relative.X+relative.Width/2)*scale+widget.WindowBounds.X*scale)},{"y",JsonValue.CreateNumberValue(nav.Y+(relative.Y+relative.Height/2)*scale+widget.WindowBounds.Y*scale)}};
                for(int n=0;;n++){try{System.IO.File.WriteAllText(System.IO.Path.Combine(folder.Path,"onex-gamepad-qa-state.json"),data.Stringify());break;}catch(System.IO.IOException){if(n>=20)throw;await Task.Delay(25);}}
                bool ok=false;for(int n=0;n<70;n++){await Task.Delay(100);try{if(await FileIO.ReadTextAsync(await folder.GetFileAsync("onex-gamepad-qa-ack.txt"))==id&&expected()){ok=true;break;}}catch{}}if(!ok)throw new InvalidOperationException("Actual brightness icon click failed at "+step);
            }
            try
            {
                if(!before.GetNamedBoolean("brightnessAvailable"))throw new InvalidOperationException("Brightness unavailable");
                var denied=await App.SendMessageAsync(new ValueSet{{"ToggleBrightnessDim",true}});if(denied==null||!denied.ContainsKey("Error"))throw new InvalidOperationException("Unmarked dim toggle accepted");
                PerformanceNavItem.IsChecked=true;await Click(false,()=>true);int minimum=(int)before.GetNamedNumber("brightnessMinimum");int baseline=minimum<80?minimum+20:minimum;await Set(baseline);await Wait(false,baseline);double width=RailBrightnessSlider.ActualWidth;string readout=RailBrightnessValue.Text;
                await Click(true,()=>OnexBrightnessDimmed);await Wait(true,minimum);
                if(RailBrightnessSlider.Value!=baseline||RailBrightnessValue.Text!=readout||_onexBrightnessSlash.Visibility!=Visibility.Visible||Math.Abs(RailBrightnessSlider.ActualWidth-width)>.1)throw new InvalidOperationException("Dim moved the remembered slider/readout/geometry");
                string red=((SolidColorBrush)RailBrightnessIcon.Foreground).Color.ToString();if(red!="#FFFF6B6B"||((SolidColorBrush)MediaAccentBrush()).Color.ToString()==red||(!before.GetNamedBoolean("muted")&&((SolidColorBrush)RailMuteIcon.Foreground).Color.ToString()==red))throw new InvalidOperationException("Dim accent leaked into audio");
                await SaveOnexImage("onex-brightness-dimmed.png",_releaseHome.MediaSection,0);
                System.IO.File.WriteAllText(System.IO.Path.Combine(folder.Path,"onex-brightness-reload-request.json"),"{}");bool reloaded=false;
                for(int n=0;n<80;n++){try{var loaded=JsonObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(folder.Path,"onex-brightness-reloaded.json")));if(loaded.GetNamedBoolean("brightnessDimmed")&&loaded.GetNamedNumber("brightnessRestore")==baseline&&loaded.GetNamedNumber("brightness")==minimum){reloaded=true;break;}}catch{}await Task.Delay(100);}
                if(!reloaded)throw new InvalidOperationException("Remembered dim level did not survive controller reconstruction");report["durablePreferenceReloaded"]=JsonValue.CreateBooleanValue(true);
                await Click(true,()=>!OnexBrightnessDimmed);await Wait(false,baseline);if(RailBrightnessSlider.Value!=baseline||_onexBrightnessSlash.Visibility!=Visibility.Collapsed)throw new InvalidOperationException("Second tap did not restore the remembered UI");
                await Click(true,()=>OnexBrightnessDimmed);await Wait(true,minimum);int sliderTarget=baseline+1;RailBrightnessSlider.Value=sliderTarget;await WaitMediaValue("brightness",sliderTarget);await Wait(false,sliderTarget);
                await Click(true,()=>OnexBrightnessDimmed);await Wait(true,minimum);var priorExternal=await ReadOnexMediaAsync();int external=baseline+2;
                System.IO.File.WriteAllText(System.IO.Path.Combine(folder.Path,"onex-brightness-external-request.json"),new JsonObject{{"id",JsonValue.CreateStringValue(run)},{"value",JsonValue.CreateNumberValue(external)}}.Stringify());
                await Wait(false,external);var afterExternal=await ReadOnexMediaAsync();if(afterExternal.GetNamedNumber("brightnessWrites")!=priorExternal.GetNamedNumber("brightnessWrites"))throw new InvalidOperationException("External brightness echoed as a UI write");
                await SaveOnexImage("onex-brightness-restored.png",_releaseHome.MediaSection,0);
                report["actualPointerClicks"]=JsonValue.CreateNumberValue(step);report["supportedMinimum"]=JsonValue.CreateNumberValue(minimum);report["dimRestoreBaseline"]=JsonValue.CreateNumberValue(baseline);report["stableSliderAndReadout"]=JsonValue.CreateBooleanValue(true);report["audioAccentUnchanged"]=JsonValue.CreateBooleanValue(true);report["sliderCancelsDim"]=JsonValue.CreateBooleanValue(true);report["externalWindowsCancelsDimWithoutEcho"]=JsonValue.CreateBooleanValue(true);report["unmarkedToggleRejected"]=JsonValue.CreateBooleanValue(true);report["physicalFingerTested"]=JsonValue.CreateBooleanValue(false);
            }
            finally
            {
                await Set((int)(before.GetNamedBoolean("brightnessDimmed",false)?before.GetNamedNumber("brightnessRestore"):before.GetNamedNumber("brightness")));
                if(before.GetNamedBoolean("brightnessDimmed",false))await ToggleOnexBrightnessDimAsync();
                var restored=await ReadOnexMediaAsync();if(restored.GetNamedNumber("brightness")!=before.GetNamedNumber("brightness")||restored.GetNamedBoolean("brightnessDimmed")!=before.GetNamedBoolean("brightnessDimmed",false))throw new InvalidOperationException("Original brightness not restored");
                foreach(string key in new[]{"volume","muted","microphoneMuted"})if(restored[key].Stringify()!=before[key].Stringify())throw new InvalidOperationException("Dim changed audio setting: "+key);
                System.IO.File.WriteAllText(System.IO.Path.Combine(folder.Path,"onex-gamepad-qa-state.json"),"{\"finished\":true}");report["originalMediaRestored"]=JsonValue.CreateBooleanValue(true);
            }
        }
    }
}
