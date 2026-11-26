// SPDX-License-Identifier: AGPL-3.0-only
using System;
using ONEXTweaks.Power;
using System.Collections.Generic;
using System.Linq;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private Slider AddOnexSourceSlider(OnexSourceForm form,OnexSourceCard card,Panel host,string key,string title,int min,int max,int step,bool percent=false,bool nullable=false,string help=null,Grid track=null)
        {
            var panel=new StackPanel{Spacing=3};panel.Children.Add(help==null?new TextBlock{Text=OnexStrings.Get(title),TextWrapping=TextWrapping.Wrap,FontSize=13}:(UIElement)OnexHelpHeader(title,help));
            bool power=key=="pl1"||key=="pl2";
            var value=new TextBlock{FontSize=12,Foreground=power?OnexSolidBrush((_onexAppliedTheme??OnexPalette.CreateTheme()).TextPrimary):OnexDialogAccent,HorizontalAlignment=HorizontalAlignment.Right};
            if(power){value.Name="Onex"+form.Source+"_"+key+"Value";OnexPowerReadout.SetIsNeutral(value,true);}panel.Children.Add(value);
            var slider=new Slider{Name="Onex"+form.Source+"_"+key.Replace(':','_'),Minimum=min,Maximum=max,StepFrequency=step,SmallChange=percent?5:step,LargeChange=percent?5:step,Style=(Style)Resources["OnexPreciseSliderStyle"],Height=36,IsThumbToolTipEnabled=false};
            if(percent)OnexInputGuard.ConfigurePercentage(slider);
            void Caption(){value.Text=nullable&&(slider.Value<0||key.StartsWith("cpu:freq")&&slider.Value==0)?global::XboxGamingBar.OnexStrings.Get("Leave to Windows"):((int)slider.Value)+(key.StartsWith("cpu:freq")||key=="clockMhz"?global::XboxGamingBar.OnexStrings.Get(" MHz"):percent?"%":key=="pl1"||key=="pl2"?global::XboxGamingBar.OnexStrings.Get(" W"):global::XboxGamingBar.OnexStrings.Get(" FPS"));}
            void Commit(){if(form.Updating)return;if(key=="clockMhz"&&slider.Value>0&&slider.Value<1000)slider.Value=1000;QueueOnexSource(form,key,nullable&&(slider.Value<0||key.StartsWith("cpu:freq")&&slider.Value==0)?JsonValue.CreateNullValue():JsonValue.CreateNumberValue((int)slider.Value));}
            OnexInputGuard.Wire(slider,Commit);OnexInputGuard.SetCommit(slider,Commit);slider.ValueChanged+=(s,e)=>{Caption();if(!form.Updating&&OnexInputGuard.IsActive(slider))Commit();};
            form.Updates.Add(values=>{if(!OnexInputGuard.IsEditing(slider)&&!form.Pending.ContainsKey(key)&&!form.InFlight.Contains(key)){var token=OnexSourceValue(values,key);slider.Value=token.ValueType==JsonValueType.Number?token.GetNumber():nullable?min:min;}Caption();});
            if(track!=null)
            {
                track.Children.Add(slider);
                panel.Children.Add(track);
            }
            else panel.Children.Add(slider);
            host.Children.Add(panel);form.Hosts[key]=panel;form.Sliders.Add(slider);card.Rows.Add(new Control[]{slider});return slider;
        }
        private ToggleSwitch AddOnexSourceToggle(OnexSourceForm form,OnexSourceCard card,string key,string title,Func<JsonObject,bool> read,Func<bool,IJsonValue> encode)
        {
            var toggle=new ToggleSwitch{Name="Onex"+form.Source+"_"+key+"Toggle",Header=OnexStrings.Get(title),OnContent="",OffContent=""};OnexInputGuard.Wire(toggle);ApplyOnexTileWindowControlAccent(toggle);
            toggle.Toggled+=(s,e)=>{if(!form.Updating&&OnexInputGuard.ConsumeToggleInput(toggle))QueueOnexSource(form,key,encode(toggle.IsOn));};
            form.Updates.Add(values=>{if(!form.Pending.ContainsKey(key)&&!form.InFlight.Contains(key))toggle.IsOn=read(values);});card.Body.Children.Add(toggle);card.Rows.Add(new Control[]{toggle});return toggle;
        }
        private ComboBox AddOnexSourceChoice(OnexSourceForm form,OnexSourceCard card,Panel host,string key,string title,Func<IEnumerable<Tuple<string,object>>> options,string help=null)
        {
            var choice=OnexSettingsChoice(title);choice.Name="Onex"+form.Source+"_"+key;choice.HorizontalAlignment=HorizontalAlignment.Stretch;if(help!=null)choice.Header=OnexHelpHeader(title,help);ApplyOnexTileWindowControlAccent(choice);OnexInputGuard.Wire(choice);
            choice.SelectionChanged+=(s,e)=>{if(form.Updating||!OnexInputGuard.IsActive(choice)||!(choice.SelectedItem is ComboBoxItem item))return;QueueOnexSource(form,key,item.Tag is string text?JsonValue.CreateStringValue(text):JsonValue.CreateNumberValue(Convert.ToInt32(item.Tag)));};
            form.Updates.Add(values=>{var token=OnexSourceValue(values,key);object selected=token.ValueType==JsonValueType.String?token.GetString():token.ValueType==JsonValueType.Number?(object)(int)token.GetNumber():null;if(!form.Pending.ContainsKey(key)&&!form.InFlight.Contains(key))SyncOnexPerfChoice(choice,options(),selected);});host.Children.Add(choice);card.Rows.Add(new Control[]{choice});return choice;
        }
        private static IEnumerable<Tuple<string,object>> OnexSourceOptions(string[] names,int start=0)=>names.Select((name,index)=>Tuple.Create(OnexStrings.Get(name),(object)(index+start)));
        private void BuildOnexSourceControls(OnexSourceForm form)
        {
            var tdp=form.Cards.Single(card=>card.Id=="tdp");
            var sustainedTrack=new Grid();
            var boostTrack=new Grid();
            var sustainedSlider=AddOnexSourceSlider(form,tdp,tdp.Body,"pl1",global::XboxGamingBar.OnexStrings.Get("PL1 · Sustained power"),8,40,1,
                help:global::XboxGamingBar.OnexStrings.Get("Длительный лимит общего пакета CPU/GPU. Например, 12 W обычно экономнее 20 W; фактическое потребление зависит от нагрузки."),track:sustainedTrack);
            var pl2=AddOnexSourceToggle(form,tdp,"separatePl2",global::XboxGamingBar.OnexStrings.Get("Separate PL2"),values=>OnexSourceBool(values,"separatePl2",OnexSourceNumber(values,"pl2")>OnexSourceNumber(values,"pl1")),value=>JsonValue.CreateBooleanValue(value));
            int boundsPosition=form.Updates.Count;
            var boost=AddOnexSourceSlider(form,tdp,tdp.Body,"pl2",global::XboxGamingBar.OnexStrings.Get("PL2 · Short boost power"),8,41,1,
                help:global::XboxGamingBar.OnexStrings.Get("Off: PL2 = PL1 + 1 W. At PL1 40 W, manual PL2 goes up to 52 W."),track:boostTrack);
            var scale=new OnexPowerScale(sustainedTrack,sustainedSlider,boostTrack,boost,12,
                (Windows.UI.Xaml.Media.Brush)Resources["OnexInactiveSliderTrackBrush"]);
            pl2.Toggled+=(s,e)=>{form.Hosts["pl2"].Visibility=boost.Visibility=pl2.IsOn?Visibility.Visible:Visibility.Collapsed;scale.Update(pl2.IsOn);WireOnexSourceFocus(form);};
            void UpdateBounds(int sustained)
            {
                boost.Maximum=Pl2ControlPolicy.ManualCeiling(sustained);
                boost.Minimum=pl2.IsOn?sustained:Pl2ControlPolicy.MinimumPl1;
                scale.Update(pl2.IsOn);
            }
            sustainedSlider.ValueChanged+=(s,e)=>
            {
                if(form.Updating||!pl2.IsOn||!OnexInputGuard.IsEditing(sustainedSlider))return;
                form.Updating=true;
                try
                {
                    int sustained=(int)e.NewValue;
                    int manual=(int)OnexSourceNumber(form.Values,"pl2",boost.Value);
                    UpdateBounds(sustained);
                    boost.Value=Pl2ControlPolicy.AdjustManual(sustained,manual);
                }
                finally{form.Updating=false;}
            };
            form.Updates.Insert(boundsPosition, values =>
            {
                int sustained = OnexInputGuard.IsEditing(sustainedSlider)
                    ? (int)sustainedSlider.Value : (int)OnexSourceNumber(values, "pl1", 8);
                UpdateBounds(sustained);
                form.Hosts["pl2"].Visibility = boost.Visibility = pl2.IsOn ? Visibility.Visible : Visibility.Collapsed;
            });
            var cpu=form.Cards.Single(card=>card.Id=="cpu");int lastBoost=4;
            AddOnexSourceToggle(form,cpu,"boost",global::XboxGamingBar.OnexStrings.Get("CPU Boost"),values=>{int mode=(int)OnexSourceNumber(values,"boost");if(mode>0)lastBoost=mode;return mode>0;},enabled=>JsonValue.CreateNumberValue(enabled?lastBoost:0));
            AddOnexSourceChoice(form,cpu,cpu.Body,"boost",global::XboxGamingBar.OnexStrings.Get("Boost mode"),()=>OnexSourceOptions(new[]{"Disabled","Enabled","Aggressive","Efficient enabled","Efficient aggressive","Aggressive at guaranteed","Efficient aggressive at guaranteed"}),OnexBoostHelp);
            AddOnexSourceChoice(form,cpu,cpu.Body,"scheduling",global::XboxGamingBar.OnexStrings.Get("Core preference"),()=>OnexSourceOptions(new[]{"All cores","Only performance cores","Prefer performance cores","Only efficiency cores","Prefer efficiency cores","Automatic"}),OnexCoreHelp);
            AddOnexSourceSlider(form,cpu,cpu.Body,"clockMhz",global::XboxGamingBar.OnexStrings.Get("OEM frequency cap · 0 = Off"),0,6000,100,help:global::XboxGamingBar.OnexStrings.Get("Общий OEM-лимит частоты. 0 выключает его; рабочие ненулевые значения — от 1000 MHz. Например, 3500 MHz ограничивает максимальную частоту, а не фиксирует её."));
            var columns=new[]{new StackPanel{Spacing=10},new StackPanel{Spacing=10}};
            columns[0].Children.Add(new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("Performance cores"),FontSize=15,FontWeight=Windows.UI.Text.FontWeights.SemiBold});columns[1].Children.Add(new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("Efficiency cores"),FontSize=15,FontWeight=Windows.UI.Text.FontWeights.SemiBold});
            var fields=new[]{Tuple.Create("freq",OnexStrings.Get("Max Frequency"),global::XboxGamingBar.OnexStrings.Get("Верхняя граница частоты. Leave to Windows убирает эту настройку ONEXTweaks. Например, 3500 MHz может уменьшить нагрев, но ограничить FPS в CPU-зависимой игре.")),Tuple.Create("max",OnexStrings.Get("Maximum Processor State"),global::XboxGamingBar.OnexStrings.Get("Максимальная производительность группы ядер; это не загрузка CPU. Например, 90% может ограничить скорость и потребление. Leave to Windows возвращает управление системе.")),Tuple.Create("min",OnexStrings.Get("Minimum Processor State"),global::XboxGamingBar.OnexStrings.Get("Нижняя граница производительности. Например, 5% разрешает Windows сильнее снижать частоту в простое; высокое значение может увеличить расход батареи.")),Tuple.Create("epp",OnexStrings.Get("Energy Performance Preference"),global::XboxGamingBar.OnexStrings.Get("0 — приоритет скорости, 100 — экономии. Например, 80 на E-ядрах помогает экономнее выполнять фоновые задачи. Это не процент загрузки CPU.")),Tuple.Create("parkMin",OnexStrings.Get("Cores Kept Awake · minimum"),global::XboxGamingBar.OnexStrings.Get("Минимальная доля распаркованных ядер. Например, 50% оставляет доступной половину группы. Высокое значение может уменьшить задержки, но увеличить расход энергии.")),Tuple.Create("parkMax",OnexStrings.Get("Cores Kept Awake · maximum"),global::XboxGamingBar.OnexStrings.Get("Максимальная доля распаркованных ядер. 100% разрешает использовать все. Значение не может быть ниже минимума; слишком низкий потолок может вызывать рывки."))};
            foreach(var field in fields)
            {
                int first=cpu.Rows.Count;var pair=new List<Control>();for(int c=0;c<2;c++)pair.Add(AddOnexSourceSlider(form,cpu,columns[c],"cpu:"+field.Item1+(c==0?"1":"0"),field.Item2,field.Item1=="freq"?0:-5,field.Item1=="freq"?6000:100,field.Item1=="freq"?100:1,field.Item1!="freq",true,field.Item3));
                cpu.Rows.RemoveRange(first,2);cpu.Rows.Add(pair.ToArray());
            }
            cpu.Body.Children.Add(OnexSettingsColumns(OnexSettingsCard(columns[0]),OnexSettingsCard(columns[1])));
            cpu.Body.Children.Add(new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("Leave to Windows — без ограничения ONEXTweaks. Частоты и поведение зависят от режима Windows и драйвера. Геймпад: A — редактировать/подтвердить; ←/→ — изменить; ↑/↓ — сохранить и перейти; B — отменить."),FontSize=12,TextWrapping=TextWrapping.Wrap,Foreground=tileTextBrush});
            var fetch=new Button{Content=global::XboxGamingBar.OnexStrings.Get("Read Windows State"),Style=OnexSettingsButtonStyle(false)};var reset=new Button{Content=global::XboxGamingBar.OnexStrings.Get("Reset to Windows · Boost Off"),Style=OnexSettingsButtonStyle(false)};
            fetch.Click+=async(s,e)=>
            {
                try
                {
                    var reply=await App.SendMessageAsync(new ValueSet{{"OnexCpuTuningGet",true}});var actual=JsonObject.Parse((string)reply["OnexCpuTuningState"]);
                    if(_onexDetailsDialog!=null)return;var values=new StackPanel{Spacing=6};
                    foreach(var entry in actual.GetNamedObject("fields"))
                    {var field=entry.Value.GetObject();string alias=entry.Key;string label=(alias.EndsWith("1")?"P cores · ":"E cores · ")+field.GetNamedString("name");var token=field.GetNamedValue("value",JsonValue.CreateNullValue());string actualText=token.ValueType==JsonValueType.Number?token.GetNumber()+(field.GetNamedBoolean("frequency")?" MHz":"%"):"Unavailable";values.Children.Add(new TextBlock{Text=label+": "+actualText,FontSize=13,TextWrapping=TextWrapping.Wrap});}
                    var dialog=CreateOnexSettingsDialog(global::XboxGamingBar.OnexStrings.Get("Windows CPU State"),(actual.GetNamedBoolean("onAc",false)?global::XboxGamingBar.OnexStrings.Get("AC · зарядка"):global::XboxGamingBar.OnexStrings.Get("DC · батарея"))+global::XboxGamingBar.OnexStrings.Get(". Это текущие значения Windows; сохранённые настройки профиля не меняются."),values);dialog.PrimaryButtonText="";dialog.CloseButtonText=global::XboxGamingBar.OnexStrings.Get("Close");_onexDetailsDialog=dialog;try{await dialog.ShowAsync();}finally{if(ReferenceEquals(_onexDetailsDialog,dialog))_onexDetailsDialog=null;}
                }
                catch(Exception error){form.Status.Text=error.Message;}
            };
            reset.Click+=(s,e)=>QueueOnexSource(form,"cpuReset",JsonValue.CreateBooleanValue(true));cpu.Body.Children.Add(OnexSettingsColumns(fetch,reset));cpu.Rows.Add(new Control[]{fetch,reset});
            var modeCard=form.Cards.Single(card=>card.Id=="mode");AddOnexSourceChoice(form,modeCard,modeCard.Body,"powerMode",global::XboxGamingBar.OnexStrings.Get("OS Power Mode"),()=>OnexSourceOptions(new[]{OnexStrings.Get("Best power efficiency"),"Balanced",OnexStrings.Get("Best performance")}),OnexPowerModeHelp);
            var fps=form.Cards.Single(card=>card.Id=="fps");AddOnexSourceToggle(form,fps,"fpsEnabled",global::XboxGamingBar.OnexStrings.Get("FPS Limit"),values=>OnexSourceBool(values,"fpsEnabled"),enabled=>JsonValue.CreateBooleanValue(enabled));AddOnexSourceChoice(form,fps,fps.Body,"fpsProvider",global::XboxGamingBar.OnexStrings.Get("Limiter"),()=>new[]{Tuple.Create("Intel",(object)"Intel"),Tuple.Create("RTSS",(object)"RTSS")});AddOnexSourceSlider(form,fps,fps.Body,"fpsValue",global::XboxGamingBar.OnexStrings.Get("FPS"),30,300,1);
            fps.Body.Children.Add(new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("Intel использует лимитер драйвера; RTSS — свой лимитер. Например, 60 FPS ограничивает скорость кадров, если выбранный способ поддерживает эту игру."),TextWrapping=TextWrapping.Wrap,FontSize=12,Foreground=tileTextBrush});
            var intel=form.Cards.Single(card=>card.Id=="intel");
            var intelDefinitions=new[]{Tuple.Create("frameGeneration","Frame Gen",new[]{"App Choice","2X","3X","4X"}),Tuple.Create("vrr","VRR",new[]{"Off","On"}),Tuple.Create("vrrMode","VRR mode",new[]{"Fullscreen only","Full + Windowed"}),Tuple.Create("lowLatency","Low Latency",new[]{"Off","On","On + Boost"}),Tuple.Create("frameSync","Frame Sync",new[]{"Application controlled","V-Sync Off","V-Sync On","Smooth Sync","Speed Sync"}),Tuple.Create("scalingMode","Scaling Mode",new[]{"GPU Scaling","Retro Scaling"}),Tuple.Create("scalingMethod","Scaling Method",new[]{"Centered","Stretch","Preserve Aspect Ratio"})};
            foreach(var range in new[]{Tuple.Create(0,3),Tuple.Create(3,2),Tuple.Create(5,2)})
            {
                var grid=new Grid{ColumnSpacing=12};var row=new List<Control>();int first=intel.Rows.Count;
                for(int column=0;column<range.Item2;column++)
                {
                    grid.ColumnDefinitions.Add(new ColumnDefinition());var host=new StackPanel{Spacing=4};Grid.SetColumn(host,column);grid.Children.Add(host);var entry=intelDefinitions[range.Item1+column];string key=entry.Item1;
                    row.Add(AddOnexSourceChoice(form,intel,host,key,entry.Item2,()=>OnexSourceOptions(key=="scalingMethod"?(OnexSourceNumber(form.Values,"scalingMode",1)==2?new[]{"Integer","Nearest neighbour"}:new[]{"Centered","Stretch","Preserve Aspect Ratio"}):entry.Item3,key=="scalingMode"?1:0)));
                }
                intel.Rows.RemoveRange(first,range.Item2);intel.Rows.Add(row.ToArray());intel.Body.Children.Add(OnexSettingsCard(grid));
            }
            intel.Body.Children.Add(new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("Frame Gen может требовать перезапуска игры. VRR зависит от поддержки дисплея и разрешения; масштабирование заметно при разрешении ниже родного. Эти настройки не выбирают и не изменяют Screen Color Profile."),FontSize=12,TextWrapping=TextWrapping.Wrap,Foreground=tileTextBrush});
            BuildOnexSourceModes(form,form.Cards.Single(card=>card.Id=="resolution"));
        }
        private void BuildOnexSourceModes(OnexSourceForm form,OnexSourceCard card)
        {
            var resolution=OnexSettingsChoice(global::XboxGamingBar.OnexStrings.Get("Resolution"));var refresh=OnexSettingsChoice(global::XboxGamingBar.OnexStrings.Get("Refresh Rate"));resolution.Name="Onex"+form.Source+"_resolution";refresh.Name="Onex"+form.Source+"_refresh";
            foreach(var choice in new[]{resolution,refresh}){OnexInputGuard.Wire(choice);ApplyOnexTileWindowControlAccent(choice);}card.Body.Children.Add(OnexSettingsColumns(resolution,refresh));card.Rows.Add(new Control[]{resolution,refresh});
            JsonObject[] Modes()=>_onexDisplayState?.GetNamedObject("modes",null)?.GetNamedArray("modes",null)?.Select(value=>value.GetObject()).ToArray()??new JsonObject[0];
            void Save(bool size)
            {
                if(form.Updating||!(size?OnexInputGuard.IsActive(resolution):OnexInputGuard.IsActive(refresh)))return;
                string chosen=(resolution.SelectedItem as ComboBoxItem)?.Tag as string;int hz=(int?)((refresh.SelectedItem as ComboBoxItem)?.Tag)??60;
                var mode=Modes().Where(value=>value.GetNamedNumber("width")+"x"+value.GetNamedNumber("height")==chosen).OrderBy(value=>Math.Abs(value.GetNamedNumber("hz")-hz)).FirstOrDefault();if(mode!=null)QueueOnexSource(form,"mode",JsonObject.Parse(mode.Stringify()));
            }
            resolution.SelectionChanged+=(s,e)=>Save(true);refresh.SelectionChanged+=(s,e)=>Save(false);
            form.Updates.Add(values=>
            {
                if(form.Pending.ContainsKey("mode")||form.InFlight.Contains("mode"))return;
                var token=OnexSourceValue(values,"mode");var mode=token.ValueType==JsonValueType.Object?token.GetObject():OnexDisplayObject(OnexDisplayObject(_onexDisplayState,"modes"),"current");if(mode==null)return;
                string size=mode.GetNamedNumber("width")+"x"+mode.GetNamedNumber("height");var modes=Modes();SyncOnexPerfChoice(resolution,modes.Select(value=>value.GetNamedNumber("width")+"x"+value.GetNamedNumber("height")).Distinct().Select(value=>Tuple.Create(value,(object)value)),size);SyncOnexPerfChoice(refresh,modes.Where(value=>value.GetNamedNumber("width")+"x"+value.GetNamedNumber("height")==size).Select(value=>(int)value.GetNamedNumber("hz")).Distinct().Select(value=>Tuple.Create(value+" Hz",(object)value)),(int)mode.GetNamedNumber("hz"));
            });
        }
        private void RefreshOnexSourceSummaries(OnexSourceForm form)
        {
            var value=form.Values;int mode=(int)OnexSourceNumber(value,"powerMode",1);var token=OnexSourceValue(value,"mode");var displayMode=token.ValueType==JsonValueType.Object?token.GetObject():null;
            string[] summaries={OnexSourceNumber(value,"pl1")+" W · PL2 "+(OnexSourceBool(value,"separatePl2",OnexSourceNumber(value,"pl2")>OnexSourceNumber(value,"pl1"))?OnexSourceNumber(value,"pl2")+" W":"Off"),"Boost "+(OnexSourceNumber(value,"boost")>0?"On":"Off")+" · "+new[]{"All cores","Only P","Prefer P","Only E","Prefer E","Windows"}[(int)OnexSourceNumber(value,"scheduling")],new[]{"Efficiency","Balanced","Performance"}[Math.Max(0,Math.Min(2,mode))],OnexSourceValue(value,"fpsProvider").GetString()+" · "+(OnexSourceBool(value,"fpsEnabled")?OnexSourceNumber(value,"fpsValue")+" FPS":"Off"),"Frame Gen "+new[]{"App Choice","2X","3X","4X"}[(int)OnexSourceNumber(value,"frameGeneration")]+" · VRR "+(OnexSourceNumber(value,"vrr")>0?"On":"Off"),displayMode==null?"Windows":displayMode.GetNamedNumber("width")+"×"+displayMode.GetNamedNumber("height")+" · "+displayMode.GetNamedNumber("hz")+" Hz"};
            for(int n=0;n<form.Cards.Count;n++){form.Cards[n].Summary.Text=summaries[n];form.Cards[n].Header.IsEnabled=App.IsConnected;}
        }
    }
}
