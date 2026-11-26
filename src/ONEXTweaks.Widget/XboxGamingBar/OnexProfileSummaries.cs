// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private TextBlock _onexIntelSettingsSummary;
        private static string OnexPresetLink(JsonObject side,string key)=>side!=null&&side.TryGetValue(key,out var value)&&value.ValueType==JsonValueType.String?value.GetString():null;
        private static JsonObject OnexPreset(JsonObject state,string id)=>state?.GetNamedArray("entries",null)?.Select(value=>value.GetObject()).FirstOrDefault(value=>value.GetNamedString("id")==id);
        private string OnexPresetName(JsonObject entry)=>entry==null||entry.GetNamedString("id")=="global"?OnexStrings.Get("Global Profile"):entry.GetNamedString("id").StartsWith("firmware-",StringComparison.Ordinal)?OnexStrings.Get(entry.GetNamedString("name")):entry.GetNamedString("name");
        private JsonObject OnexPresetValues(JsonObject state,string id)
        {var entry=OnexPreset(state,id);return entry?.GetNamedObject(state.GetNamedBoolean("onAc",false)&&entry.GetNamedBoolean("split",false)?"ac":"dc",null);}
        private string OnexScreenBrief(JsonObject side,bool withName)
        {
            string id=OnexScreenProfileId;var entry=OnexPreset(_onexScreenProfiles,id);var values=OnexPresetValues(_onexScreenProfiles,id);
            var hdr=OnexDisplayObject(values,"hdr");var color=OnexDisplayObject(values,"colors");
            string brief="HDR "+OnexStrings.Get(hdr?.GetNamedBoolean("enabled",false)==true?"On":"Off")+" · Auto HDR "+OnexStrings.Get(hdr?.GetNamedBoolean("autoHdr",false)==true?"On":"Off");
            if(color!=null)brief+=" · S"+color.GetNamedNumber("saturation")+" C"+color.GetNamedNumber("contrast")+" B"+color.GetNamedNumber("brightness");
            return (withName?OnexPresetName(entry)+" · ":"")+brief;
        }
        private void RefreshOnexProfileSummaries()
        {
            if(PerformanceScrollViewer?.Visibility!=Visibility.Visible)return;
            if(_onexProfilesState!=null)_onexPerformanceProfilePicker?.Apply(_onexProfilesState);
            if(_onexPerfSections!=null)RefreshOnexPerformanceCards();
            RefreshOnexIntelSummary();
        }
        private void EnsureOnexIntelSummary()
        {
            if(_onexIntelSettingsSummary!=null||!(DisplayGpuTabHost.Child is Panel body))return;
            var header=new StackPanel{Spacing=4};
            header.Children.Add(new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("Графические настройки активного профиля производительности"),FontSize=12,Opacity=.75,TextWrapping=TextWrapping.Wrap,Foreground=tileTextBrush});
            _onexIntelSettingsSummary=new TextBlock{Name="OnexIntelActiveSummary",FontSize=12,Opacity=.85,TextWrapping=TextWrapping.Wrap,Foreground=tileTextBrush};header.Children.Add(_onexIntelSettingsSummary);body.Children.Insert(0,header);
        }
        private string OnexCpuCardBrief(JsonObject side)
        {
            int scheduling=(int)(side?.GetNamedNumber("scheduling",0)??0);
            string policy=new[]{"All cores","P cores only","Prefer P cores","E cores only","Prefer E cores","Windows policy"}[Math.Max(0,Math.Min(5,scheduling))];
            return global::XboxGamingBar.OnexStrings.Format("CPU Boost {0}\n{1}",OnexStrings.Get(_onexBoostOn?"On":"Off"),OnexStrings.Get(policy));
        }
        private string OnexIntelBrief()=>_onexIntelSettingsSummary?.Text??OnexStrings.Get("Ожидание данных Intel");
        private string OnexResolutionBrief()
        {
            var current=OnexDisplayObject(OnexDisplayObject(_onexDisplayState,"modes"),"current");
            return current==null?"—":current.GetNamedNumber("width")+"×"+current.GetNamedNumber("height")+" · "+current.GetNamedNumber("hz")+global::XboxGamingBar.OnexStrings.Get(" Hz");
        }
        private void RefreshOnexIntelSummary()
        {
            EnsureOnexIntelSummary();if(_onexIntelSettingsSummary==null)return;
            var settings=OnexDisplayObject(_onexDisplayState,"settings");var parts=new List<string>();
            void Add(string label,string key,ComboBox choice)
            {
                var value=OnexDisplayObject(settings,key);if(value?.GetNamedBoolean("available",false)!=true)return;
                string actual=(choice.SelectedItem as ComboBoxItem)?.Content?.ToString()??choice.SelectedItem?.ToString();
                var token=value.GetNamedValue("value",JsonValue.CreateNullValue());if(string.IsNullOrWhiteSpace(actual)&&token.ValueType==JsonValueType.Number)actual=token.GetNumber().ToString(CultureInfo.InvariantCulture);
                if(!string.IsNullOrWhiteSpace(actual))parts.Add(OnexStrings.Get(label)+": "+actual);
            }
            Add("Frame Gen","frameGeneration",IntelFrameGenerationComboBox);Add("VRR","vrr",IntelVrrComboBox);Add("VRR mode","vrrMode",IntelVrrModeComboBox);Add("Low Latency","lowLatency",IntelLowLatencyComboBox);Add("Sync","frameSync",IntelFrameSyncComboBox);Add("Scaling","scalingMode",IntelScalingModeComboBox);Add("Method","scalingMethod",IntelScalingMethodComboBox);
            _onexIntelSettingsSummary.Text=parts.Count==0?global::XboxGamingBar.OnexStrings.Get("Ожидание данных Intel"):string.Join(" · ",parts);
        }
    }
}
