// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private sealed class OnexCpuSettingsForm
        {internal StackPanel Panel;internal Control[] NavigationControls;internal Control[][] NavigationRows;internal Func<Task> Apply;internal Action<JsonObject> Refresh;internal Action Cancel;internal Func<bool> Pending;internal bool AutoSave;internal bool? OnAc;internal string ProfileId;}
        private async Task<OnexCpuSettingsForm> CreateOnexCpuSettingsFormAsync()
        {
            string expectedProfile=OnexFrontend.ProfileId;
            var read=await App.SendMessageAsync(new ValueSet{{"OnexWindowsPowerGet",true}});ApplyOnexWindowsPowerState((string)read["OnexWindowsPowerState"]);
            if(expectedProfile!=OnexFrontend.ProfileId)throw new InvalidOperationException(global::XboxGamingBar.OnexStrings.Get("Профиль сменился; открой CPU Settings заново."));
            if(!OnexWindowsPowerReady)throw new InvalidOperationException(_onexWindowsPower?.GetNamedString("error","CPU settings unavailable"));
            var cpu=_onexWindowsPower.GetNamedObject("cpu");var panel=new StackPanel{Spacing=8};
            ComboBox Choose(string header,string key,string[] names)
            {
                var choice=OnexSettingsChoice(header);choice.Name="OnexCpu"+char.ToUpperInvariant(key[0])+key.Substring(1);choice.Header=OnexHelpHeader(header,(header=="Plugged in"?global::XboxGamingBar.OnexStrings.Get("Действует при подключённой зарядке.\n\n"):global::XboxGamingBar.OnexStrings.Get("Действует при работе от батареи.\n\n"))+OnexStrings.Get(key.StartsWith("boost")?OnexBoostHelp:OnexCoreHelp));choice.ItemsSource=names.Select(OnexStrings.Get).ToArray();choice.SelectedIndex=(int)cpu.GetNamedNumber(key);return choice;
            }
            string[] boosts={"Disabled","Enabled","Aggressive","Efficient enabled","Efficient aggressive","Aggressive at guaranteed","Efficient aggressive at guaranteed"};
            var boostAc=Choose("Plugged in","boostAc",boosts);var boostDc=Choose("Battery","boostDc",boosts);var boostsPanel=new StackPanel{Spacing=8};boostsPanel.Children.Add(new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("Boost policy"),FontSize=14});boostsPanel.Children.Add(OnexSettingsColumns(boostAc,boostDc));panel.Children.Add(OnexSettingsCard(boostsPanel));
            string[] policies={"All cores","Only performance cores","Prefer performance cores","Only efficiency cores","Prefer efficiency cores","Automatic"};
            var schedulingAc=Choose("Plugged in","schedulingAc",policies);var schedulingDc=Choose("Battery","schedulingDc",policies);var coresPanel=new StackPanel{Spacing=8};coresPanel.Children.Add(new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("Core preference"),FontSize=14});coresPanel.Children.Add(OnexSettingsColumns(schedulingAc,schedulingDc));panel.Children.Add(OnexSettingsCard(coresPanel));
            int priorClock=(int)(_onexHardwareHome?.GetNamedNumber("cpuClockMhz",0)??0);var cap=new ToggleSwitch{Name="OnexCpuClockToggle",IsOn=priorClock>0,MinWidth=0};var clockText=new TextBlock{Text=priorClock>0?priorClock+global::XboxGamingBar.OnexStrings.Get(" MHz"):global::XboxGamingBar.OnexStrings.Get("Auto"),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center,Foreground=OnexDialogAccent};
            var frequency=new Slider{Name="OnexCpuClockSlider",Minimum=1000,Maximum=6000,StepFrequency=100,SmallChange=100,LargeChange=500,SnapsTo=Windows.UI.Xaml.Controls.Primitives.SliderSnapsTo.StepValues,Value=priorClock>0?priorClock:2500,IsEnabled=cap.IsOn,IsThumbToolTipEnabled=true,Height=36};var frequencyPanel=new StackPanel{Spacing=2};frequencyPanel.Children.Add(OnexSettingsColumns(OnexSettingsInlineToggle(OnexHelpHeader(global::XboxGamingBar.OnexStrings.Get("Frequency limit"),OnexFrequencyHelp),cap),clockText));frequencyPanel.Children.Add(frequency);panel.Children.Add(OnexSettingsCard(frequencyPanel));
            void UpdateClock(){frequency.IsEnabled=cap.IsOn;clockText.Text=cap.IsOn?((int)frequency.Value)+global::XboxGamingBar.OnexStrings.Get(" MHz"):global::XboxGamingBar.OnexStrings.Get("Auto");}cap.Toggled+=(s,e)=>UpdateClock();frequency.ValueChanged+=(s,e)=>UpdateClock();
            bool? oldMask=null;ToggleSwitch mask=null;var maskValue=_onexHardwareHome?.GetNamedValue("smallProcessorMask",JsonValue.CreateNullValue());
            if(maskValue?.ValueType==JsonValueType.Boolean){oldMask=maskValue.GetBoolean();mask=new ToggleSwitch{Name="OnexCpuMaskToggle",IsOn=oldMask.Value};panel.Children.Add(OnexSettingsInlineToggle(OnexHelpHeader(global::XboxGamingBar.OnexStrings.Get("P/E scheduling · restart"),OnexMaskHelp),mask));}
            panel.Resources[typeof(Slider)]=Resources["OnexPreciseSliderStyle"];foreach(var control in OnexLogicalElements<Control>(panel))if(control is ToggleSwitch||control is ComboBox)ApplyOnexTileWindowControlAccent(control);
            string Fingerprint()=>boostAc.SelectedIndex+":"+boostDc.SelectedIndex+":"+schedulingAc.SelectedIndex+":"+schedulingDc.SelectedIndex+":"+cap.IsOn+":"+frequency.Value+":"+mask?.IsOn;
            string baseline=Fingerprint();
            void Refresh(JsonObject current)
            {
                // Reflect acknowledged changes from another view until this editor
                // contains a draft. Never overwrite an unsubmitted choice.
                if(Fingerprint()!=baseline)return;
                boostAc.SelectedIndex=(int)current.GetNamedNumber("boostAc");boostDc.SelectedIndex=(int)current.GetNamedNumber("boostDc");
                schedulingAc.SelectedIndex=(int)current.GetNamedNumber("schedulingAc");schedulingDc.SelectedIndex=(int)current.GetNamedNumber("schedulingDc");
                baseline=Fingerprint();
            }
            return new OnexCpuSettingsForm{Panel=panel,NavigationControls=new Control[]{boostAc,boostDc,schedulingAc,schedulingDc,cap,frequency,mask},Refresh=Refresh,Apply=async()=>
            {
                if(expectedProfile!=OnexFrontend.ProfileId)throw new InvalidOperationException(global::XboxGamingBar.OnexStrings.Get("Профиль сменился; открой CPU Settings заново."));
                // An inline editor may stay open. Preserve all policies the form does not own,
                // including Windows/OEM frequency ceilings which can change independently.
                var fresh=await App.SendMessageAsync(new ValueSet{{"OnexWindowsPowerGet",true}});var current=JsonObject.Parse((string)fresh["OnexWindowsPowerState"]).GetNamedObject("cpu");
                current["boostAc"]=JsonValue.CreateNumberValue(boostAc.SelectedIndex);current["boostDc"]=JsonValue.CreateNumberValue(boostDc.SelectedIndex);current["schedulingAc"]=JsonValue.CreateNumberValue(schedulingAc.SelectedIndex);current["schedulingDc"]=JsonValue.CreateNumberValue(schedulingDc.SelectedIndex);current["shortSchedulingAc"]=current["schedulingAc"];current["shortSchedulingDc"]=current["schedulingDc"];
                var applied=await SendOnexHomeRequestAsync(new ValueSet{{"OnexCpuSettings",current.Stringify()},{"OnexProfileId",expectedProfile}});ApplyOnexWindowsPowerState((string)applied["OnexWindowsPowerState"]);
                int requested=cap.IsOn?(int)frequency.Value:0;if(requested!=priorClock){var clock=await SendOnexHomeRequestAsync(new ValueSet{{"OnexCpuClockSet",requested},{"OnexProfileId",expectedProfile}});ApplyOnexHardwareHomeState((string)clock["OnexHardwareHomeState"]);priorClock=requested;}
                if(mask!=null&&mask.IsOn!=oldMask.Value){var updated=await SendOnexHomeRequestAsync(new ValueSet{{"OnexSmallMaskSet",mask.IsOn}});ApplyOnexHardwareHomeState((string)updated["OnexHardwareHomeState"]);oldMask=mask.IsOn;ShowOnexDesktopStatus(global::XboxGamingBar.OnexStrings.Get("CPU scheduling change saved. Restart Windows to activate it."));}
                baseline=Fingerprint();
            }};
        }
    }
}
