// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Automation.Provider;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexAboutContrastAsync(JsonObject report)
  {
   int checks=0;var samples=new JsonArray();var findings=new JsonArray();var contexts=new JsonArray();void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
   void Finding(bool ok,string why){checks++;if(!ok)findings.Add(JsonValue.CreateStringValue(why));}
   Color Colour(Brush brush)=>brush is SolidColorBrush solid?solid.Color:Colors.Transparent;
   Color[] Stops(Brush brush)=>brush is SolidColorBrush solid?new[]{solid.Color}:brush is LinearGradientBrush gradient?gradient.GradientStops.Select(stop=>stop.Color).ToArray():new Color[0];
   var prefs=ApplicationData.Current.LocalSettings.Values;bool ready=isThemeInitialized,store=_onexThemeQaStore,expanded=AboutExpandButton.IsChecked==true;
   var original=GetVisibleNavigationItems().FirstOrDefault(item=>item.IsChecked==true);string caption=OnexSupportAuthorCaption.Text;
   var toggle=(IToggleProvider)new ToggleButtonAutomationPeer(AboutExpandButton).GetPattern(PatternInterface.Toggle);
   bool hadGlass=prefs.TryGetValue(OnexGlassSurfaces.Setting,out var glassValue);
   try
   {
    Check(_onexDetailsDialog==null&&!OnexSourceEditorsPending&&!_kbmBusy&&_kbmTuning.Count==0,"Protect pending owner edits");
    _onexThemeQaStore=true;isThemeInitialized=false;EnsureOnexAbout();SystemNavItem.IsChecked=true;
    foreach(string name in OnexAllowedThemeNames.Concat(new[]{"Catppuccin Macchiato","Catppuccin Latte","Windows 11 Light","Miasma"}))
    {
     if(AboutExpandButton.IsChecked==true)toggle.Toggle();Check(AboutContent.Visibility==Visibility.Collapsed,"About did not collapse");
     isThemeInitialized=false;ApplyTheme(name);await Task.Delay(4);UpdateLayout();var theme=_onexAppliedTheme;
     report["lastCaptionContext"]=new JsonObject{{"requested",JsonValue.CreateStringValue(name)},{"applied",JsonValue.CreateStringValue(theme.Name)},{"actualInk",JsonValue.CreateStringValue(Colour(OnexSupportAuthorCaption.Foreground).ToString())},{"expectedInk",JsonValue.CreateStringValue(theme.TextPrimary.ToString())},{"settingsVisible",JsonValue.CreateBooleanValue(SystemScrollViewer.Visibility==Visibility.Visible)},{"aboutExpanded",JsonValue.CreateBooleanValue(AboutExpandButton.IsChecked==true)},{"previewPalette",JsonValue.CreateStringValue(_onexSettingsPreview?.Name??"")}};
     var context=report.GetNamedObject("lastCaptionContext");context["paletteFailurePhase"]=JsonValue.CreateStringValue(_onexPaletteFailurePhase);context["paletteFailureHresult"]=JsonValue.CreateStringValue(_onexPaletteFailureHresult.ToString("X8"));context["paletteFailureType"]=JsonValue.CreateStringValue(_onexPaletteFailureType);context["paletteFailureMethods"]=JsonValue.CreateStringValue(_onexPaletteFailureMethods);context["restoreScopeType"]=JsonValue.CreateStringValue(_onexRestoreScopeType);context["restoreKey"]=JsonValue.CreateStringValue(_onexRestoreKey);context["restoreOperation"]=JsonValue.CreateStringValue(_onexRestoreOperation);context["restoreOriginalType"]=JsonValue.CreateStringValue(_onexRestoreOriginalType);context["restoreKeyWasLocal"]=JsonValue.CreateBooleanValue(_onexRestoreKeyLocal);context["restoreLocalCount"]=JsonValue.CreateNumberValue(_onexRestoreLocalCount);contexts.Add(context);
     Finding(_onexPaletteFailureHresult==0,"Root palette failed: "+name+"/"+_onexPaletteFailurePhase+"/"+_onexPaletteFailureHresult.ToString("X8"));
     Finding(Colour(OnexSupportAuthorCaption.Foreground)==theme.TextPrimary,"Collapsed About support title kept old palette: "+name);
     toggle.Toggle();UpdateLayout();await Task.Delay(4);Check(AboutContent.Visibility==Visibility.Visible,"Native About toggle did not reveal content");
     var ink=Colour(OnexSupportAuthorCaption.Foreground);Finding(ink==theme.TextPrimary,"Expanded title is not current primary ink: "+name);
     Check(OnexSupportAuthorCaption.Text==caption&&!OnexSupportAuthorCaption.IsTextTrimmed&&OnexSupportAuthorCaption.ActualHeight>0,"Localized support title changed or clipped");
     var actualStops=Stops(OnexAboutCard.Background);Check(actualStops.Length>0,"Actual About surface missing");
     foreach(var background in actualStops)Finding(OnexContrast(ink,background)>=4.5,"Actual About surface contrast: "+name);
     foreach(bool glass in new[]{false,true})
     {
      // Exercise the exact production surface factory for both finishes without
      // writing or toggling the owner's persisted Glass preference. The actual
      // current card surface is checked separately above.
      var brush=OnexGlassSurfaces.Create(theme.CardBackground,theme,glass);var stops=Stops(brush);Check(stops.Length>0,"Factory surface missing");
      double ratio=stops.Min(background=>OnexContrast(ink,background));Finding(ratio>=4.5,"Support contrast below4.5: "+name+"/"+glass);
      samples.Add(new JsonObject{{"theme",JsonValue.CreateStringValue(name)},{"glassFactoryPreview",JsonValue.CreateBooleanValue(glass)},{"ink",JsonValue.CreateStringValue(ink.ToString())},{"minimumContrast",JsonValue.CreateNumberValue(ratio)},{"surfaceStops",JsonValue.CreateNumberValue(stops.Length)}});
     }
    }
    Check(prefs.TryGetValue(OnexGlassSurfaces.Setting,out var freshGlass)==hadGlass&&Equals(glassValue,freshGlass),"Owner Glass preference changed");
    report["paletteContexts"]=contexts;report["findings"]=findings;
    report["originalLocalValuesCaptured"]=JsonValue.CreateNumberValue(_onexOriginalLocalValuesCaptured);report["originalInheritedLookupsSkipped"]=JsonValue.CreateNumberValue(_onexOriginalInheritedLookupsSkipped);report["originalCheckedBorderInheritedLookupsSkipped"]=JsonValue.CreateNumberValue(_onexOriginalCheckedBorderInheritedLookupsSkipped);
    report["aboutContrastChecks"]=JsonValue.CreateNumberValue(checks);report["factoryFinishSamples"]=samples;report["currentRealAboutCardRead"]=JsonValue.CreateBooleanValue(true);report["closedAndExpandedChecked"]=JsonValue.CreateBooleanValue(true);
    report["bothGlassModesFactoryPreviewOnly"]=JsonValue.CreateBooleanValue(true);report["ownerGlassPreferenceToggled"]=JsonValue.CreateBooleanValue(false);report["ownerSettingsWrites"]=JsonValue.CreateNumberValue(0);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);report["rasterCapture"]=JsonValue.CreateBooleanValue(false);
    Check(findings.Count==0,"R3palette/ink/contrastfindings; inspect allcapturedstates, thresholdsunchanged");
   }
   finally
   {
    isThemeInitialized=false;ApplyTheme(prefs.TryGetValue("WidgetTheme",out var latest)?latest as string:OnexPalette.Name);isThemeInitialized=ready;_onexThemeQaStore=store;
    if((AboutExpandButton.IsChecked==true)!=expanded)toggle.Toggle();if(original?.Visibility==Visibility.Visible)original.IsChecked=true;
   }
  }
 }
}
