// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private ComboBox _onexResolutionChoice,_onexOrientationChoice,_onexScaleChoice;
        private TextBlock _onexResolutionCurrent,_onexResolutionStatus;
        private bool _onexResolutionSyncing,_onexResolutionTileBusy;
        private static int OnexSystemNumber(JsonObject value,string key,int fallback=0)=>value!=null&&value.TryGetValue(key,out var field)&&field.ValueType==JsonValueType.Number?(int)field.GetNumber():fallback;
        private JsonObject OnexDisplaySystem=>OnexDisplayObject(_onexDisplayState,"system");
        private string OnexResolutionTileCaption=>_onexResolutionTileBusy||_onexDisplayBusy?"Applying…":OnexDisplayModesReady&&OnexCurrentDisplayMode!=null?OnexCurrentDisplayMode.GetNamedNumber("width",0)+" × "+OnexCurrentDisplayMode.GetNamedNumber("height",0):"Unavailable";
        private JsonObject[] OnexSupportedResolutions()
        {
            var values=OnexDisplayObject(_onexDisplayState,"modes")?.GetNamedArray("modes",null);if(values==null)return new JsonObject[0];
            return values.Select(value=>value.GetObject()).GroupBy(value=>value.GetNamedNumber("width")+"x"+value.GetNamedNumber("height")).Select(group=>group.First()).OrderByDescending(value=>value.GetNamedNumber("width")*value.GetNamedNumber("height")).ThenByDescending(value=>value.GetNamedNumber("width")).ToArray();
        }
        private static string OnexResolutionTag(JsonObject mode)=>mode.GetNamedNumber("width")+"x"+mode.GetNamedNumber("height");
        private async Task CycleOnexResolutionAsync()
        {
            if(_onexResolutionTileBusy||_onexDisplayBusy||!OnexDisplayModesReady)return;_onexResolutionTileBusy=true;ApplyOnexTileAvailability();
            try
            {
                await RefreshOnexDisplayAsync(false);var current=OnexCurrentDisplayMode;
                string next=OnexResolutionPolicy.Next((int)current.GetNamedNumber("width"),(int)current.GetNamedNumber("height"),OnexSupportedResolutions().Select(mode=>OnexResolutionPolicy.Key((int)mode.GetNamedNumber("width"),(int)mode.GetNamedNumber("height"))));
                if(!await SetOnexDisplayAsync("resolution",JsonValue.CreateStringValue(next)))throw new InvalidOperationException(_onexDisplayStatus.Text);
                current=OnexCurrentDisplayMode;if(OnexResolutionPolicy.Key((int)current.GetNamedNumber("width"),(int)current.GetNamedNumber("height"))!=next)throw new InvalidOperationException("Windows did not confirm the selected resolution.");
            }
            catch(Exception error){ShowOnexDesktopStatus(error.Message);}
            finally{_onexResolutionTileBusy=false;ApplyOnexTileAvailability();}
        }
        private void RefreshOnexResolutionWindow()
        {
            if(_onexResolutionCurrent==null)return;_onexResolutionSyncing=true;
            try
            {
                var current=OnexCurrentDisplayMode;var system=OnexDisplaySystem;var scale=OnexDisplayObject(system,"scale");var modes=OnexSupportedResolutions();
                _onexResolutionCurrent.Text=current==null?global::XboxGamingBar.OnexStrings.Get("Display mode unavailable"):global::XboxGamingBar.OnexStrings.Format("Current: {0} × {1} • {2} Hz",current.GetNamedNumber("width"),current.GetNamedNumber("height"),current.GetNamedNumber("hz"));
                var tags=modes.Select(OnexResolutionTag).ToArray();
                if(!_onexResolutionChoice.Items.OfType<ComboBoxItem>().Select(item=>(string)item.Tag).SequenceEqual(tags))
                {_onexResolutionChoice.Items.Clear();foreach(var mode in modes)_onexResolutionChoice.Items.Add(new ComboBoxItem{Content=mode.GetNamedNumber("width")+" × "+mode.GetNamedNumber("height"),Tag=OnexResolutionTag(mode)});}
                if(!_onexResolutionChoice.IsDropDownOpen)_onexResolutionChoice.SelectedItem=_onexResolutionChoice.Items.OfType<ComboBoxItem>().FirstOrDefault(item=>(string)item.Tag==(current==null?null:OnexResolutionTag(current)));
                int orientation=OnexSystemNumber(system,"orientation");bool naturalWide=current!=null&&(current.GetNamedNumber("width")>current.GetNamedNumber("height"))==(orientation%2==0);
                for(int index=0;index<_onexOrientationChoice.Items.Count;index++)((ComboBoxItem)_onexOrientationChoice.Items[index]).Content=(naturalWide==(index%2==0)?global::XboxGamingBar.OnexStrings.Get("Landscape"):global::XboxGamingBar.OnexStrings.Get("Portrait"))+(index>=2?global::XboxGamingBar.OnexStrings.Get(" (flipped)"):"")+" • "+(index*90)+"°";
                if(!_onexOrientationChoice.IsDropDownOpen)_onexOrientationChoice.SelectedIndex=system?.GetNamedBoolean("orientationAvailable",false)==true?orientation:-1;
                var options=scale?.GetNamedArray("choices",null)?.Select(value=>(int)value.GetNumber()).ToArray()??new int[0];int percent=OnexSystemNumber(scale,"current"),recommended=OnexSystemNumber(scale,"recommended");
                if(!_onexScaleChoice.Items.OfType<ComboBoxItem>().Select(item=>(int)item.Tag).SequenceEqual(options)){_onexScaleChoice.Items.Clear();foreach(int value in options)_onexScaleChoice.Items.Add(new ComboBoxItem{Content=value+"%"+(value==recommended?global::XboxGamingBar.OnexStrings.Get(" (Recommended)"):""),Tag=value});}
                foreach(var item in _onexScaleChoice.Items.OfType<ComboBoxItem>())item.Content=item.Tag+"%"+((int)item.Tag==recommended?global::XboxGamingBar.OnexStrings.Get(" (Recommended)"):"");
                if(!_onexScaleChoice.IsDropDownOpen)_onexScaleChoice.SelectedItem=_onexScaleChoice.Items.OfType<ComboBoxItem>().FirstOrDefault(item=>(int)item.Tag==percent);
                _onexResolutionChoice.IsEnabled=OnexDisplayModesReady&&(!_onexDisplayBusy||_onexResolutionChoice.FocusState!=FocusState.Unfocused);
                _onexOrientationChoice.IsEnabled=system?.GetNamedBoolean("orientationAvailable",false)==true&&(!_onexDisplayBusy||_onexOrientationChoice.FocusState!=FocusState.Unfocused);
                _onexScaleChoice.IsEnabled=scale?.GetNamedBoolean("available",false)==true&&(!_onexDisplayBusy||_onexScaleChoice.FocusState!=FocusState.Unfocused);ToolTipService.SetToolTip(_onexScaleChoice,OnexDisplayText(scale,"error"));
            }
            finally{_onexResolutionSyncing=false;}
        }
        private async Task SetOnexResolutionWindowAsync(string key,IJsonValue value)
        {
            if(_onexResolutionSyncing||_onexDisplayBusy)return;if(_onexResolutionStatus!=null)_onexResolutionStatus.Text=global::XboxGamingBar.OnexStrings.Get("Applying…");
            bool applied=await SetOnexDisplayAsync(key,value);RefreshOnexResolutionWindow();if(_onexResolutionStatus!=null)_onexResolutionStatus.Text=applied?global::XboxGamingBar.OnexStrings.Get("Applied • read back from Windows"):_onexDisplayStatus.Text;
        }
        private async Task ShowOnexResolutionSettingsAsync(Button anchor)
        {
            if(_onexDetailsDialog!=null||_onexResolutionTileBusy)return;
            var body=new StackPanel{Spacing=14,Margin=new Thickness(3)};_onexResolutionCurrent=new TextBlock{FontSize=14,TextWrapping=TextWrapping.Wrap};body.Children.Add(_onexResolutionCurrent);
            _onexResolutionChoice=OnexSettingsChoice(global::XboxGamingBar.OnexStrings.Get("All Windows resolutions"));_onexOrientationChoice=OnexSettingsChoice(global::XboxGamingBar.OnexStrings.Get("Display orientation"));_onexScaleChoice=OnexSettingsChoice(global::XboxGamingBar.OnexStrings.Get("Windows UI scale"));
            _onexScaleChoice.Header=OnexHelpHeader(global::XboxGamingBar.OnexStrings.Get("Windows UI scale"),global::XboxGamingBar.OnexStrings.Get("Меняет размер текста, кнопок и окон Windows, а не разрешение игры. Например, при150% элементы крупнее, чем при100%. Это отдельная настройка от Scaling Mode/Method Intel. Некоторые старые приложения обновляют размер после повторного запуска."));
            foreach(var choice in new[]{_onexResolutionChoice,_onexOrientationChoice,_onexScaleChoice}){OnexInputGuard.Wire(choice);ApplyOnexTileWindowControlAccent(choice);body.Children.Add(choice);}
            for(int index=0;index<4;index++)_onexOrientationChoice.Items.Add(new ComboBoxItem{Content=index*90+"°",Tag=index});
            _onexResolutionChoice.SelectionChanged+=async(s,e)=>{if(!_onexResolutionSyncing&&OnexInputGuard.IsActive(_onexResolutionChoice)&&_onexResolutionChoice.SelectedItem is ComboBoxItem item)await SetOnexResolutionWindowAsync("resolution",JsonValue.CreateStringValue((string)item.Tag));};
            _onexOrientationChoice.SelectionChanged+=async(s,e)=>{if(!_onexResolutionSyncing&&OnexInputGuard.IsActive(_onexOrientationChoice)&&_onexOrientationChoice.SelectedItem is ComboBoxItem item)await SetOnexResolutionWindowAsync("displayOrientation",JsonValue.CreateNumberValue((int)item.Tag));};
            _onexScaleChoice.SelectionChanged+=async(s,e)=>{if(!_onexResolutionSyncing&&OnexInputGuard.IsActive(_onexScaleChoice)&&_onexScaleChoice.SelectedItem is ComboBoxItem item)await SetOnexResolutionWindowAsync("displayScale",JsonValue.CreateNumberValue((int)item.Tag));};
            body.Children.Add(new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("Resolution saves to the active Performance profile. Orientation and Windows UI scale apply across Windows; Intel GPU scaling remains in Intel Settings."),FontSize=12,TextWrapping=TextWrapping.Wrap,Foreground=OnexSettingsBrush(170,181,193)});
            _onexResolutionStatus=new TextBlock{FontSize=12,TextWrapping=TextWrapping.Wrap};body.Children.Add(_onexResolutionStatus);
            var dialog=CreateOnexSettingsDialog(global::XboxGamingBar.OnexStrings.Get("Resolution & Layout"),global::XboxGamingBar.OnexStrings.Get("Tap the tile:1920×1200 →1680×1050 →1440×900 →1280×800. Hold / X: all supported modes."),body);dialog.PrimaryButtonText="";dialog.CloseButtonText=global::XboxGamingBar.OnexStrings.Get("Close");dialog.DefaultButton=ContentDialogButton.Close;if(dialog.Content is ScrollViewer viewport)viewport.Background=OnexDialogSurface;_onexDetailsDialog=dialog;
            try{RefreshOnexResolutionWindow();var showing=dialog.ShowAsync();await RefreshOnexDisplayAsync(false);RefreshOnexResolutionWindow();await showing;}
            catch(Exception error){ShowOnexDesktopStatus(error.Message);}
            finally{_onexDetailsDialog=null;_onexResolutionChoice=_onexOrientationChoice=_onexScaleChoice=null;_onexResolutionCurrent=_onexResolutionStatus=null;anchor?.Focus(FocusState.Programmatic);}
        }
    }
}
