// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Automation.Provider;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task VerifyOnexCpuAdvancedAsync(JsonObject report)
        {
            var folder=ApplicationData.Current.LocalFolder;string run=Guid.NewGuid().ToString("N"),owned=null,label="initial";int step=0;Slider active=null;
            void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
            async Task<JsonObject> Read(){var reply=await App.SendMessageAsync(new ValueSet{{"OnexCpuTuningGet",true}});return JsonObject.Parse((string)reply["OnexCpuTuningState"]);}
            string Actual(JsonObject value)
            {
                // A JSON object's serialization order is not its state. Compare
                // canonical numeric settings and retain both sides on failure.
                return string.Join(";",value.GetNamedObject("fields").OrderBy(item=>item.Key,StringComparer.Ordinal).Select(item=>item.Key+":"+item.Value.GetObject().GetNamedNumber("ac").ToString("R",CultureInfo.InvariantCulture)+","+item.Value.GetObject().GetNamedNumber("dc").ToString("R",CultureInfo.InvariantCulture)));
            }
            async Task Input(string key,Func<bool> expected)
            {
                string id=run+":"+(++step);var rect=new RadioButtonAutomationPeer(_releasePowerNav).GetBoundingRectangle();
                string packet=new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"button",JsonValue.CreateStringValue(key)},{"x",JsonValue.CreateNumberValue(rect.X+rect.Width/2)},{"y",JsonValue.CreateNumberValue(rect.Y+rect.Height/2)}}.Stringify();
                for(int n=0;;n++){try{System.IO.File.WriteAllText(System.IO.Path.Combine(folder.Path,"onex-gamepad-qa-state.json"),packet);break;}catch(System.IO.IOException){if(n>=40)throw;await Task.Delay(25);}}
                for(int n=0;n<80;n++){await Task.Delay(100);try{if(System.IO.File.ReadAllText(System.IO.Path.Combine(folder.Path,"onex-gamepad-qa-ack.txt"))==id&&expected())return;}catch(System.IO.IOException){}}
                throw new InvalidOperationException(label+" failed "+key+"; focus="+(FocusManager.GetFocusedElement() as FrameworkElement)?.Name+"; value="+active?.Value);
            }
            async Task Focus(Slider slider){active=slider;slider.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(100);Check(slider.Focus(FocusState.Keyboard),"Cannot focus "+label);}
            bool Focused()=>ReferenceEquals(FocusManager.GetFocusedElement(),active);
            async Task Applied(string alias,string key,int expected)
            {
                for(int n=0;n<65;n++)
                {
                    await Task.Delay(100);var state=await Read();await ReadOnexPerformanceProfilesAsync();
                    var side=OnexActiveProfile.GetNamedObject(_onexProfilesState.GetNamedBoolean("onAc")?"ac":"dc");
                    if(state.GetNamedObject("fields").GetNamedObject(alias).GetNamedNumber("value")==expected&&side.GetNamedObject("cpuTuning",new JsonObject()).GetNamedNumber(key,-1)==expected&&_onexPerformanceCpuForm?.Pending?.Invoke()!=true)return;
                }
                throw new InvalidOperationException("CPU value did not apply/save: "+alias);
            }
            _releasePowerNav.IsChecked=true;EnsureOnexPerformanceCards();await SetOnexPerfCardAsync(null);await ReadOnexPerformanceProfilesAsync();
            string initialEntries=_onexProfilesState.GetNamedArray("entries").Stringify(),initialSelection=_onexProfilesState.GetNamedString("selection");
            Check(OnexFrontend.ProfileId=="global"&&!_onexProfilesState.GetNamedBoolean("manualOverride",false),"CPU QA requires unheld Global");
            var initial=await Read();Check(initial.GetNamedBoolean("available"),"CPU native policy is unavailable");
            var power=await ReadOnexStateAsync();string originalActual=Actual(initial);var policy=new JsonObject();
            foreach(var item in initial.GetNamedObject("fields"))
            {
                var field=item.Value.GetObject();Check(field.GetNamedBoolean("available"),"Required CPU field unavailable: "+item.Key);string name=field.GetNamedString("name");name=char.ToLowerInvariant(name[0])+name.Substring(1);double value=field.GetNamedNumber("value");
                if(item.Key=="freq1")value=3500;if(item.Key=="freq0")value=2500;if(item.Key.StartsWith("epp"))value=value<=90?value+5:value-5;policy[name]=JsonValue.CreateNumberValue(value);
            }
            var cases=new JsonArray();report["controls"]=cases;
            try
            {
                await ChangeOnexPerformanceProfileAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("create")},{"name",JsonValue.CreateStringValue("ONEX CPU QA")}});owned=OnexFrontend.ProfileId;Check(owned!="global","CPU QA scope not created");
                System.IO.File.WriteAllText(System.IO.Path.Combine(folder.Path,"onex-cpu-qa-owned.json"),new JsonObject{{"id",JsonValue.CreateStringValue(owned)},{"run",JsonValue.CreateStringValue(run)}}.Stringify());
                var request=new ValueSet{{"OnexCpuTuningSet",policy.Stringify()},{"OnexProfileId",owned},{"OnexCpuTuningOnAc",initial.GetNamedBoolean("onAc")},{"OnexCpuTuningScheme",initial.GetNamedString("scheme")}};
                var set=await SendOnexHomeRequestAsync(request);var applied=JsonObject.Parse((string)set["OnexCpuTuningState"]);
                foreach(var item in applied.GetNamedObject("fields")){var field=item.Value.GetObject();string key=field.GetNamedString("name");key=char.ToLowerInvariant(key[0])+key.Substring(1);Check(field.GetNamedNumber("value")==policy.GetNamedNumber(key),"Native policy acknowledgement mismatch: "+item.Key);Check(field.GetNamedNumber(initial.GetNamedBoolean("onAc")?"dc":"ac")==initial.GetNamedObject("fields").GetNamedObject(item.Key).GetNamedNumber(initial.GetNamedBoolean("onAc")?"dc":"ac"),"Inactive CPU side changed");}
                await SetOnexPerfCardAsync("cpu");Check(_onexPerformanceCpuForm?.AutoSave==true,"Advanced CPU form is missing");
                var sliders=OnexLogicalElements<Slider>(_onexPerformanceCpuForm.Panel).ToArray();Check(sliders.Length==12,"Expected twelve P/E CPU controls");
                Check(_onexPerformanceCpuForm.NavigationRows.Length==8,"CPU navigation rows differ");
                await SaveOnexImage("onex-cpu-advanced-201.png",_onexPerformanceCpuForm.Panel,0);
                await Input("up",()=>true);var beforePreviewState=await Read();string beforePreview=Actual(beforePreviewState);double beforeUserWrites=beforePreviewState.GetNamedNumber("userWrites");
                foreach(var slider in sliders)
                {
                    label=slider.Name;await Focus(slider);double original=slider.Value,delta=slider.Name.Contains("freq")?100:5;int sign=original+delta<=slider.Maximum?1:-1;
                    await Input("a",()=>Focused()&&OnexInputGuard.IsPreviewing(slider));
                    await Input(sign>0?"right":"left",()=>Focused()&&OnexInputGuard.IsPreviewing(slider)&&Math.Abs(slider.Value-original-sign*delta)<.01&&_releasePowerNav.IsChecked==true);
                    await Input("b",()=>Focused()&&!OnexInputGuard.IsPreviewing(slider)&&Math.Abs(slider.Value-original)<.01);
                    cases.Add(new JsonObject{{"name",JsonValue.CreateStringValue(slider.Name)},{"previewStep",JsonValue.CreateNumberValue(delta)},{"bCancelled",JsonValue.CreateBooleanValue(true)},{"touchStep",JsonValue.CreateNumberValue(slider.StepFrequency)}});
                }
                var afterPreviewState=await Read();for(int n=0;n<25&&Actual(afterPreviewState)!=beforePreview;n++){await Task.Delay(100);afterPreviewState=await Read();}
                string afterPreview=Actual(afterPreviewState);report["beforePreviewNative"]=JsonValue.CreateStringValue(beforePreview);report["afterPreviewNative"]=JsonValue.CreateStringValue(afterPreview);
                Check(afterPreviewState.GetNamedNumber("userWrites")==beforeUserWrites,"Cancelled previews sent an explicit CPU write");
                report["cancelledPreviewUserWrites"]=JsonValue.CreateNumberValue(afterPreviewState.GetNamedNumber("userWrites")-beforeUserWrites);report["backgroundRepairs"]=afterPreviewState.GetNamedValue("repairWrites");
                Check(afterPreview==beforePreview,"Cancelled previews changed native CPU settings");
                var epp=sliders.Single(slider=>slider.Name=="OnexCpuAdvancedepp1");await Focus(epp);int originalEpp=(int)epp.Value,signEpp=originalEpp+5<=100?1:-1,target=originalEpp+5*signEpp;
                label="EPP A commit";await Input("a",()=>OnexInputGuard.IsPreviewing(epp));await Input(signEpp>0?"right":"left",()=>Focused()&&epp.Value==target);await Input("a",()=>Focused()&&!OnexInputGuard.IsPreviewing(epp));await Applied("epp1","perfEpp",target);Check(Focused(),"CPU A acknowledgement moved focus");
                label="EPP Down commit";await Input("a",()=>OnexInputGuard.IsPreviewing(epp));await Input(signEpp>0?"left":"right",()=>epp.Value==originalEpp);var down=epp.XYFocusDown;await Input("down",()=>!OnexInputGuard.IsPreviewing(epp)&&ReferenceEquals(FocusManager.GetFocusedElement(),down));await Applied("epp1","perfEpp",originalEpp);Check(ReferenceEquals(FocusManager.GetFocusedElement(),down),"CPU Down acknowledgement moved focus");
                label="EPP Up commit";await Focus(epp);await Input("a",()=>OnexInputGuard.IsPreviewing(epp));await Input(signEpp>0?"right":"left",()=>epp.Value==target);var up=epp.XYFocusUp;await Input("up",()=>!OnexInputGuard.IsPreviewing(epp)&&ReferenceEquals(FocusManager.GetFocusedElement(),up));await Applied("epp1","perfEpp",target);Check(ReferenceEquals(FocusManager.GetFocusedElement(),up),"CPU Up acknowledgement moved focus");
                var reset=OnexLogicalElements<Button>(_onexPerformanceCpuForm.Panel).Single(button=>button.Name=="OnexCpuAdvancedReset");((IInvokeProvider)new ButtonAutomationPeer(reset).GetPattern(PatternInterface.Invoke)).Invoke();
                for(int n=0;n<80;n++){await Task.Delay(100);if(Actual(await Read())==originalActual&&_onexPerformanceCpuForm?.Pending?.Invoke()!=true)break;}
                Check(Actual(await Read())==originalActual,"Reset did not restore the original native values");
                report["actualPolicyReadback"]=JsonValue.CreateBooleanValue(true);report["inactiveSidePreserved"]=JsonValue.CreateBooleanValue(true);report["aUpDownAppliedAndSaved"]=JsonValue.CreateBooleanValue(true);report["resetRestoresOriginals"]=JsonValue.CreateBooleanValue(true);report["virtualControllerSteps"]=JsonValue.CreateNumberValue(step);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);report["measuredCpuFrequencyTested"]=JsonValue.CreateBooleanValue(false);
            }
            finally
            {
                try
                {
                    OnexInputGuard.Cancel(active);_onexPerformanceCpuForm?.Cancel?.Invoke();
                    for(int n=0;n<100&&_onexPerformanceCpuApplying;n++)await Task.Delay(100);
                    await SetOnexPerfCardAsync(null);
                    if(owned!=null)await ChangeOnexPerformanceProfileAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("delete")},{"id",JsonValue.CreateStringValue(owned)}});
                    if(initialSelection=="auto")await ChangeOnexPerformanceProfileAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("automatic")}});
                    await ReadOnexPerformanceProfilesAsync();Check(_onexProfilesState.GetNamedArray("entries").Stringify()==initialEntries,"Original performance entries changed");
                    Check(Actual(await Read())==originalActual,"Original CPU policies were not restored");Check((await ReadOnexStateAsync()).GetNamedString("rawLimits")==power.GetNamedString("rawLimits"),"CPU QA changed original TDP");
                    report["originalEntriesCpuPowerRestored"]=JsonValue.CreateBooleanValue(true);System.IO.File.Delete(System.IO.Path.Combine(folder.Path,"onex-cpu-qa-owned.json"));
                }
                finally{for(int n=0;;n++){try{System.IO.File.WriteAllText(System.IO.Path.Combine(folder.Path,"onex-gamepad-qa-state.json"),"{\"finished\":true}");break;}catch(System.IO.IOException){if(n>=40)throw;await Task.Delay(25);}}}
            }
        }
    }
}
