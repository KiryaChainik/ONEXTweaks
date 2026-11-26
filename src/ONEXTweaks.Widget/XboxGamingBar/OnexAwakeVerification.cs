// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.Foundation;
using Windows.Graphics.Display;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation.Peers;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexAwakeAsync(JsonObject report)
        {
            void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
            async Task Wait(Func<bool> expected,string message){for(int i=0;i<100;i++){if(expected())return;await Task.Delay(100);}throw new InvalidOperationException(message);}
            var response=await App.SendMessageAsync(new ValueSet{{"OnexAwakeGet",true}});ApplyOnexAwakeState((string)response["OnexAwakeState"]);
            Check(!OnexAwakeOn,"An owner session is active; do not override it for QA");var original=_onexAwakeState.GetNamedObject("settings");
            var originalMedia=await ReadOnexMediaAsync();Check(originalMedia.GetNamedBoolean("brightnessAvailable")&&!originalMedia.GetNamedBoolean("brightnessDimmed"),"Owner brightness is already dimmed or unavailable; leave it unchanged");
            int brightness=(int)originalMedia.GetNamedNumber("brightness"),minimum=(int)originalMedia.GetNamedNumber("brightnessMinimum");
            JsonObject Settings(int minutes,bool dim)=>new JsonObject{{"durationMinutes",JsonValue.CreateNumberValue(minutes)},{"screenOff",JsonValue.CreateBooleanValue(dim)},{"shutdownAtEnd",JsonValue.CreateBooleanValue(false)}};
            async Task Media(bool dim,int level)
            {
                for(int n=0;n<60;n++){var state=await ReadOnexMediaAsync();ApplyOnexMediaLevels(state.Stringify());if(state.GetNamedBoolean("brightnessDimmed")==dim&&state.GetNamedNumber("brightness")==level)return;await Task.Delay(100);}
                throw new InvalidOperationException("Actual Windows brightness/dim readback mismatch");
            }
            var tile=qsTileMap["OnexAwake"];var ordered=qsTileDefinitions.OrderBy(t=>t.Order).ToArray();int index=Array.IndexOf(ordered,tile);
            Check(tile.IsVisible&&index>=0&&index+1<ordered.Length&&ordered[index+1].Id=="Power","Amphetamine is not immediately before Power Menu");
            var folder=ApplicationData.Current.LocalFolder;string run=Guid.NewGuid().ToString("N");int step=0;
            async Task Input(string button,Func<bool> expected,int closedMilliseconds=250,bool brightnessIcon=false)
            {
                string id=run+":"+(++step);var r=new RadioButtonAutomationPeer(PerformanceNavItem).GetBoundingRectangle();
                double x=r.X+r.Width/2,y=r.Y+r.Height/2;
                if(brightnessIcon)
                {
                    double scale=DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel;
                    var b=RailBrightnessIcon.TransformToVisual(PerformanceNavItem).TransformBounds(new Rect(0,0,RailBrightnessIcon.ActualWidth,RailBrightnessIcon.ActualHeight));
                    x=r.X+(b.X+b.Width/2+widget.WindowBounds.X)*scale;y=r.Y+(b.Y+b.Height/2+widget.WindowBounds.Y)*scale;
                }
                await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-gamepad-qa-state.json",CreationCollisionOption.ReplaceExisting),new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"button",JsonValue.CreateStringValue(button)},{"x",JsonValue.CreateNumberValue(x)},{"y",JsonValue.CreateNumberValue(y)},{"delayMs",JsonValue.CreateNumberValue(closedMilliseconds)},{"checkAwake",JsonValue.CreateBooleanValue(button=="reopen")}}.Stringify());
                var until=DateTimeOffset.UtcNow.AddSeconds(100);while(DateTimeOffset.UtcNow<until)
                {await Task.Delay(100);try{if(await FileIO.ReadTextAsync(await folder.GetFileAsync("onex-gamepad-qa-ack.txt"))==id&&expected())return;}catch{}}
                throw new InvalidOperationException("Amphetamine input failed: "+button+" step "+step);
            }
            async Task ActivateDialog()
            {
                Check(_onexDetailsDialog?.PrimaryButtonText=="Активировать","Menu primary action is not Activate");
                await Wait(()=>_onexDetailsDialog.IsLoaded&&_onexDetailsDialog.ActualWidth>0,"Menu not loaded");
                // Only inspect this live owned dialog, as the other settings QA routines do.
                // ContentDialog itself has no public automation peer; its actual Button does.
                var button=FindOnexSettingsControls<Windows.UI.Xaml.Controls.Button>(_onexDetailsDialog).First(b=>b.Content as string=="Активировать");
                var invoke=new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke) as Windows.UI.Xaml.Automation.Provider.IInvokeProvider;
                Check(invoke!=null,"Actual Activate button peer not found");invoke.Invoke();
                await Wait(()=>OnexAwakeOn&&!_onexAwakeBusy&&_onexDetailsDialog==null,"Activate did not start configured mode/close menu");
            }
            try
            {
                PerformanceNavItem.IsChecked=true;await SetOnexAwakeAsync(false,Settings(1,true));
                tile.TileButton.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(150);tile.TileButton.Focus(FocusState.Keyboard);
                await SaveOnexImage("onex-awake-tile154.png",tile.TileButton);
                await Input("a",()=>OnexAwakeOn&&!_onexAwakeBusy);Check(_onexAwakeState.GetNamedBoolean("requestsHeld"),"No active native requests");
                var active=_onexAwakeState.GetNamedObject("activeSettings");
                Check(active.GetNamedNumber("durationMinutes")==0&&!active.GetNamedBoolean("screenOff")&&!active.GetNamedBoolean("shutdownAtEnd")&&OnexAwakeCaption=="On","Tap reused saved options");
                Check(_onexAwakeState["remainingSeconds"].ValueType==JsonValueType.Null&&_onexAwakeState.GetNamedObject("settings").GetNamedNumber("durationMinutes")==1,"Plain tap started a timer or lost saved settings");await Media(false,brightness);
                await Input("a",()=>!OnexAwakeOn&&!_onexAwakeBusy);report["gamepadTapOnOff"]=JsonValue.CreateBooleanValue(true);
                report["tapIsIndefiniteWithoutOptionsAndPreservesMenuPreferences"]=JsonValue.CreateBooleanValue(true);
                tile.TileButton.Focus(FocusState.Keyboard);await Input("x",()=>_onexDetailsDialog!=null&&_onexAwakeMinutes!=null);
                await ActivateDialog();await Media(true,minimum);Check(_onexAwakeState.GetNamedNumber("remainingSeconds")>0,"Menu Activate did not start its timer from Off");
                report["actualActivateButtonStartsConfiguredModeFromOff"]=JsonValue.CreateBooleanValue(true);
                Check(_onexBrightnessSlash.Visibility==Visibility.Visible&&RailBrightnessSlider.Value==brightness,"Amphetamine did not use the shared crossed icon/remembered slider");
                await Input("focus",()=>!OnexBrightnessDimmed,brightnessIcon:true);await Media(false,brightness);Check(OnexAwakeOn,"Icon restore stopped Amphetamine");
                Check(await SetOnexAwakeAsync(true,Settings(0,true)),"Configured reactivation failed");await Media(true,minimum);
                Check(await SetOnexAwakeAsync(false),"Could not stop after manual restore");await Media(false,brightness);
                report["actualIconRestoresBrightnessWithoutStoppingAwake"]=JsonValue.CreateBooleanValue(true);report["configuredReactivationReappliesDimOption"]=JsonValue.CreateBooleanValue(true);
                await Input("focus",()=>OnexBrightnessDimmed,brightnessIcon:true);await Media(true,minimum);
                Check(await SetOnexAwakeAsync(true,Settings(0,true)),"Already-dim start failed");await Media(true,minimum);
                Check(await SetOnexAwakeAsync(false),"Already-dim stop failed");await Media(true,minimum);
                await Input("focus",()=>!OnexBrightnessDimmed,brightnessIcon:true);await Media(false,brightness);
                report["preExistingManualDimIsPreserved"]=JsonValue.CreateBooleanValue(true);
                Check(await SetOnexAwakeAsync(true,Settings(0,true)),"Dim session failed");await Media(true,minimum);
                int changed=brightness<100?brightness+1:brightness-1;
                var manual=await App.SendMessageAsync(new ValueSet{{"SetBrightnessLevel",changed},{"OnexMediaUserInput",true}});Check(manual!=null&&!manual.ContainsKey("Error"),"Manual brightness override failed");
                Check(await SetOnexAwakeAsync(false),"Manual override stop failed");await Media(false,changed);
                await App.SendMessageAsync(new ValueSet{{"SetBrightnessLevel",brightness},{"OnexMediaUserInput",true}});await Media(false,brightness);
                report["newerManualBrightnessIsNotOverwritten"]=JsonValue.CreateBooleanValue(true);
                Check(await SetOnexAwakeAsync(true,Settings(0,true)),"Dim start failed");await Media(true,minimum);Check(await SetOnexAwakeAsync(false),"Dim stop failed");await Media(false,brightness);
                report["tapOffRestoresOriginalBrightness"]=JsonValue.CreateBooleanValue(true);
                await SetOnexAwakeAsync(false,Settings(1,true));
                tile.TileButton.Focus(FocusState.Keyboard);await Input("x",()=>_onexDetailsDialog!=null&&_onexAwakeMinutes!=null);await ActivateDialog();
                await Media(true,minimum);
                int before=(int)_onexAwakeState.GetNamedNumber("remainingSeconds");await Task.Delay(1500);
                var live=await App.SendMessageAsync(new ValueSet{{"OnexAwakeGet",true}});var actual=JsonObject.Parse((string)live["OnexAwakeState"]);
                Check(actual.GetNamedNumber("remainingSeconds")<before&&OnexAwakeCaption.Contains(":"),"Live countdown did not advance");
                tile.TileButton.Focus(FocusState.Keyboard);await Input("x",()=>_onexDetailsDialog!=null&&_onexAwakeMinutes!=null);
                Check(_onexAwakeHours.SelectedIndex==0&&_onexAwakeMinutes.SelectedIndex==1,"Menu does not show saved time");
                await Task.Delay(250);
                await SaveOnexImage("onex-awake-menu154.png",_onexDetailsDialog);
                report["holdXMenuAndSavedTime"]=JsonValue.CreateBooleanValue(true);report["liveRemainingSeconds"]=JsonValue.CreateBooleanValue(true);
                await Input("reopen",()=>!OnexAwakeOn&&_onexDetailsDialog==null,65000);
                await Wait(()=>!OnexAwakeOn&&tile.StateText.Text=="Off","Expired tile retained On state");
                Check(_onexAwakeState.GetNamedString("stopReason")=="Timer"&&!_onexAwakeState.GetNamedBoolean("requestsHeld"),"Actual one-minute timer did not expire/release requests");
                report["realOneMinuteExpiryWhileOverlayClosed"]=JsonValue.CreateBooleanValue(true);
                await Media(false,brightness);report["realTimerRestoresActualWindowsBrightness"]=JsonValue.CreateBooleanValue(true);
                report["menusClosedAfterOverlayLifecycle"]=JsonValue.CreateBooleanValue(true);report["insertedBeforePowerMenu"]=JsonValue.CreateBooleanValue(true);
                report["injectedControllerSteps"]=JsonValue.CreateNumberValue(step);report["physicalControllerTested"]=JsonValue.CreateBooleanValue(false);
            }
            finally
            {
                CloseOnexMenus();await SetOnexAwakeAsync(false,original);
                await App.SendMessageAsync(new ValueSet{{"SetBrightnessLevel",brightness},{"OnexMediaUserInput",true}});await Media(false,brightness);
                report["originalMediaRestored"]=JsonValue.CreateBooleanValue(true);
                await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-gamepad-qa-state.json",CreationCollisionOption.ReplaceExisting),"{\"finished\":true}");
            }
        }
    }
}
