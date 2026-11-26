// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Controls.Primitives;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private static string OnexDisplayText(JsonObject value,string name,string fallback="")=>value!=null&&value.TryGetValue(name,out var field)&&field.ValueType==JsonValueType.String?field.GetString():fallback;
        private static JsonObject OnexDisplayObject(JsonObject value,string name)=>value!=null&&value.TryGetValue(name,out var field)&&field.ValueType==JsonValueType.Object?field.GetObject():null;
        private JsonObject _onexDisplayState;
        private bool _onexDisplayWired,_onexDisplaySyncing,_onexDisplayBusy;
        private readonly Dictionary<Slider,string> _onexDisplaySliders=new Dictionary<Slider,string>();
        private readonly Dictionary<ComboBox,string> _onexDisplayChoices=new Dictionary<ComboBox,string>();
        private readonly Dictionary<string,int> _onexDisplayPending=new Dictionary<string,int>();
        private readonly HashSet<Slider> _onexDisplayDragging=new HashSet<Slider>();
        private DispatcherTimer _onexDisplayDebounce;
        private TextBlock _onexDisplayStatus;
        private readonly HashSet<Slider> _onexDisplayThumbWired=new HashSet<Slider>();
        private static FrameworkElement OnexDisplayThumbPart(Slider slider,string name)
        {
            slider.ApplyTemplate();if(VisualTreeHelper.GetChildrenCount(slider)==0)return null;
            var root=VisualTreeHelper.GetChild(slider,0) as FrameworkElement;
            var thumb=root?.FindName("HorizontalThumb") as Thumb;if(thumb==null)return null;
            thumb.ApplyTemplate();if(VisualTreeHelper.GetChildrenCount(thumb)==0)return null;
            return (VisualTreeHelper.GetChild(thumb,0) as FrameworkElement)?.FindName(name) as FrameworkElement;
        }
        private static void AdjustOnexDisplayThumb(Slider slider)
        {
            var ink=OnexDisplayThumbPart(slider,"OnexDisplayThumbInk");if(ink==null)return;
            double scale=Windows.Graphics.Display.DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel;
            ink.RenderTransform=new TranslateTransform{Y=-1/scale};
        }
        private void WireOnexDisplayThumb(Slider slider)
        {
            if(!_onexDisplayThumbWired.Add(slider))return;
            slider.Loaded+=(s,e)=>AdjustOnexDisplayThumb(slider);slider.SizeChanged+=(s,e)=>AdjustOnexDisplayThumb(slider);
            if(slider.IsLoaded)AdjustOnexDisplayThumb(slider);
        }
        private bool OnexDisplayReady=>App.IsConnected&&_onexDisplayState?.GetNamedBoolean("available",false)==true;
        private bool OnexDisplayModesReady=>OnexDisplayReady&&_onexDisplayState?.GetNamedObject("modes",null)?.GetNamedBoolean("available",false)==true;
        private void EnsureOnexDisplayWiring()
        {
            if(_onexDisplayWired)return;_onexDisplayWired=true;
            _onexDisplaySliders[DisplaySaturationSlider]="saturation";_onexDisplaySliders[DisplayHueSlider]="hue";_onexDisplaySliders[DisplayContrastSlider]="contrast";_onexDisplaySliders[DisplayBrightnessSlider]="brightness";_onexDisplaySliders[DisplayGammaSlider]="gamma";_onexDisplaySliders[DisplaySharpnessSlider]="sharpness";
            foreach(var pair in _onexDisplaySliders)
            {
                var slider=pair.Key;var key=pair.Value;OnexInputGuard.Wire(slider);WireOnexDisplayThumb(slider);slider.IsFocusEngagementEnabled=false;slider.Foreground=OnexDialogAccent;
                OnexInputGuard.SetCommit(slider,()=>QueueOnexDisplaySlider(key,(int)slider.Value));
                if(key!="hue"&&key!="gamma"&&key!="sharpness")OnexInputGuard.ConfigurePercentage(slider);
                if(key=="sharpness")slider.SmallChange=slider.LargeChange=1;
                slider.ValueChanged+=(s,e)=>{if(!_onexDisplaySyncing&&OnexInputGuard.IsActive(slider))QueueOnexDisplaySlider(key,(int)slider.Value);};
                slider.AddHandler(UIElement.PointerPressedEvent,new PointerEventHandler((s,e)=>{if(!_onexDisplaySyncing&&slider.IsEnabled){_onexDisplayDragging.Add(slider);QueueOnexDisplaySlider(key,(int)slider.Value);}}),true);
                PointerEventHandler released=(s,e)=>{_onexDisplayDragging.Remove(slider);if(_onexDisplayPending.Count>0){_onexDisplayDebounce.Stop();_onexDisplayDebounce.Start();}};
                slider.AddHandler(UIElement.PointerReleasedEvent,released,true);slider.AddHandler(UIElement.PointerCanceledEvent,released,true);slider.AddHandler(UIElement.PointerCaptureLostEvent,released,true);
            }
            _onexDisplayChoices[IntelFrameGenerationComboBox]="frameGeneration";_onexDisplayChoices[IntelVrrComboBox]="vrr";_onexDisplayChoices[IntelVrrModeComboBox]="vrrMode";_onexDisplayChoices[IntelLowLatencyComboBox]="lowLatency";_onexDisplayChoices[IntelFrameSyncComboBox]="frameSync";_onexDisplayChoices[IntelScalingModeComboBox]="scalingMode";_onexDisplayChoices[IntelScalingMethodComboBox]="scalingMethod";
            foreach(var pair in _onexDisplayChoices)
            {
                var choice=pair.Key;var key=pair.Value;OnexInputGuard.Wire(choice);ApplyOnexTileWindowControlAccent(choice);
                choice.SelectionChanged+=async(s,e)=>
                {
                    if(_onexDisplaySyncing||_onexDisplayBusy||!OnexInputGuard.IsActive(choice))return;
                    int value=key=="scalingMode"?SelectedScalingMode():choice.SelectedIndex;
                    if(value>=0)await SetOnexDisplayAsync(key,JsonValue.CreateNumberValue(value));
                };
            }
            OnexInputGuard.Wire(ResolutionComboBox);OnexInputGuard.Wire(RefreshRatesComboBox);
            ResolutionComboBox.SelectionChanged+=async(s,e)=>{if(!_onexDisplaySyncing&&!_onexDisplayBusy&&OnexInputGuard.IsActive(ResolutionComboBox)&&ResolutionComboBox.SelectedItem is string value)await SetOnexDisplayAsync("resolution",JsonValue.CreateStringValue(value));};
            RefreshRatesComboBox.SelectionChanged+=async(s,e)=>{if(!_onexDisplaySyncing&&!_onexDisplayBusy&&OnexInputGuard.IsActive(RefreshRatesComboBox)&&RefreshRatesComboBox.SelectedItem is string value&&int.TryParse(value.Replace(" Hz",""),out int hz))await SetOnexDisplayAsync("refreshRate",JsonValue.CreateNumberValue(hz));};
            _onexDisplayDebounce=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(180)};_onexDisplayDebounce.Tick+=async(s,e)=>await FlushOnexDisplaySlidersAsync();
            _onexDisplayStatus=new TextBlock{Name="OnexDisplayStatus",Visibility=Visibility.Collapsed,FontSize=12,TextWrapping=TextWrapping.Wrap,Foreground=OnexDialogAccent};DisplayMainStack.Children.Insert(1,_onexDisplayStatus);
            EnsureOnexHdrEditor();
            EnsureOnexScreenProfilePicker();EnsureOnexIntelSummary();
            UpdateOnexDisplayAvailability();
        }
        private void QueueOnexDisplaySlider(string key,int value)
        {
            if(_onexDisplayProfileTransition||(!OnexDisplayReady&&key!="sdrBrightness")||_onexDisplayBusy)return;
            string screen=OnexScreenProfileId;
            if(_onexDisplayPending.Count>0&&_onexDisplayPendingScreenProfile!=screen)_onexDisplayPending.Clear();
            _onexDisplayPendingScreenProfile=screen;_onexDisplayPending[key]=value;_onexDisplayDebounce.Stop();_onexDisplayDebounce.Start();
        }
        private async Task FlushOnexDisplaySlidersAsync()
        {
            _onexDisplayDebounce.Stop();
            if(_onexDisplayPending.Count==0||_onexDisplayProfileTransition)return;
            if(_onexDisplayBusy||_onexDisplayDragging.Count>0){_onexDisplayDebounce.Start();return;}
            var pending=_onexDisplayPending.ToArray();
            string screen=_onexDisplayPendingScreenProfile;
            int revision=_onexDisplayProfileRevision;
            _onexDisplayPending.Clear();
            foreach(var value in pending)
            {
                if(screen!=OnexScreenProfileId||revision!=_onexDisplayProfileRevision)break;
                await SetOnexDisplayAsync(value.Key,JsonValue.CreateNumberValue(value.Value),screenProfile:screen);
            }
        }
        private async Task<bool> SetOnexDisplayAsync(string key,IJsonValue value,string profile=null,string screenProfile=null)
        {
            bool hdrKey=key=="hdrEnabled"||key=="autoHdr"||key=="hdrCalibration"||key=="sdrBrightness"||key=="hdrReset";
            bool image=hdrKey||key=="saturation"||key=="hue"||key=="contrast"||key=="brightness"||key=="gamma"||key=="sharpness"||key=="reset";
            if(image&&_onexDisplayProfileTransition)return false;
            if(_onexDisplayBusy||!(hdrKey?OnexHdrReady:OnexDisplayReady))return false;_onexDisplayBusy=true;UpdateOnexDisplayAvailability();
            try
            {
                var change=new JsonObject{{"key",JsonValue.CreateStringValue(key)},{"value",value}};
                if(image)change["screenProfileId"]=JsonValue.CreateStringValue(screenProfile??OnexScreenProfileId);
                else change["profileId"]=JsonValue.CreateStringValue(profile??_onexProfilesState?.GetNamedString("activeId","global")??"global");
                var response=await SendOnexHomeRequestAsync(new ValueSet{{"OnexDisplaySet",change.Stringify()}});ApplyOnexDisplayReadReply(response);_onexDisplayStatus.Visibility=Visibility.Collapsed;
                var profiles=await App.SendMessageAsync(new ValueSet{{"OnexProfilesGet",true}});if(profiles.TryGetValue("OnexProfilesState",out object state))ApplyOnexProfilesState((string)state);
                return true;
            }
            catch(Exception error){_onexDisplayStatus.Text=global::XboxGamingBar.OnexStrings.Format("Could not apply: {0}",error.Message);_onexDisplayStatus.Visibility=Visibility.Visible;await RefreshOnexDisplayAsync(false);return false;}
            finally{_onexDisplayBusy=false;UpdateOnexDisplayAvailability();}
        }
        private async Task RefreshOnexDisplayAsync(bool showReport)
        {
            if(!App.IsConnected)return;
            try
            {
                var response=await App.SendMessageAsync(new ValueSet{{"OnexDisplayGet",true}});ApplyOnexDisplayReadReply(response);
                if(showReport)
                {
                    var lines=new List<string>();var mode=OnexDisplayObject(_onexDisplayState.GetNamedObject("modes"),"current");if(mode!=null)lines.Add("Display: "+mode.GetNamedNumber("width")+" x "+mode.GetNamedNumber("height")+" @ "+mode.GetNamedNumber("hz")+" Hz");
                    foreach(var setting in _onexDisplayState.GetNamedObject("settings"))
                    {var state=setting.Value.GetObject();var actual=state.GetNamedValue("value",JsonValue.CreateNullValue());lines.Add(setting.Key+": "+(actual.ValueType==JsonValueType.Number?actual.GetNumber().ToString():"Unavailable")+(state.GetNamedBoolean("reported",true)?"":" (starting value; no override saved)")+(!string.IsNullOrEmpty(OnexDisplayText(state,"error"))?" · "+OnexDisplayText(state,"error"):""));}
                    if(!_onexDisplayState.GetNamedBoolean("colorReadback",false))lines.Add("Color sliders show ONEXTweaks values. The Intel driver does not expose complete color readback.");
                    IntelDriverStateText.Text=string.Join("\n",lines);IntelDriverStateText.Visibility=Visibility.Visible;
                }
            }
            catch(Exception error){ShowOnexDisplayReadError(error);}
        }

        private async Task<bool> RefreshOnexDisplayForConnectionAsync(IPC.NamedPipeClient owner)
        {
            try
            {
                return await TryReadOnexConnectionStateAsync(owner, "OnexDisplayGet", ApplyOnexDisplayReadReply);
            }
            catch (Exception error)
            {
                return TryRunForOnexConnection(owner, () => ShowOnexDisplayReadError(error));
            }
        }

        private void ApplyOnexDisplayReadReply(ValueSet response)
        {
            bool hasProfile=response.TryGetValue("OnexDisplayProfilesState",out var profileState)&&profileState is string profileJson&&ApplyOnexScreenProfilesState(profileJson);
            if(!response.TryGetValue("OnexDisplayState",out var displayState)||!(displayState is string displayJson))return;
            if(_onexDisplayProfileTransition&&hasProfile&&JsonObject.TryParse(displayJson,out _))_onexDisplayProfileTransition=false;
            ApplyOnexDisplayState(displayJson);
        }

        private void ShowOnexDisplayReadError(Exception error)
        {
            if (_onexDisplayStatus == null) return;
            _onexDisplayStatus.Text = error.Message;
            _onexDisplayStatus.Visibility = Visibility.Visible;
        }
        private void ApplyOnexDisplayState(string json)
        {
            if(!JsonObject.TryParse(json,out var state))return;EnsureOnexDisplayWiring();_onexDisplayState=state;ApplyOnexRotationLockState(state.GetNamedObject("rotationLock",null));_onexDisplaySyncing=true;
            try
            {
                var settings=state.GetNamedObject("settings");
                foreach(var pair in _onexDisplaySliders)
                {
                    if(!settings.TryGetValue(pair.Value,out var field))continue;var item=field.GetObject();
                    if(pair.Value=="sharpness"&&item.GetNamedBoolean("available",false))
                    {pair.Key.Maximum=item.GetNamedNumber("maximum",5);pair.Key.StepFrequency=pair.Key.SmallChange=pair.Key.LargeChange=item.GetNamedNumber("step",1);}
                    if(!OnexInputGuard.IsEditing(pair.Key)&&!_onexDisplayPending.ContainsKey(pair.Value)){var value=item.GetNamedValue("value",JsonValue.CreateNullValue());if(value.ValueType==JsonValueType.Number)pair.Key.Value=value.GetNumber();}
                }
                foreach(var pair in _onexDisplayChoices)
                {
                    if(pair.Key.IsDropDownOpen&&!_onexDisplayBusy)continue;
                    if(!settings.TryGetValue(pair.Value,out var field))continue;var value=field.GetObject().GetNamedValue("value",JsonValue.CreateNullValue());if(value.ValueType!=JsonValueType.Number)continue;int number=(int)value.GetNumber();
                    if(pair.Value=="scalingMode")pair.Key.SelectedItem=pair.Key.Items.OfType<ComboBoxItem>().FirstOrDefault(item=>item.Tag?.ToString()==number.ToString());
                    else if(pair.Value=="scalingMethod")
                    {
                        string[] entries=ScalingMethodNames(SelectedScalingMode());
                        if(!pair.Key.Items.OfType<ComboBoxItem>().Select(item=>item.Content?.ToString()).SequenceEqual(entries.Select(OnexStrings.Get)))FillScalingMethods(SelectedScalingMode(),number);
                        else pair.Key.SelectedIndex=number>=0&&number<entries.Length?number:0;
                    }
                    else pair.Key.SelectedIndex=number>=0&&number<pair.Key.Items.Count?number:-1;
                }
                var modes=state.GetNamedObject("modes");var current=OnexDisplayObject(modes,"current");var available=modes.GetNamedArray("modes");
                string[] sizes=available.Select(mode=>mode.GetObject()).Select(mode=>mode.GetNamedNumber("width")+"x"+mode.GetNamedNumber("height")).Distinct().ToArray();SetOnexDisplayItems(ResolutionComboBox,sizes);
                if(current!=null)
                {
                    ResolutionComboBox.SelectedItem=current.GetNamedNumber("width")+"x"+current.GetNamedNumber("height");
                    string[] rates=available.Select(mode=>mode.GetObject()).Where(mode=>mode.GetNamedNumber("width")==current.GetNamedNumber("width")&&mode.GetNamedNumber("height")==current.GetNamedNumber("height")).Select(mode=>mode.GetNamedNumber("hz")+" Hz").Distinct().ToArray();SetOnexDisplayItems(RefreshRatesComboBox,rates);RefreshRatesComboBox.SelectedItem=current.GetNamedNumber("hz")+" Hz";
                }
                UpdateDisplayProfileBadge();
                ApplyOnexHdrState();
            }
            finally{_onexDisplaySyncing=false;UpdateOnexDisplayAvailability();}
        }
        private static void SetOnexDisplayItems(ComboBox choice,string[] values)
        {
            if(choice.Items.Cast<object>().Select(value=>value?.ToString()).SequenceEqual(values.Select(OnexStrings.Get)))return;
            choice.ItemsSource=null;choice.Items.Clear();foreach(string value in values)choice.Items.Add(OnexStrings.Get(value));
        }
        private static void ApplyOnexDisplayChoiceSupport(ComboBox control,JsonObject state)
        {
            var allowed=state!=null&&state.TryGetValue("choices",out var raw)&&raw.ValueType==JsonValueType.Array?raw.GetArray().Select(item=>(int)item.GetNumber()).ToArray():null;
            for(int i=0;i<control.Items.Count;i++)if(control.Items[i] is ComboBoxItem item)
            {item.IsEnabled=state?.GetNamedBoolean("available",false)==true&&(allowed==null||allowed.Contains(i));ToolTipService.SetToolTip(item,item.IsEnabled?null:global::XboxGamingBar.OnexStrings.Get("Not supported by the current Intel graphics driver."));}
        }
        private void UpdateOnexDisplayAvailability()
        {
            RefreshOnexRateWindow();RefreshOnexResolutionWindow();ApplyOnexTileAvailability();
            if(!_onexDisplayWired)return;var settings=_onexDisplayState?.GetNamedObject("settings",null);
            void Enable(Control control,string key)
            {var state=settings?.GetNamedObject(key,null);if(key=="frameSync"&&control is ComboBox choice)ApplyOnexDisplayChoiceSupport(choice,state);control.IsEnabled=OnexDisplayReady&&(!_onexDisplayProfileTransition||!(control is Slider))&&(!_onexDisplayBusy||control.FocusState!=FocusState.Unfocused)&&state?.GetNamedBoolean("available",false)==true;ToolTipService.SetToolTip(control,control.IsEnabled?null:OnexDisplayText(state,"error",OnexStrings.Get("Display setting unavailable")));}
            foreach(var pair in _onexDisplaySliders)Enable(pair.Key,pair.Value);foreach(var pair in _onexDisplayChoices)Enable(pair.Key,pair.Value);
            ResolutionComboBox.IsEnabled=OnexDisplayModesReady&&(!_onexDisplayBusy||ResolutionComboBox.FocusState!=FocusState.Unfocused);
            RefreshRatesComboBox.IsEnabled=OnexDisplayModesReady&&(!_onexDisplayBusy||RefreshRatesComboBox.FocusState!=FocusState.Unfocused);
            DisplayResetButton.IsEnabled=OnexDisplayReady&&!_onexDisplayProfileTransition&&(!_onexDisplayBusy||DisplayResetButton.FocusState!=FocusState.Unfocused)&&settings?.GetNamedObject("saturation",null)?.GetNamedBoolean("available",false)==true&&settings?.GetNamedObject("gamma",null)?.GetNamedBoolean("available",false)==true;
            IntelFetchStateButton.IsEnabled=App.IsConnected&&(!_onexDisplayBusy||IntelFetchStateButton.FocusState!=FocusState.Unfocused);
            var sharpness=settings?.GetNamedObject("sharpness",null);
            if(sharpness?.GetNamedBoolean("available",false)==false)DisplaySharpnessValueText.Text=_onexDisplayState?.GetNamedObject("hdr",null)?.GetNamedBoolean("enabled",false)==true?global::XboxGamingBar.OnexStrings.Get("SDR only"):global::XboxGamingBar.OnexStrings.Get("Unavailable");
            OnexSharpnessHint.Text=sharpness?.GetNamedBoolean("available",false)==true?global::XboxGamingBar.OnexStrings.Format("0 — Off. Уровни 1–{0} усиливают контуры: например, 1 — лёгкая резкость, максимальный — сильная.",DisplaySharpnessSlider.Maximum):OnexDisplayText(sharpness,"error",global::XboxGamingBar.OnexStrings.Get("Состояние резкости пока недоступно."));
            IntelDirectFeaturesCard.Visibility=IntelFrameGenerationPanel.Visibility=IntelVrrPanel.Visibility=IntelVrrModePanel.Visibility=IntelScalingCard.Visibility=Visibility.Visible;
            bool native=_onexDisplayState?.GetNamedBoolean("atNativeResolution",false)==true;
            IntelScalingModeCaption.Text=native?global::XboxGamingBar.OnexStrings.Get("Сначала выбери разрешение ниже 1920×1200 в Resolution & Refresh Rate."):global::XboxGamingBar.OnexStrings.Get("GPU — обычное; Retro — чёткие пиксели.");IntelScalingMethodCaption.Text=native?global::XboxGamingBar.OnexStrings.Get("При родном разрешении масштабирование не требуется."):global::XboxGamingBar.OnexStrings.Get("Как изображение заполняет экран.");IntelVrrCaption.Text=native?global::XboxGamingBar.OnexStrings.Get("Intel Arc Sync"):global::XboxGamingBar.OnexStrings.Get("Only at native res.");
            int scalingMethod=IntelScalingMethodComboBox.SelectedIndex;
            OnexScalingHelp.Text=native?global::XboxGamingBar.OnexStrings.Get("Например, выбери 1280×800 в блоке Resolution & Refresh Rate — тогда станут доступны режим и метод масштабирования."):SelectedScalingMode()==RetroScalingMode
                ?scalingMethod==0?global::XboxGamingBar.OnexStrings.Get("Integer: увеличение только в целое число раз, без размытия пикселей. Если целый масштаб не заполняет экран, остаются поля."):global::XboxGamingBar.OnexStrings.Get("Nearest Neighbour: заполняет экран без сглаживания пикселей; при дробном масштабе они могут иметь разную ширину.")
                :scalingMethod==0?global::XboxGamingBar.OnexStrings.Get("Centered: исходное изображение по центру без увеличения, вокруг остаются поля."):scalingMethod==1?global::XboxGamingBar.OnexStrings.Get("Stretch: растягивает изображение на весь экран. Если пропорции отличаются, круги могут стать овалами."):global::XboxGamingBar.OnexStrings.Get("Preserve Aspect Ratio: увеличивает изображение с сохранением пропорций. Если они отличаются от экрана, остаются поля.");
            ProfileSaveResolutionCheckBox.IsEnabled=ProfileSaveRefreshRateCheckBox.IsEnabled=OnexDisplayModesReady&&!_onexProfilesBusy;
            UpdateOnexHdrAvailability();
            WireOnexDisplayNavigation();RefreshOnexIntelSummary();
        }
        private Control[][] OnexDisplayRows()=>OnexProfileRows(_onexScreenProfilePicker).Concat(new Control[][]{new Control[]{DisplayRefPrevButton,DisplayRefNextButton},new Control[]{DisplaySaturationSlider,DisplayBrightnessSlider},new Control[]{DisplayHueSlider,DisplayGammaSlider},new Control[]{DisplayContrastSlider,DisplaySharpnessSlider},new Control[]{DisplayResetButton},new Control[]{_onexHdrEnabled,_onexAutoHdr},new Control[]{_onexHdrCalibration},new Control[]{_onexSdrBrightness},new Control[]{_onexHdrCalibrate,_onexHdrReset}}).Concat(_onexScreenSavedProfiles?.FocusRows()??Enumerable.Empty<Control[]>()).ToArray();
        private void WireOnexDisplayNavigation()
        {
            var rows=OnexDisplayRows().Select(row=>row.Where(control=>control!=null&&control.IsEnabled&&control.Visibility==Visibility.Visible).ToArray()).Where(row=>row.Length>0).ToArray();
            for(int row=0;row<rows.Length;row++)for(int column=0;column<rows[row].Length;column++)
            {var control=rows[row][column];control.XYFocusLeft=rows[row][Math.Max(0,column-1)];control.XYFocusRight=rows[row][Math.Min(rows[row].Length-1,column+1)];control.XYFocusUp=row>0?rows[row-1][Math.Min(column,rows[row-1].Length-1)]:control;control.XYFocusDown=row+1<rows.Length?rows[row+1][Math.Min(column,rows[row+1].Length-1)]:control;}
        }
        private bool HandleOnexDisplayVertical(KeyRoutedEventArgs args)
        {
            if(args.Handled||!OnexFrontend.UsesOwnBackend||DisplayNavItem?.IsChecked!=true||!IsOnexProfileVertical(args.Key)||_onexDetailsDialog!=null||_onexOpenMenus.Any(menu=>menu.IsOpen))return false;
            var current=OnexDisplayRows().SelectMany(row=>row).FirstOrDefault(control=>control==FocusManager.GetFocusedElement());
            if(current==null||current is Slider slider&&slider.IsFocusEngaged||current is ComboBox choice&&choice.IsDropDownOpen)return false;
            var next=(IsOnexProfileDown(args.Key)?current.XYFocusDown:current.XYFocusUp) as Control;if(next==null||!next.IsEnabled)return false;
            args.Handled=true;_=Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,()=>{next.Focus(FocusState.Keyboard);next.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});});return true;
        }
    }
}
