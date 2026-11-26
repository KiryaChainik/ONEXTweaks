// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task<OnexCpuSettingsForm> CreateOnexCpuAdvancedFormAsync()
        {
            string expectedProfile=OnexFrontend.ProfileId;
            var initial=await App.SendMessageAsync(new ValueSet{{"OnexCpuTuningGet",true}});
            if(expectedProfile!=OnexFrontend.ProfileId)throw new InvalidOperationException(global::XboxGamingBar.OnexStrings.Get("Профиль сменился. Открой карточку CPU заново."));
            var state=JsonObject.Parse((string)initial["OnexCpuTuningState"]);
            if(!state.GetNamedBoolean("available",false))throw new InvalidOperationException(state.GetNamedString("error","CPU settings unavailable"));
            ApplyOnexWindowsPowerState((string)initial["OnexWindowsPowerState"]);
            bool onAc=state.GetNamedBoolean("onAc"),updating=true,disposed=false,writing=false;
            string scheme=state.GetNamedString("scheme");
            var panel=new StackPanel{Spacing=10};panel.Resources[typeof(Slider)]=Resources["OnexPreciseSliderStyle"];
            var status=new TextBlock{Name="OnexCpuAdvancedStatus",FontSize=12,TextWrapping=TextWrapping.Wrap,Foreground=tileTextBrush};
            var context=new TextBlock{Text=(onAc?global::XboxGamingBar.OnexStrings.Get("Plugged in"):global::XboxGamingBar.OnexStrings.Get("Battery"))+" · "+OnexPresetName(OnexActiveProfile)+global::XboxGamingBar.OnexStrings.Get("\nИзменения сохраняются автоматически. OS — вернуть исходное значение Windows."),FontSize=12,TextWrapping=TextWrapping.Wrap,Foreground=tileTextBrush};panel.Children.Add(context);
            var controls=new List<Control>();var rows=new List<Control[]>();
            var sliders=new Dictionary<string,Slider>();var readouts=new Dictionary<string,TextBlock>();
            var pending=new Dictionary<string,JsonValue>();int? pendingBoost=null,pendingScheduling=null;
            var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(250)};
            var boost=OnexSettingsChoice(global::XboxGamingBar.OnexStrings.Get("Boost mode"));boost.Name="OnexCpuAdvancedBoost";boost.Header=OnexHelpHeader(global::XboxGamingBar.OnexStrings.Get("Boost mode"),OnexBoostHelp);
            boost.ItemsSource=new[]{"Disabled","Enabled","Aggressive","Efficient enabled","Efficient aggressive","Aggressive at guaranteed","Efficient aggressive at guaranteed"}.Select(OnexStrings.Get).ToArray();
            var scheduling=OnexSettingsChoice(global::XboxGamingBar.OnexStrings.Get("Core preference"));scheduling.Name="OnexCpuAdvancedScheduling";scheduling.Header=OnexHelpHeader(global::XboxGamingBar.OnexStrings.Get("Core preference"),OnexCoreHelp);
            scheduling.ItemsSource=new[]{"All cores","Only performance cores","Prefer performance cores","Only efficiency cores","Prefer efficiency cores","Automatic"}.Select(OnexStrings.Get).ToArray();
            foreach(var choice in new[]{boost,scheduling}){ApplyOnexTileWindowControlAccent(choice);OnexInputGuard.Wire(choice);controls.Add(choice);}rows.Add(new Control[]{boost,scheduling});
            panel.Children.Add(OnexSettingsCard(OnexSettingsColumns(boost,scheduling)));
            bool Valid()=>!disposed&&expectedProfile==OnexFrontend.ProfileId;
            JsonObject Policy(JsonObject value){var token=value.GetNamedValue("policy",JsonValue.CreateNullValue());return token.ValueType==JsonValueType.Object?token.GetObject():null;}
            bool HasDraft()=>pending.Count>0||pendingBoost.HasValue||pendingScheduling.HasValue;
            void Schedule(){if(!Valid())return;status.Text=global::XboxGamingBar.OnexStrings.Get("Ожидает применения…");timer.Stop();timer.Start();}
            void RefreshBoost(JsonObject cpu)
            {
                bool before=updating;updating=true;
                try{if(!pendingBoost.HasValue)boost.SelectedIndex=(int)cpu.GetNamedNumber(onAc?"boostAc":"boostDc");if(!pendingScheduling.HasValue)scheduling.SelectedIndex=(int)cpu.GetNamedNumber(onAc?"schedulingAc":"schedulingDc");}
                finally{updating=before;}
            }
            void Refresh(JsonObject next)
            {
                if(!next.GetNamedBoolean("available",false))throw new InvalidOperationException(next.GetNamedString("error","CPU settings unavailable"));
                if(next.GetNamedBoolean("onAc")!=onAc||next.GetNamedString("scheme")!=scheme)throw new InvalidOperationException(global::XboxGamingBar.OnexStrings.Get("Источник питания или план Windows изменился. Открой CPU заново."));
                state=next;var fields=state.GetNamedObject("fields");var policy=Policy(state);bool before=updating;updating=true;
                try
                {
                    foreach(var pair in sliders)
                    {
                        var field=fields.GetNamedObject(pair.Key);var slider=pair.Value;
                        string key=field.GetNamedString("name");key=char.ToLowerInvariant(key[0])+key.Substring(1);
                        var selected=policy?.GetNamedValue(key,JsonValue.CreateNullValue());bool managed=selected?.ValueType==JsonValueType.Number;
                        if(!pending.ContainsKey(key)&&!OnexInputGuard.IsEditing(slider))slider.Value=managed?selected.GetNumber():(field.GetNamedBoolean("frequency")?0:-5);
                        slider.IsEnabled=field.GetNamedBoolean("available",false);string unit=field.GetNamedBoolean("frequency")?" MHz":"%";
                        string Actual(string name){double value=field.GetNamedNumber(name);return field.GetNamedBoolean("frequency")&&value==0?OnexStrings.Get("No cap"):value+unit;}
                        readouts[pair.Key].Text=slider.IsEnabled?global::XboxGamingBar.OnexStrings.Format("Windows: {0} · исходное: {1}",Actual("value"),Actual("osValue")):global::XboxGamingBar.OnexStrings.Get("Недоступно на этом CPU");
                    }
                }
                finally{updating=before;}
            }
            async Task Flush()
            {
                timer.Stop();if(!Valid()||writing||!HasDraft())return;
                var patch=pending.ToArray();pending.Clear();int? nextBoost=pendingBoost,nextScheduling=pendingScheduling;pendingBoost=null;pendingScheduling=null;
                writing=true;_onexPerformanceCpuApplying=true;status.Text=global::XboxGamingBar.OnexStrings.Get("Применение…");
                try
                {
                    var read=await App.SendMessageAsync(new ValueSet{{"OnexCpuTuningGet",true}});
                    if(!Valid())throw new InvalidOperationException(global::XboxGamingBar.OnexStrings.Get("Профиль сменился; изменение отменено."));
                    var fresh=JsonObject.Parse((string)read["OnexCpuTuningState"]);
                    if(!fresh.GetNamedBoolean("available",false)||fresh.GetNamedBoolean("onAc")!=onAc||fresh.GetNamedString("scheme")!=scheme)throw new InvalidOperationException(global::XboxGamingBar.OnexStrings.Get("Источник питания или план Windows изменился. Открой CPU заново."));
                    var policy=Policy(fresh);policy=policy==null?new JsonObject():JsonObject.Parse(policy.Stringify());
                    foreach(var change in patch)policy[change.Key]=change.Value;
                    var request=new ValueSet{{"OnexCpuTuningSet",policy.Stringify()},{"OnexProfileId",expectedProfile},{"OnexCpuTuningOnAc",onAc},{"OnexCpuTuningScheme",scheme}};
                    if(nextBoost.HasValue)request["OnexCpuTuningBoostMode"]=nextBoost.Value;
                    if(nextScheduling.HasValue)request["OnexCpuTuningScheduling"]=nextScheduling.Value;
                    var reply=await SendOnexHomeRequestAsync(request);
                    if(!Valid())return;
                    Refresh(JsonObject.Parse((string)reply["OnexCpuTuningState"]));ApplyOnexWindowsPowerState((string)reply["OnexWindowsPowerState"]);RefreshBoost(_onexWindowsPower.GetNamedObject("cpu"));status.Text=global::XboxGamingBar.OnexStrings.Get("Сохранено в текущий профиль.");
                }
                catch(Exception error){status.Text=error.Message;}
                finally{writing=false;_onexPerformanceCpuApplying=false;ApplyOnexAvailability();if(Valid()&&HasDraft())Schedule();}
            }
            timer.Tick+=async(s,e)=>await Flush();
            boost.SelectionChanged+=(s,e)=>{if(!updating&&OnexInputGuard.IsActive(boost)){pendingBoost=boost.SelectedIndex;Schedule();}};
            scheduling.SelectionChanged+=(s,e)=>{if(!updating&&OnexInputGuard.IsActive(scheduling)){pendingScheduling=scheduling.SelectedIndex;Schedule();}};
            var pColumn=new StackPanel{Spacing=12};var eColumn=new StackPanel{Spacing=12};
            pColumn.Children.Add(new TextBlock{Text=global::XboxGamingBar.OnexStrings.Format("Performance cores · {0}",state.GetNamedNumber("performanceCores")),FontWeight=FontWeights.SemiBold,FontSize=15});
            eColumn.Children.Add(new TextBlock{Text=global::XboxGamingBar.OnexStrings.Format("Efficiency cores · {0}",state.GetNamedNumber("efficiencyCores")),FontWeight=FontWeights.SemiBold,FontSize=15});
            var descriptions=new[]{
                Tuple.Create("freq",OnexStrings.Get("Max Frequency (MHz)"),global::XboxGamingBar.OnexStrings.Get("Верхняя граница частоты этой группы ядер. OS возвращает исходный лимит Windows; это не фиксированная частота и не обещание FPS.\n\nПример: 3500 MHz ограничивает разгон P-ядер до 3,5 ГГц. В лёгкой игре это может снизить нагрев, а в игре с нагрузкой на CPU — уменьшить FPS. Режим питания и драйвер могут дополнительно ограничивать действие.")),
                Tuple.Create("max",OnexStrings.Get("Maximum Processor State"),global::XboxGamingBar.OnexStrings.Get("Максимальная производительность группы ядер в процентах. Это не процент загрузки CPU. OS возвращает исходную настройку.\n\nПример: снижение со 100% до 90% может ограничить скорость CPU и расход энергии. Точная частота зависит от процессора и Windows.")),
                Tuple.Create("min",OnexStrings.Get("Minimum Processor State"),global::XboxGamingBar.OnexStrings.Get("Нижняя граница производительности группы ядер. Большое значение может удерживать более высокую скорость и тратить батарею в простое.\n\nПример: 5% разрешает Windows сильнее снижать скорость, когда игра ждёт GPU.")),
                Tuple.Create("epp",OnexStrings.Get("Energy Performance Preference"),global::XboxGamingBar.OnexStrings.Get("Предпочтение между скоростью и экономией: 0 — быстрее реагировать, 100 — сильнее экономить. Это не ограничение загрузки и не гарантированный прирост FPS.\n\nПример: 80 на E-ядрах позволяет Windows экономнее выполнять фоновые задачи. OS возвращает значение до настройки ONEXTweaks.")),
                Tuple.Create("parkMin",OnexStrings.Get("Cores Kept Awake · minimum"),global::XboxGamingBar.OnexStrings.Get("Минимальная доля ядер этой группы, которую Windows должна держать доступной без парковки. Это не загрузка ядер.\n\nПример: 50% оставляет доступной минимум половину группы. 100% может сократить задержки пробуждения, но увеличить расход энергии.")),
                Tuple.Create("parkMax",OnexStrings.Get("Cores Kept Awake · maximum"),global::XboxGamingBar.OnexStrings.Get("Максимальная доля ядер группы, которую Windows разрешено распарковать. Значение не должно быть ниже минимума.\n\nПример: 100% разрешает использовать все ядра, а низкий потолок может вызвать просадки FPS и рывки. OS возвращает исходную настройку."))};
            foreach(var description in descriptions)
            {
                var row=new List<Control>();
                foreach(var suffix in new[]{"1","0"})
                {
                    string alias=description.Item1+suffix;var field=state.GetNamedObject("fields").GetNamedObject(alias);bool frequency=field.GetNamedBoolean("frequency");
                    string key=field.GetNamedString("name");key=char.ToLowerInvariant(key[0])+key.Substring(1);
                    var item=new StackPanel{Spacing=2};var header=OnexHelpHeader(description.Item2,description.Item3);foreach(var text in ((Panel)header).Children.OfType<TextBlock>())text.MaxWidth=215;
                    item.Children.Add(header);
                    var chosen=new TextBlock{HorizontalAlignment=HorizontalAlignment.Right,Foreground=OnexDialogAccent,FontSize=12};item.Children.Add(chosen);
                    var slider=new Slider{Name="OnexCpuAdvanced"+alias,Minimum=frequency?0:-5,Maximum=frequency?Math.Max(6000,field.GetNamedNumber("value")):100,StepFrequency=frequency?100:1,SmallChange=frequency?100:5,LargeChange=frequency?500:5,Height=36,IsThumbToolTipEnabled=false};
                    var readout=new TextBlock{FontSize=10,Foreground=tileTextBrush,TextWrapping=TextWrapping.Wrap,Opacity=.8};
                    void Caption(){chosen.Text=(frequency?slider.Value==0:slider.Value<0)?global::XboxGamingBar.OnexStrings.Get("Leave to OS"):((int)slider.Value)+(frequency?global::XboxGamingBar.OnexStrings.Get(" MHz"):"%");}
                    void Commit(){if(updating||!Valid())return;pending[key]=(frequency?slider.Value==0:slider.Value<0)?JsonValue.CreateNullValue():JsonValue.CreateNumberValue((int)slider.Value);Schedule();}
                    if(frequency)OnexInputGuard.Wire(slider,Commit);else{OnexInputGuard.ConfigurePercentage(slider);OnexInputGuard.Wire(slider,Commit);}
                    OnexInputGuard.SetCommit(slider,Commit);slider.ValueChanged+=(s,e)=>{Caption();if(!updating&&OnexInputGuard.IsActive(slider))Commit();};
                    item.Children.Add(slider);item.Children.Add(readout);sliders.Add(alias,slider);readouts.Add(alias,readout);row.Add(slider);controls.Add(slider);
                    (suffix=="1"?pColumn:eColumn).Children.Add(item);Caption();
                }
                rows.Add(row.ToArray());
            }
            panel.Children.Add(OnexSettingsColumns(OnexSettingsCard(pColumn),OnexSettingsCard(eColumn)));
            var hint=new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("Геймпад: A — начать/подтвердить, ←/→ — изменить, ↑/↓ — применить и перейти, B — отменить. Проценты: 5% с геймпада, 1% пальцем. Частота: 100 MHz.\nОграничения Windows могут работать иначе в Best performance. Числа ниже ползунков показывают прочитанные настройки, а не измеренную частоту CPU."),FontSize=12,TextWrapping=TextWrapping.Wrap,Foreground=tileTextBrush};panel.Children.Add(hint);
            int oemClock=(int)(_onexHardwareHome?.GetNamedNumber("cpuClockMhz",0)??0);
            if(oemClock>0)panel.Children.Add(new TextBlock{Text=global::XboxGamingBar.OnexStrings.Format("Также действует общий OEM-лимит {0} MHz, заданный в меню CPU Boost.",oemClock),FontSize=12,TextWrapping=TextWrapping.Wrap,Foreground=tileTextBrush});
            var fetch=new Button{Name="OnexCpuAdvancedFetch",Content=global::XboxGamingBar.OnexStrings.Get("Fetch Windows State"),Style=OnexSettingsButtonStyle(false)};
            var reset=new Button{Name="OnexCpuAdvancedReset",Content=global::XboxGamingBar.OnexStrings.Get("Reset to OS · Boost Off"),Style=OnexSettingsButtonStyle(false)};
            panel.Children.Add(OnexSettingsColumns(fetch,reset));controls.Add(fetch);controls.Add(reset);rows.Add(new Control[]{fetch,reset});panel.Children.Add(status);
            fetch.Click+=async(s,e)=>{if(writing||!Valid())return;await Flush();try{var reply=await App.SendMessageAsync(new ValueSet{{"OnexCpuTuningGet",true}});if(!Valid())return;Refresh(JsonObject.Parse((string)reply["OnexCpuTuningState"]));ApplyOnexWindowsPowerState((string)reply["OnexWindowsPowerState"]);RefreshBoost(_onexWindowsPower.GetNamedObject("cpu"));status.Text=global::XboxGamingBar.OnexStrings.Get("Считано из Windows.");}catch(Exception error){status.Text=error.Message;}};
            reset.Click+=async(s,e)=>{if(writing||!Valid())return;foreach(var field in state.GetNamedObject("fields")){string key=field.Value.GetObject().GetNamedString("name");key=char.ToLowerInvariant(key[0])+key.Substring(1);pending[key]=JsonValue.CreateNullValue();}pendingBoost=0;await Flush();};
            for(int r=0;r<rows.Count;r++)for(int c=0;c<rows[r].Length;c++)
            {var control=rows[r][c];if(r>0)control.XYFocusUp=rows[r-1][Math.Min(c,rows[r-1].Length-1)];if(r+1<rows.Count)control.XYFocusDown=rows[r+1][Math.Min(c,rows[r+1].Length-1)];if(rows[r].Length>1){control.XYFocusLeft=rows[r][0];control.XYFocusRight=rows[r][1];}}
            RefreshBoost(_onexWindowsPower.GetNamedObject("cpu"));Refresh(state);updating=false;status.Text=global::XboxGamingBar.OnexStrings.Get("Настройки Windows считаны. Изменения ещё не выполнялись.");
            void Cancel(){disposed=true;timer.Stop();pending.Clear();pendingBoost=null;pendingScheduling=null;foreach(var slider in sliders.Values)OnexInputGuard.Cancel(slider);}
            panel.Unloaded+=(s,e)=>Cancel();
            return new OnexCpuSettingsForm{Panel=panel,NavigationControls=controls.ToArray(),NavigationRows=rows.ToArray(),Apply=Flush,Refresh=RefreshBoost,Cancel=Cancel,Pending=()=>writing||HasDraft(),AutoSave=true,OnAc=onAc,ProfileId=expectedProfile};
        }
    }
}
