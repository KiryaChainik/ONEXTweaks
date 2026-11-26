// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexBrightnessDetailsAsync(JsonObject report)
        {
            for(int i=0;i<50&&!_onexDesktopAvailable;i++)await Task.Delay(100);if(!_onexDesktopAvailable)throw new InvalidOperationException("Desktop actions unavailable");
            var before=await App.SendMessageAsync(new ValueSet{{"OnexAudioOutputGet",true}});var mediaBefore=JsonObject.Parse((string)before["MediaLevels"]);double brightnessWidth=RailBrightnessSlider.ActualWidth,volumeWidth=RailVolumeSlider.ActualWidth;
            var denied=await App.SendMessageAsync(new ValueSet{{"OnexDesktopAction","displaySettings"}});if(denied==null||!denied.ContainsKey("Error"))throw new InvalidOperationException("Display settings launch without user input accepted");
            if(RailBrightnessIcon.ActualWidth!=24||RailBrightnessIcon.ActualHeight!=28||Math.Abs(brightnessWidth-volumeWidth)>.5)throw new InvalidOperationException("Compact media geometry changed");
            await OpenOnexBrightnessSettingsAsync();
            var after=await App.SendMessageAsync(new ValueSet{{"OnexAudioOutputGet",true}});var mediaAfter=JsonObject.Parse((string)after["MediaLevels"]);
            foreach(string key in new[]{"brightness","volume","muted","microphoneMuted"})if(mediaBefore[key].Stringify()!=mediaAfter[key].Stringify())throw new InvalidOperationException("Display shortcut changed media setting: "+key);
            report["brightnessActionInvoked"]=JsonValue.CreateBooleanValue(true);report["invalidLaunchRejected"]=JsonValue.CreateBooleanValue(true);report["brightnessVolumeMutePreserved"]=JsonValue.CreateBooleanValue(true);report["compactGeometryPreserved"]=JsonValue.CreateBooleanValue(true);report["physicalFingerHoldTested"]=JsonValue.CreateBooleanValue(false);report["expectedSettingsUri"]=JsonValue.CreateStringValue("ms-settings:display");
        }
    }
}
