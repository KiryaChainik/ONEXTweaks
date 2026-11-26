// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Automation.Provider;
using Windows.UI.Xaml.Input;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexPowerSourcesAsync(JsonObject report)
        {
            var folder=ApplicationData.Current.LocalFolder;string run=Guid.NewGuid().ToString("N"),owned=null;int step=0;
            void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
            async Task Wait(Func<bool> ready,string message){for(int n=0;n<100;n++){if(ready())return;await Task.Delay(80);}throw new TimeoutException(message);}
            async Task Input(string key,Func<bool> ready)
            {
                string id=run+":"+(++step);var bounds=new RadioButtonAutomationPeer(_releasePowerNav).GetBoundingRectangle();
                await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-gamepad-qa-state.json",CreationCollisionOption.ReplaceExisting),new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"button",JsonValue.CreateStringValue(key)},{"x",JsonValue.CreateNumberValue(bounds.X+bounds.Width/2)},{"y",JsonValue.CreateNumberValue(bounds.Y+bounds.Height/2)}}.Stringify());
                bool accepted=false;for(int n=0;n<80;n++){await Task.Delay(80);try{accepted=await FileIO.ReadTextAsync(await folder.GetFileAsync("onex-gamepad-qa-ack.txt"))==id&&ready();}catch{}if(accepted)break;}Check(accepted,"Injected source input failed: "+key+" step "+step+" focus="+(FocusManager.GetFocusedElement() as FrameworkElement)?.Name);await Task.Delay(150);
            }
            void Invoke(Button button)=>((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
            JsonObject Side(OnexSourceForm form)=>OnexActiveProfile.GetNamedObject(form.Source);
            async Task Patch(OnexSourceForm form,string key,IJsonValue value)
            {QueueOnexSource(form,key,value);await FlushOnexSourceAsync(form);await Wait(()=>!form.Writing&&form.Pending.Count==0,"Source patch did not settle");}
            await ReadOnexPerformanceProfilesAsync();Check(_onexProfilesState.GetNamedString("activeId")=="global"&&_onexProfilesState.GetNamedValue("currentSession").ValueType==JsonValueType.Null,"Owner active context protected; source QA deferred");
            string originalSelection=_onexProfilesState.GetNamedString("selection","manual");var original=await ReadOnexStateAsync();
            try
            {
                await ChangeOnexPerformanceProfileAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("create")},{"name",JsonValue.CreateStringValue("ONEXQA Sources215")}});owned=OnexActiveProfile.GetNamedString("id");
                await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-source-qa-owned.json",CreationCollisionOption.ReplaceExisting),new JsonObject{{"id",JsonValue.CreateStringValue(owned)},{"run",JsonValue.CreateStringValue(run)}}.Stringify());
                await ChangeOnexPerformanceProfileAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("split")},{"enabled",JsonValue.CreateBooleanValue(true)}});
                _releasePowerNav.IsChecked=true;PerformanceScrollViewer.ChangeView(null,0,null,true);await Task.Delay(200);
                Check(_onexDcEditor.Root.Visibility==Visibility.Visible&&_onexAcEditor.Root.Visibility==Visibility.Visible&&OnexPerfCardGrid.Visibility==Visibility.Collapsed,"Both full source editors are not visible");
                Check(_onexDcEditor.Cards.Count==7&&_onexAcEditor.Cards.Count==7,"Source editor lacks settings cards");
                var body=(Panel)PerformanceScrollViewer.Content;Check(body.Children.IndexOf(_onexDcEditor.Root)==body.Children.IndexOf(_onexPerformanceProfilePickerCard)+1&&body.Children.IndexOf(_onexAcEditor.Root)==body.Children.IndexOf(_onexPerformanceProfileFooter)+1,"DC/AC blocks are not in requested positions");
                var active=_onexProfilesState.GetNamedBoolean("onAc")?_onexAcEditor:_onexDcEditor;var inactive=ReferenceEquals(active,_onexAcEditor)?_onexDcEditor:_onexAcEditor;
                string rawBefore=original.GetNamedString("rawLimits");int baseValue=Math.Min(35,(int)Side(inactive).GetNamedNumber("pl1"));
                await Patch(inactive,"pl1",JsonValue.CreateNumberValue(baseValue));Check((await ReadOnexStateAsync()).GetNamedString("rawLimits")==rawBefore,"Inactive save changed actual TDP");
                Invoke(inactive.Cards[0].Header);await Input("focus",()=>true);await Input("up",()=>true);var slider=inactive.Sliders.Single(value=>value.Name=="Onex"+inactive.Source+"_pl1");slider.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(150);Check(slider.Focus(FocusState.Keyboard),"Inactive slider could not take focus");
                await Input("a",()=>OnexInputGuard.IsPreviewing(slider));await Input("right",()=>slider.Value==baseValue+1);await Input("a",()=>!OnexInputGuard.IsPreviewing(slider));
                await Wait(()=>Side(inactive).GetNamedNumber("pl1")==baseValue+1&&!OnexSourceEditorsPending,"A did not save inactive slider");Check((await ReadOnexStateAsync()).GetNamedString("rawLimits")==rawBefore,"Inactive controller edit changed hardware");
                await Input("a",()=>OnexInputGuard.IsPreviewing(slider));await Input("right",()=>slider.Value==baseValue+2);await Input("b",()=>!OnexInputGuard.IsPreviewing(slider)&&slider.Value==baseValue+1);await Task.Delay(350);Check(Side(inactive).GetNamedNumber("pl1")==baseValue+1,"B applied an unconfirmed source edit");
                await Input("a",()=>OnexInputGuard.IsPreviewing(slider));await Input("right",()=>slider.Value==baseValue+2);await Input("down",()=>!ReferenceEquals(FocusManager.GetFocusedElement(),slider));await Wait(()=>Side(inactive).GetNamedNumber("pl1")==baseValue+2&&!OnexSourceEditorsPending,"Down did not save and leave slider");
                Invoke(active.Cards[0].Header);var activeSlider=active.Sliders.Single(value=>value.Name=="Onex"+active.Source+"_pl1");int actual=(int)Side(active).GetNamedNumber("pl1"),change=actual<40?1:-1;activeSlider.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(150);Check(activeSlider.Focus(FocusState.Keyboard),"Active slider could not take focus");
                await Input("a",()=>OnexInputGuard.IsPreviewing(activeSlider));await Input(change>0?"right":"left",()=>activeSlider.Value==actual+change);await Input("a",()=>!OnexInputGuard.IsPreviewing(activeSlider));
                await Wait(()=>Side(active).GetNamedNumber("pl1")==actual+change&&!OnexSourceEditorsPending,"Active source did not save");Check((await ReadOnexStateAsync()).GetNamedNumber("pl1Watts")==actual+change,"Active controller source edit did not reach hardware");await Patch(active,"pl1",JsonValue.CreateNumberValue(actual));
                var acBefore=OnexActiveProfile.GetNamedObject("ac").Stringify();await ChangeOnexPerformanceProfileAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("split")},{"enabled",JsonValue.CreateBooleanValue(false)}});
                Check(_onexAcEditor.Root.Visibility==Visibility.Collapsed&&_onexDcEditor.Root.Visibility==Visibility.Collapsed&&OnexPerfCardGrid.Visibility==Visibility.Visible,"Disabled split did not hide AC block/title and restore shared editor");Check(OnexActiveProfile.GetNamedObject("ac").Stringify()==acBefore,"Disabling split erased AC values");
                await ChangeOnexPerformanceProfileAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("split")},{"enabled",JsonValue.CreateBooleanValue(true)}});Check(OnexActiveProfile.GetNamedObject("ac").Stringify()==acBefore,"Re-enabling split replaced archived AC");
                await SaveOnexImage("onex-sources-dc-215.png",_onexDcEditor.Root,800);await SaveOnexImage("onex-sources-ac-215.png",_onexAcEditor.Root,800);
                report["twoCompleteEditorsAndRequestedPlacement"]=JsonValue.CreateBooleanValue(true);report["inactiveSideSaveDoesNotApplyHardware"]=JsonValue.CreateBooleanValue(true);report["activeSideAReachesActualTdp"]=JsonValue.CreateBooleanValue(true);report["aCommitBRevertDownCommitMove"]=JsonValue.CreateBooleanValue(true);report["hideAndRetainAc"]=JsonValue.CreateBooleanValue(true);report["injectedControllerSteps"]=JsonValue.CreateNumberValue(step);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);report["physicalUnplugTested"]=JsonValue.CreateBooleanValue(false);
            }
            finally
            {
                foreach(var form in new[]{_onexDcEditor,_onexAcEditor})if(form!=null){form.Timer.Stop();form.Pending.Clear();foreach(var slider in form.Sliders)OnexInputGuard.Cancel(slider);}
                await Wait(()=>!new[]{_onexDcEditor,_onexAcEditor}.Any(form=>form?.Writing==true),"Source write did not finish before QA cleanup");
                if(owned!=null&&OnexProfiles.Any(entry=>entry.GetNamedString("id")==owned&&entry.GetNamedString("name")=="ONEXQA Sources215"))await ChangeOnexPerformanceProfileAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("delete")},{"id",JsonValue.CreateStringValue(owned)}});
                if(originalSelection=="auto")await ChangeOnexPerformanceProfileAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("automatic")}});
                var after=await ReadOnexStateAsync();Check(after.GetNamedString("rawLimits")==original.GetNamedString("rawLimits")&&after.GetNamedNumber("rememberedPl2Watts")==original.GetNamedNumber("rememberedPl2Watts")&&after.GetNamedBoolean("separatePl2")==original.GetNamedBoolean("separatePl2"),"Original hardware TDP/PL2 not restored");
                var manifest=await folder.TryGetItemAsync("onex-source-qa-owned.json");if(manifest!=null)await manifest.DeleteAsync();await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-gamepad-qa-state.json",CreationCollisionOption.ReplaceExisting),"{\"finished\":true}");
            }
        }
    }
}
