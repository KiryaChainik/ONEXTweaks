// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private sealed class OnexSourceCard
        {
            internal string Id;internal Button Header,Close;internal Border Detail;internal StackPanel Body;internal TextBlock Summary;
            internal readonly List<Control[]> Rows=new List<Control[]>();
        }
        private sealed class OnexSourceForm
        {
            internal string Source,ProfileId;internal bool Updating,Writing,Live;internal JsonObject Values;
            internal StackPanel Root;internal Grid Grid;internal TextBlock Status,Context;internal DispatcherTimer Timer;internal OnexSourceCard Open;
            internal readonly List<OnexSourceCard> Cards=new List<OnexSourceCard>();
            internal readonly List<Action<JsonObject>> Updates=new List<Action<JsonObject>>();
            internal readonly List<Slider> Sliders=new List<Slider>();
            internal readonly Dictionary<string,FrameworkElement> Hosts=new Dictionary<string,FrameworkElement>();
            internal readonly Dictionary<string,IJsonValue> Pending=new Dictionary<string,IJsonValue>();
            internal readonly HashSet<string> InFlight=new HashSet<string>();
        }
        private OnexSourceForm _onexDcEditor,_onexAcEditor;
        private bool OnexSourceEditorsPending=>new[]{_onexDcEditor,_onexAcEditor}.Any(form=>form!=null&&(form.Writing||form.Pending.Count>0||form.Sliders.Any(OnexInputGuard.IsPreviewing)));
        private static readonly Dictionary<string,string> OnexSourceCpuKeys=new Dictionary<string,string>{{"freq1","perfFrequency"},{"freq0","effFrequency"},{"max1","perfMaximum"},{"max0","effMaximum"},{"min1","perfMinimum"},{"min0","effMinimum"},{"epp1","perfEpp"},{"epp0","effEpp"},{"parkMin1","perfAwakeMinimum"},{"parkMin0","effAwakeMinimum"},{"parkMax1","perfAwakeMaximum"},{"parkMax0","effAwakeMaximum"}};
        private static IJsonValue OnexSourceValue(JsonObject values,string key)
        {
            if(values==null)return JsonValue.CreateNullValue();
            JsonObject host=values;string field=key;
            if(key.StartsWith("cpu:")){host=OnexDisplayObject(values,"cpuTuning");field=OnexSourceCpuKeys[key.Substring(4)];}
            else if(key.StartsWith("fps")){host=OnexDisplayObject(values,"fps");field=key=="fpsProvider"?"provider":key=="fpsEnabled"?"enabled":"value";}
            else if(new[]{"mode","frameGeneration","vrr","vrrMode","lowLatency","frameSync","scalingMode","scalingMethod"}.Contains(key))host=OnexDisplayObject(values,"display");
            return host!=null&&host.TryGetValue(field,out var value)?value:JsonValue.CreateNullValue();
        }
        private static double OnexSourceNumber(JsonObject values,string key,double fallback=0){var value=OnexSourceValue(values,key);return value.ValueType==JsonValueType.Number?value.GetNumber():fallback;}
        private static bool OnexSourceBool(JsonObject values,string key,bool fallback=false){var value=OnexSourceValue(values,key);return value.ValueType==JsonValueType.Boolean?value.GetBoolean():fallback;}
        private void EnsureOnexSourceEditors()
        {
            if(_onexDcEditor!=null||_onexPerformanceProfilePickerCard==null||_onexPerformanceProfileFooter==null||!(PerformanceScrollViewer.Content is Panel body))return;
            _onexDcEditor=CreateOnexSourceForm("dc","DC (Direct Current): Power from a battery");
            _onexAcEditor=CreateOnexSourceForm("ac","AC (Alternating Current): Power from a wall outlet");
            body.Children.Insert(body.Children.IndexOf(_onexPerformanceProfilePickerCard)+1,_onexDcEditor.Root);
            body.Children.Insert(body.Children.IndexOf(_onexPerformanceProfileFooter)+1,_onexAcEditor.Root);
            RefreshOnexSourceEditors();
        }
        private OnexSourceForm CreateOnexSourceForm(string source,string heading)
        {
            var form=new OnexSourceForm{Source=source,Root=new StackPanel{Name="Onex"+source.ToUpperInvariant()+"Editor",Spacing=10,Visibility=Visibility.Collapsed},Grid=new Grid{ColumnSpacing=12},Status=new TextBlock{FontSize=12,TextWrapping=TextWrapping.Wrap,Foreground=tileTextBrush},Context=new TextBlock{FontSize=12,TextWrapping=TextWrapping.Wrap,Foreground=tileTextBrush}};
            form.Root.Children.Add(new TextBlock{Text=OnexStrings.Get(heading),FontSize=17,FontWeight=Windows.UI.Text.FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});form.Root.Children.Add(form.Context);
            form.Grid.ColumnDefinitions.Add(new ColumnDefinition());form.Grid.ColumnDefinitions.Add(new ColumnDefinition());for(int n=0;n<8;n++)form.Grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});form.Root.Children.Add(form.Grid);form.Root.Children.Add(form.Status);
            foreach(var item in new[]{Tuple.Create("tdp","TDP Settings","\uE945"),Tuple.Create("cpu","CPU Settings","\uE950"),Tuple.Create("mode","OS Power Mode","\uE7E8"),Tuple.Create("fps","FPS Limit","\uE916"),Tuple.Create("intel","Intel Settings","\uE7F4"),Tuple.Create("resolution","Resolution & Refresh Rate","\uE7F4")})
            {
                int index=form.Cards.Count;var body=new StackPanel{Spacing=10};var summary=new TextBlock{FontSize=12,Opacity=.8,TextWrapping=TextWrapping.Wrap,MaxLines=2};var text=new StackPanel{Spacing=4};text.Children.Add(new TextBlock{Text=OnexStrings.Get(item.Item2),FontSize=18,FontWeight=Windows.UI.Text.FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});text.Children.Add(summary);
                var content=new Grid{ColumnSpacing=12};content.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(24)});content.ColumnDefinitions.Add(new ColumnDefinition());content.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(18)});content.Children.Add(new FontIcon{Glyph=item.Item3,FontSize=23,VerticalAlignment=VerticalAlignment.Center});Grid.SetColumn(text,1);content.Children.Add(text);var arrow=new FontIcon{Glyph="\uE70D",FontSize=13,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(arrow,2);content.Children.Add(arrow);
                var header=new Button{CornerRadius=new CornerRadius(12),Name="Onex"+source+"Card_"+item.Item1,Content=content,Style=(Style)Resources["StandaloneButtonStyle"],HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch,MinHeight=104,Padding=new Thickness(14,12,14,12),Margin=new Thickness(0,0,0,10)};
                var close=new Button{Name="Onex"+source+"Close_"+item.Item1,Content="×",Width=34,Height=32,Padding=new Thickness(0),Style=OnexSettingsButtonStyle(false),HorizontalAlignment=HorizontalAlignment.Right};
                var detailBody=new StackPanel{Spacing=10};detailBody.Children.Add(close);detailBody.Children.Add(body);var detail=OnexSettingsCard(detailBody);detail.Visibility=Visibility.Collapsed;detail.Margin=new Thickness(0,0,0,12);
                var card=new OnexSourceCard{Id=item.Item1,Header=header,Close=close,Body=body,Detail=detail,Summary=summary};form.Cards.Add(card);
                Grid.SetRow(header,index/2*2);Grid.SetColumn(header,index%2);if(index==6)Grid.SetColumnSpan(header,2);form.Grid.Children.Add(header);Grid.SetRow(detail,index/2*2+1);Grid.SetColumnSpan(detail,2);form.Grid.Children.Add(detail);
                void Toggle(bool opening){if(form.Open!=null){form.Open.Detail.Visibility=Visibility.Collapsed;form.Open.Header.BorderBrush=(Brush)Resources["CardBorderBrush"];}form.Open=opening?card:null;if(opening){card.Detail.Visibility=Visibility.Visible;card.Header.BorderBrush=OnexPerformanceAccentBrush(card.Id);if(OnexBrandTheme)PaintOnexPageContent(card.Detail,OnexPerformanceAccent(card.Id));}WireOnexSourceFocus(form);ApplyOnexTabPalette();}
                header.Click+=(s,e)=>Toggle(form.Open!=card);close.Click+=(s,e)=>{Toggle(false);_=Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low,()=>{if(form.Open==null&&form.Root.Visibility==Visibility.Visible)header.Focus(FocusState.Keyboard);});};
            }
            BuildOnexSourceControls(form);
            form.Timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(200)};form.Timer.Tick+=async(s,e)=>await FlushOnexSourceAsync(form);
            form.Root.Unloaded+=(s,e)=>{form.Timer.Stop();form.Pending.Clear();foreach(var slider in form.Sliders)OnexInputGuard.Cancel(slider);};
            return form;
        }
        private void QueueOnexSource(OnexSourceForm form,string key,IJsonValue value)
        {
            if(form.Updating||!App.IsConnected||form.ProfileId==null||form.ProfileId!=OnexFrontend.ProfileId||form.Root.Visibility!=Visibility.Visible)return;
            form.Pending[key]=value;form.Status.Text=global::XboxGamingBar.OnexStrings.Get("Ожидает сохранения…");form.Timer.Stop();form.Timer.Start();
        }
        private async Task FlushOnexSourceAsync(OnexSourceForm form)
        {
            form.Timer.Stop();if(form.Writing||form.Pending.Count==0)return;
            if(form.ProfileId!=OnexFrontend.ProfileId){form.Pending.Clear();return;}
            var patch=form.Pending.ToArray();form.Pending.Clear();string id=form.ProfileId;form.Writing=true;foreach(var item in patch)form.InFlight.Add(item.Key);
            try
            {
                foreach(var item in patch)
                {
                    if(id!=OnexFrontend.ProfileId)throw new InvalidOperationException(global::XboxGamingBar.OnexStrings.Get("Профиль сменился; изменение отменено."));
                    var payload=new JsonObject{{"operation",JsonValue.CreateStringValue("source")},{"id",JsonValue.CreateStringValue(id)},{"source",JsonValue.CreateStringValue(form.Source)},{"key",JsonValue.CreateStringValue(item.Key)},{"value",item.Value}};
                    var reply=await SendOnexHomeRequestAsync(new ValueSet{{"OnexProfilesSet",payload.Stringify()}});ApplyOnexProfilesState((string)reply["OnexProfilesState"]);
                }
                form.Status.Text=form.Live?global::XboxGamingBar.OnexStrings.Get("Сохранено и применено."):global::XboxGamingBar.OnexStrings.Get("Сохранено. Применится при смене источника питания.");
            }
            catch(Exception error){form.Status.Text=error.Message;try{await ReadOnexPerformanceProfilesAsync();}catch{}}
            finally{form.Writing=false;form.InFlight.Clear();RefreshOnexSourceEditors();if(form.Pending.Count>0)form.Timer.Start();}
        }
        private void RefreshOnexSourceEditors()
        {
            if(_onexDcEditor==null||OnexActiveProfile==null)return;bool split=OnexActiveProfile.GetNamedBoolean("split",false);
            if(split&&_onexPerfOpen!=null)
            {
                _onexPerformanceCpuForm?.Cancel?.Invoke();_onexPerformanceCpuForm=null;_cpuCardExpanded=false;CpuSectionContent.Children.Clear();CpuCardExpandButton.IsChecked=false;CpuCardExpandIcon.Glyph="\uE70D";
                _onexPerfOpen=null;foreach(var card in _onexPerfSections){card.Detail.Visibility=Visibility.Collapsed;card.Header.BorderBrush=(Brush)Resources["CardBorderBrush"];}
            }
            bool paint=split&&PerformanceScrollViewer.Visibility==Visibility.Visible;
            OnexPerfCardGrid.Visibility=split?Visibility.Collapsed:Visibility.Visible;
            foreach(var form in new[]{_onexDcEditor,_onexAcEditor})
            {
                string id=OnexActiveProfile.GetNamedString("id");if(form.ProfileId!=id){form.Timer.Stop();form.Pending.Clear();foreach(var slider in form.Sliders)OnexInputGuard.Cancel(slider);form.ProfileId=id;}
                form.Live=(_onexProfilesState.GetNamedBoolean("onAc",false)?"ac":"dc")==form.Source;form.Values=OnexActiveProfile.GetNamedObject(form.Source=="ac"?"effectiveAc":"effectiveDc",OnexActiveProfile.GetNamedObject(form.Source));form.Root.Visibility=split?Visibility.Visible:Visibility.Collapsed;
                if(!paint)continue; // Model/profile cancellation above stays current; no hidden editors rebuilt.
                form.Context.Text=form.Live?global::XboxGamingBar.OnexStrings.Get("Сейчас используется этот источник питания. Изменения сохраняются и применяются."):global::XboxGamingBar.OnexStrings.Get("Настройки для другого источника питания: сейчас только сохраняются.");
                form.Updating=true;try{foreach(var update in form.Updates)update(form.Values);RefreshOnexSourceSummaries(form);}finally{form.Updating=false;}WireOnexSourceFocus(form);
            }
            if(PerformanceScrollViewer.Visibility==Visibility.Visible)ApplyOnexPageSpacing();
        }
        private List<Control[]> OnexSourceRows(OnexSourceForm form)
        {
            var rows=new List<Control[]>();for(int row=0;row<4;row++){rows.Add(form.Cards.Skip(row*2).Take(2).Select(card=>(Control)card.Header).ToArray());if(form.Open!=null&&form.Cards.IndexOf(form.Open)/2==row){rows.AddRange(form.Open.Rows);rows.Add(new Control[]{form.Open.Close});}}
            return rows.Select(row=>row.Where(control=>control!=null&&control.IsEnabled&&control.Visibility==Visibility.Visible).ToArray()).Where(row=>row.Length>0).ToList();
        }
        private void WireOnexSourceFocus(OnexSourceForm form)
        {
            var rows=OnexSourceRows(form);for(int r=0;r<rows.Count;r++)for(int c=0;c<rows[r].Length;c++)
            {var control=rows[r][c];control.XYFocusLeft=rows[r][Math.Max(0,c-1)];control.XYFocusRight=rows[r][Math.Min(rows[r].Length-1,c+1)];control.XYFocusUp=r>0?rows[r-1][Math.Min(c,rows[r-1].Length-1)]:form.Source=="ac"?(Control)_onexPerformanceProfilePicker.Split:_onexPerformanceProfilePicker.Choose;control.XYFocusDown=r+1<rows.Count?rows[r+1][Math.Min(c,rows[r+1].Length-1)]:form.Source=="dc"?(Control)PerfSavedProfilesExpandButton:control;}
        }
        private bool HandleOnexSourceKey(KeyRoutedEventArgs args)
        {
            if(args.Handled||_releasePowerNav?.IsChecked!=true||_onexDcEditor==null||_onexDcEditor.Root.Visibility!=Visibility.Visible||_onexDetailsDialog!=null||_onexOpenMenus.Any(menu=>menu.IsOpen))return false;
            var focused=FocusManager.GetFocusedElement();if(focused is ComboBox choice&&choice.IsDropDownOpen)return false;
            if(ReferenceEquals(focused,_onexPerformanceProfilePicker.Split)&&IsOnexProfileDown(args.Key)){args.Handled=true;_onexAcEditor.Cards[0].Header.Focus(FocusState.Keyboard);return true;}
            if(ReferenceEquals(focused,PerfSavedProfilesExpandButton)&&IsOnexProfileVertical(args.Key)&&!IsOnexProfileDown(args.Key))
            {var last=OnexSourceRows(_onexDcEditor).Last().First();args.Handled=true;last.Focus(FocusState.Keyboard);last.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});return true;}
            foreach(var form in new[]{_onexDcEditor,_onexAcEditor})
            {
                var rows=OnexSourceRows(form);for(int r=0;r<rows.Count;r++)for(int c=0;c<rows[r].Length;c++)if(ReferenceEquals(focused,rows[r][c]))
                {
                    if(form.Open!=null&&IsOnexPerformanceBackKey(args.Key,ReferenceEquals(focused,form.Open.Header))){args.Handled=true;var card=form.Open;card.Detail.Visibility=Visibility.Collapsed;card.Header.BorderBrush=(Brush)Resources["CardBorderBrush"];form.Open=null;ApplyOnexTabPalette();card.Header.Focus(FocusState.Keyboard);return true;}
                    if(!IsOnexProfileVertical(args.Key))return false;args.Handled=true;int next=r+(IsOnexProfileDown(args.Key)?1:-1);Control target=next<0?form.Source=="ac"?(Control)_onexPerformanceProfilePicker.Split:_onexPerformanceProfilePicker.Choose:next>=rows.Count?form.Source=="dc"?(Control)PerfSavedProfilesExpandButton:rows[r][c]:rows[next][Math.Min(c,rows[next].Length-1)];_=Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,()=>{target.Focus(FocusState.Keyboard);target.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});});return true;
                }
            }
            return false;
        }
    }
}
