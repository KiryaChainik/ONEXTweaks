// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Automation.Peers;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexPowerModeAsync(JsonObject report)
        {
            var folder=ApplicationData.Current.LocalFolder;string run=Guid.NewGuid().ToString("N");var before=JsonObject.Parse((string)(await App.SendMessageAsync(new ValueSet{{"OnexWindowsPowerGet",true}}))["OnexWindowsPowerState"]);bool ac=before.GetNamedBoolean("onAc");int original=(int)before.GetNamedNumber(ac?"modeAc":"modeDc");var steps=new JsonArray();
            try
            {
                _releasePowerNav.IsChecked=true;OSPowerModeComboBox.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(250);
                if(!OSPowerModeComboBox.IsEnabled)throw new InvalidOperationException("OS Power Mode remains disabled");
                OSPowerModeComboBox.SelectedIndex=(original+1)%3;await Task.Delay(350);
                var rejected=JsonObject.Parse((string)(await App.SendMessageAsync(new ValueSet{{"OnexWindowsPowerGet",true}}))["OnexWindowsPowerState"]);if(rejected.GetNamedNumber(ac?"modeAc":"modeDc")!=original)throw new InvalidOperationException("Programmatic selection changed OS power mode");ApplyOnexWindowsPowerState(rejected.Stringify());
                int index=0;
                foreach(int requested in new[]{(original+1)%3,(original+2)%3,original})
                {
                    OSPowerModeComboBox.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(150);
                    ++index;
                    async Task Publish(string phase,Windows.Foundation.Rect rect)
                    {
                        var value=new JsonObject{{"run",JsonValue.CreateStringValue(run)},{"step",JsonValue.CreateNumberValue(index)},{"target",JsonValue.CreateNumberValue(requested)},{"phase",JsonValue.CreateStringValue(phase)},{"rect",RectJson(rect)},{"finished",JsonValue.CreateBooleanValue(false)},{"scale",JsonValue.CreateNumberValue(Windows.Graphics.Display.DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel)}};
                        if(widget!=null){value["gameBarBounds"]=RectJson(widget.WindowBounds);value["widgetVisible"]=JsonValue.CreateBooleanValue(widget.Visible);}
                        await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-mode-qa-state.json",CreationCollisionOption.ReplaceExisting),value.Stringify());
                    }
                    await Publish("open",new ComboBoxAutomationPeer(OSPowerModeComboBox).GetBoundingRectangle());
                    for(int n=0;n<50&&!OSPowerModeComboBox.IsDropDownOpen;n++)await Task.Delay(100);
                    if(!OSPowerModeComboBox.IsDropDownOpen)throw new InvalidOperationException("Pointer did not open OS mode selector");await Task.Delay(200);
                    var item=(ComboBoxItem)OSPowerModeComboBox.Items[requested];await Publish("select",new ComboBoxItemAutomationPeer(item).GetBoundingRectangle());
                    bool acknowledged=false;
                    for(int n=0;n<80;n++)
                    {
                        await Task.Delay(150);var actual=JsonObject.Parse((string)(await App.SendMessageAsync(new ValueSet{{"OnexWindowsPowerGet",true}}))["OnexWindowsPowerState"]);
                        if(actual.GetNamedNumber(ac?"modeAc":"modeDc")==requested){ApplyOnexWindowsPowerState(actual.Stringify());acknowledged=true;break;}
                    }
                    if(!acknowledged||OSPowerModeComboBox.SelectedIndex!=requested)throw new InvalidOperationException("Pointer selection did not reach Windows: "+requested);
                    steps.Add(JsonValue.CreateNumberValue(requested));
                }
                await SaveOnexImage("onex-os-power-mode.png",PerformanceScrollViewer);report["pointerModes"]=steps;report["programmaticSelectionRejected"]=JsonValue.CreateBooleanValue(true);report["selectorReadback"]=JsonValue.CreateBooleanValue(true);
            }
            finally
            {
                await SetOnexPowerModeAsync(original);var restored=JsonObject.Parse((string)(await App.SendMessageAsync(new ValueSet{{"OnexWindowsPowerGet",true}}))["OnexWindowsPowerState"]);ApplyOnexWindowsPowerState(restored.Stringify());
                await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-mode-qa-state.json",CreationCollisionOption.ReplaceExisting),new JsonObject{{"run",JsonValue.CreateStringValue(run)},{"finished",JsonValue.CreateBooleanValue(true)}}.Stringify());
                if(restored.GetNamedNumber("modeAc")!=before.GetNamedNumber("modeAc")||restored.GetNamedNumber("modeDc")!=before.GetNamedNumber("modeDc"))throw new InvalidOperationException("Original AC/DC OS modes not restored");
                report["originalModeRestored"]=JsonValue.CreateBooleanValue(true);
            }
        }
    }
}
