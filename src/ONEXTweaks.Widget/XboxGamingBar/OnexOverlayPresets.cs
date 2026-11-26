// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private ContentControl _onexOverlaySettingsGate;private JsonObject _onexOverlayPresets;private OnexScopedProfilePicker _onexOverlayPresetPicker;private Border _onexOverlayPresetHeader;private OnexSavedCatalogSection _onexOverlaySavedPresets;private bool _onexOverlayPresetChanging;
  private void EnsureOnexOverlayPresets()
  {
   if(_onexOverlayPresetPicker!=null||_releaseOsdNav==null)return;
   var settingsBody=OnexOverlaySettingsCard.Child;OnexOverlaySettingsCard.Child=null;_onexOverlaySettingsGate=new ContentControl{Content=settingsBody,IsTabStop=false,HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Top};OnexOverlaySettingsCard.Child=_onexOverlaySettingsGate;
   _onexOverlayPresetPicker=new OnexScopedProfilePicker(ReadOnexOverlayPresetsAsync,ChangeOnexOverlayPresetsAsync,()=>CreateOnexMenu(FlyoutPlacementMode.Bottom),DescribeOnexOverlayPreset,(Style)Resources["StandaloneButtonStyle"],tileTextBrush,ApplyOnexTileWindowControlAccent);
   _onexOverlayPresetHeader=OnexSharedProfileCard(_onexOverlayPresetPicker,"OverlayPreset",global::XboxGamingBar.OnexStrings.Get("Пресет сохраняет макет, показатели, шрифт, размер и цвета. Изменения сохраняются автоматически в выбранный пресет."));_onexOverlayPresetPicker.Split.Visibility=Visibility.Collapsed;_onexOverlayPresetPicker.Split.IsTabStop=false;_onexOverlayPresetPicker.Footer.Visibility=Visibility.Collapsed;
   OnexOverlayBody.Children.Remove(OnexOverlayConnectionCaption);_onexOverlayPresetPicker.Root.Children.Add(OnexOverlayConnectionCaption);OnexOverlayBody.Children.Insert(0,_onexOverlayPresetHeader);
   _onexOverlaySavedPresets=new OnexSavedCatalogSection(this,"OverlayPreset",()=>_onexOverlayPresets,ChangeOnexOverlayPresetsAsync,DescribeOnexOverlayPreset);OnexOverlayBody.Children.Add(_onexOverlaySavedPresets.Card);
   if(_onexOverlayPresets!=null){_onexOverlayPresetPicker.Apply(_onexOverlayPresets);_onexOverlaySavedPresets.Apply();}
   _releaseOsdNav.Checked+=async(s,e)=>{try{await ReadOnexOverlayPresetsAsync();}catch(Exception error){_onexOverlayCaption.Text=error.Message;}};
  }
  private string DescribeOnexOverlayPreset(JsonObject values)
  {
   int level=(int)values.GetNamedNumber("level",0);var map=values.GetNamedString("config","").Split(';').Where(part=>part.Contains(":")).Select(part=>new{Key=part.Substring(0,part.IndexOf(':')),Value=part.Substring(part.IndexOf(':')+1)}).GroupBy(pair=>pair.Key).ToDictionary(group=>group.Key,group=>group.Last().Value);
   string Read(string key,string fallback)=>map.TryGetValue(key,out var value)?value:fallback;
   return OnexOverlayNames[Math.Max(0,Math.Min(4,level))]+" · "+Read("TextSize","150")+"% · "+Read("Font","Bahnschrift|400").Split('|')[0]+global::XboxGamingBar.OnexStrings.Get("\nOpacity ")+Read("Opacity","100")+global::XboxGamingBar.OnexStrings.Get("% · Background ")+(Read("Background","0|60").StartsWith("1|")?global::XboxGamingBar.OnexStrings.Get("On"):global::XboxGamingBar.OnexStrings.Get("Off"));
  }
  private void ApplyOnexOverlayPresetsState(string json)
  {
   if(!JsonObject.TryParse(json,out var state))return;_onexOverlayPresets=state;OnexFrontend.OverlayPresetId=state.GetNamedString("activeId","global");EnsureOnexOverlayPresets();_onexOverlayPresetPicker?.Apply(state);_onexOverlaySavedPresets?.Apply();_onexProfileTileSelection?.Apply();ApplyOnexPageSpacing();ApplyOnexTileAvailability();
  }
  private async Task<JsonObject> ReadOnexOverlayPresetsAsync()
  {
   var reply=await App.SendMessageAsync(new ValueSet{{"OnexOverlayPresetsGet",true}});if(reply==null||reply.ContainsKey("Error"))throw new InvalidOperationException(reply?.ContainsKey("Error")==true?reply["Error"].ToString():"Overlay presets unavailable");ApplyOnexOverlayPresetsState((string)reply["OnexOverlayPresetsState"]);return _onexOverlayPresets;
  }
  private async Task ChangeOnexOverlayPresetsAsync(JsonObject mutation)
  {
   var owner=App.PipeClient;
   if(_onexOverlayPresetChanging||_onexOverlayOpening||_onexOverlayBusy||isLoadingOSDConfig||OnexInputGuard.IsPreviewing(OSDOpacitySlider)||OnexInputGuard.IsPreviewing(OSDBackgroundOpacitySlider))throw new InvalidOperationException(global::XboxGamingBar.OnexStrings.Get("Подтверди настройку и повтори выбор пресета."));
   _onexOverlayPresetChanging=true;
   try{
   for(int n=0;n<40&&(_onexOverlayConfigWrites>0||osdBackgroundDebounceTimer?.IsEnabled==true);n++)await Task.Delay(30);
   RequireOnexProfileConnection(owner);
   if(_onexOverlayConfigWrites>0||osdBackgroundDebounceTimer?.IsEnabled==true)throw new InvalidOperationException(global::XboxGamingBar.OnexStrings.Get("Дождись сохранения оверлея."));
   if(_onexOverlaySettingsGate!=null)_onexOverlaySettingsGate.IsEnabled=false;ApplyOnexTileAvailability();
   try{var reply=await SendOnexHomeRequestAsync(new ValueSet{{"OnexOverlayPresetsSet",mutation.Stringify()}});ApplyOnexOverlayPresetsState((string)reply["OnexOverlayPresetsState"]);if(reply.TryGetValue("OnexOverlayState",out var state))ApplyOnexOverlayState((string)state);if(mutation.GetNamedString("operation","")=="select"){SyncOsdCustomizeLevelToActiveOverlay();await ReadOnexOverlayConfigurationAsync();}}
   finally{if(_onexOverlaySettingsGate!=null)_onexOverlaySettingsGate.IsEnabled=true;}
   }finally{_onexOverlayPresetChanging=false;ApplyOnexTileAvailability();}
  }
 }
}
