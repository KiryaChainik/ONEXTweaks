// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation.Peers;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexStartupAsync(JsonObject report)
        {
            var folder=ApplicationData.Current.LocalFolder;string run=Guid.NewGuid().ToString("N");
            SystemNavItem.IsChecked=true;await QueryOnexStartupAsync();
            if(_onexStartupState?.GetNamedBoolean("available",false)!=true)throw new InvalidOperationException("Startup task unavailable");
            bool original=_onexStartupState.GetNamedBoolean("enabled");var actions=new JsonArray();
            try
            {
                int step=0;
                foreach(bool target in new[]{!original,original})
                {
                    OnexStartupToggle.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(200);UpdateLayout();
                    var rect=new ToggleSwitchAutomationPeer(OnexStartupToggle).GetBoundingRectangle();
                    var state=new JsonObject{{"run",JsonValue.CreateStringValue(run)},{"step",JsonValue.CreateNumberValue(++step)},{"target",JsonValue.CreateBooleanValue(target)},{"phase",JsonValue.CreateStringValue("toggle")},{"rect",RectJson(rect)},{"finished",JsonValue.CreateBooleanValue(false)},{"scale",JsonValue.CreateNumberValue(Windows.Graphics.Display.DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel)},{"gameBarBounds",RectJson(widget.WindowBounds)},{"widgetVisible",JsonValue.CreateBooleanValue(widget.Visible)}};
                    await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-startup-qa-state.json",CreationCollisionOption.ReplaceExisting),state.Stringify());
                    bool confirmed=false;
                    for(int n=0;n<100;n++){await Task.Delay(100);if(!_onexStartupBusy&&OnexStartupToggle.IsOn==target&&_onexStartupState.GetNamedBoolean("enabled")==target){confirmed=true;break;}}
                    if(!confirmed)throw new InvalidOperationException("Native pointer did not apply startup toggle");
                    var reply=await App.SendMessageAsync(new ValueSet{{"OnexStartupGet",true}});var actual=JsonObject.Parse((string)reply["OnexStartupState"]);
                    if(!actual.GetNamedBoolean("available")||actual.GetNamedBoolean("enabled")!=target)throw new InvalidOperationException("Startup task readback differs from toggle");
                    actions.Add(JsonValue.CreateBooleanValue(target));
                }
                report["startupPointerTargets"]=actions;report["nativeTaskReadback"]=JsonValue.CreateBooleanValue(true);
                await SaveOnexImage("onex-startup-settings.png",OnexStartupCard);
            }
            finally
            {
                var reply=await SendOnexHomeRequestAsync(new ValueSet{{"OnexStartupSet",original}});ApplyOnexStartupState((string)reply["OnexStartupState"]);
                await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-startup-qa-state.json",CreationCollisionOption.ReplaceExisting),"{\"finished\":true}");
                PerformanceNavItem.IsChecked=true;
            }
        }
    }
}
