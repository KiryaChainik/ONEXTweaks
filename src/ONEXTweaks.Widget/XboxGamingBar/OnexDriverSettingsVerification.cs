using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Automation.Provider;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexDriverSettingsAsync(JsonObject report,bool checkOfficial=true)
  {
   int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
   var values=ApplicationData.Current.LocalSettings.Values;string oldTheme=values.TryGetValue("WidgetTheme",out var old)?old as string:null;
   bool expanded=_onexDriversExpand?.IsChecked==true,initialized=isThemeInitialized;
   var original=GetVisibleNavigationItems().FirstOrDefault(x=>x.IsChecked==true);
   async Task Idle(){for(int n=0;n<300&&_onexDriversBusy;n++)await Task.Delay(500);Check(!_onexDriversBusy,"Component manager did not finish");}
   try
   {
    EnsureOnexDriverSettings();SystemNavItem.IsChecked=true;
    var toggle=(IToggleProvider)new ToggleButtonAutomationPeer(_onexDriversExpand).GetPattern(PatternInterface.Toggle);
    if(_onexDriversExpand.IsChecked==true)toggle.Toggle();Check(_onexDriversBody.Visibility==Visibility.Collapsed,"Driver card did not collapse");
    toggle.Toggle();await Idle();Check(_onexDriverData.Count>=10,"Actual component inventory is incomplete: "+_onexDriversStatus.Text);
    foreach(string id in new[]{"pawnio","hidhide","usbip","viiper","rtss","graphics","dotnet","inteldsa","onexconsole","onexdrivers"})Check(_onexDriverButtons.ContainsKey(id),"Missing installed component: "+id);
    if(checkOfficial)await RunOnexDriverOperationAsync("check");
    var result=await ReadOnexDriverResultAsync();Check(result!=null&&result.GetNamedBoolean("done",false)&&result.GetNamedBoolean("ok",false),"Real official release check failed");
    foreach(string id in new[]{"pawnio","hidhide","usbip","viiper"}){
     var row=_onexDriverData.Select(x=>x.GetObject()).Single(x=>x.GetNamedString("id")==id);
     Check(!string.IsNullOrEmpty(row.GetNamedString("current","")),"Installed version missing: "+id);
     Check(!string.IsNullOrEmpty(row.GetNamedString("latest",""))&&!string.IsNullOrEmpty(row.GetNamedString("lastChecked","")),"Official latest version/check date missing: "+id);
     Check(row.GetNamedString("source","").StartsWith("https://github.com/",StringComparison.Ordinal),"Wrong official release source: "+id);
     Check(row.GetNamedString("status","")=="Up to date"||row.GetNamedString("status","")=="New version detected","Actual GitHub status failed: "+id);
    }
    var intel=_onexDriverData.Select(x=>x.GetObject()).Single(x=>x.GetNamedString("id")=="inteldsa");
    Check(intel.GetNamedBoolean("installed",false)&&!string.IsNullOrEmpty(intel.GetNamedString("current","")),"Intel DSA prerequisite is not installed");
    Check(intel.GetNamedString("source","")=="https://www.intel.com/content/www/us/en/support/detect.html","Installed Intel DSA action is wrong");
    var oem=_onexDriverData.Select(x=>x.GetObject()).Single(x=>x.GetNamedString("id")=="onexconsole");
    Check(oem.GetNamedString("source","")=="https://onexplayerstore.com/pages/drivers-and-faqs"&&!oem.GetNamedBoolean("canUpdate",false),"OEM catalog action misreported as automatic update");
    var pack=_onexDriverData.Select(x=>x.GetObject()).Single(x=>x.GetNamedString("id")=="onexdrivers");
    Check(pack.GetNamedString("source","")=="https://drive.google.com/file/d/197i2xoRkOVQjRfURqhbAprVZKd6WC_oV/view?usp=sharing","Actual supported OEM model pack was not confirmed on the official catalog");
    Check(pack.GetNamedString("latest","")=="1.1"&&!string.IsNullOrEmpty(pack.GetNamedString("lastChecked","")),"OEM package/check metadata missing");
    Check(pack.GetNamedString("status","")=="Vendor-managed"&&!pack.GetNamedBoolean("canUpdate",false),"OEM catalog availability misreported as installed update");
    Check(pack.GetNamedBoolean("catalogVerified",false)||pack.GetNamedString("detail","").StartsWith("Live catalog unavailable.",StringComparison.Ordinal),"Unavailable OEM catalog was not clearly reported");
    Check(intel.GetNamedString("service","").Contains("DSA: ")&&intel.GetNamedString("service","").Contains("Updater: "),"Actual Intel DSA service inventory missing");
    foreach(string theme in new[]{OnexPalette.Name,"Windows 11 Light"}){
     isThemeInitialized=false;ApplyTheme(theme);UpdateLayout();await Task.Delay(160);
     foreach(var pair in _onexDriverButtons){
      Check(pair.Value.Focus(FocusState.Keyboard),"Component row cannot focus: "+pair.Key);
      Check(OnexPageNavigationRows().SelectMany(x=>x).Any(x=>ReferenceEquals(x.Control,pair.Value)),"Component row missing from gamepad navigation: "+pair.Key);
      var content=(StackPanel)pair.Value.Content;
      Check(content.Children.OfType<TextBlock>().All(x=>x.TextWrapping==TextWrapping.Wrap&&!x.IsTextTrimmed&&x.ActualHeight>0),"Component row text clips: "+pair.Key);
     }
     Check(_onexDriversExpand.Focus(FocusState.Keyboard),"Driver header cannot regain focus");
     var bounds=_onexDriversCard.TransformToVisual(SystemScrollViewer.Content as UIElement).TransformBounds(new Windows.Foundation.Rect(0,0,_onexDriversCard.ActualWidth,_onexDriversCard.ActualHeight));
     SystemScrollViewer.ChangeView(null,bounds.Y,null,true);await Task.Delay(160);
     string prefix="onex-drivers"+"385"+"-"+(theme==OnexPalette.Name?"dark":"light");
     await SaveOnexImage(prefix+"-full.png",_onexDriversCard,60);await SaveOnexImage(prefix+"-top.png",SystemScrollViewer,60);
     SystemScrollViewer.ChangeView(null,bounds.Bottom-SystemScrollViewer.ViewportHeight,null,true);await Task.Delay(140);
     await SaveOnexImage(prefix+"-bottom.png",SystemScrollViewer,60);
    }
    Check((values.TryGetValue("WidgetTheme",out var fresh)?fresh as string:null)==oldTheme,"Driver UI wrote owner theme");
    report["checks"]=JsonValue.CreateNumberValue(checks);report["realPackagedManagerActivation"]=JsonValue.CreateBooleanValue(true);
    report["actualRegistryAndOfficialGitHub"]=JsonValue.CreateBooleanValue(checkOfficial);report["reusedAcceptedOfficialReleaseCache"]=JsonValue.CreateBooleanValue(!checkOfficial);report["actualOwnUwpRendering"]=JsonValue.CreateBooleanValue(true);
    report["installerExecuted"]=JsonValue.CreateBooleanValue(false);report["hardwareWritesRequested"]=JsonValue.CreateNumberValue(0);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);
    report["actualSupportedModel"]=JsonValue.CreateBooleanValue(true);report["actualOemCatalogConfirmed"]=JsonValue.CreateBooleanValue(pack.GetNamedBoolean("catalogVerified",false));report["vendorExternalWorkflow"]=JsonValue.CreateBooleanValue(true);
   }
   finally
   {
    if(_onexDriversExpand.IsChecked!=expanded)_onexDriversExpand.IsChecked=expanded;
    isThemeInitialized=false;ApplyTheme(values.TryGetValue("WidgetTheme",out var fresh)?fresh as string:OnexPalette.Name);isThemeInitialized=initialized;
    if(original!=null&&original.Visibility==Visibility.Visible)original.IsChecked=true;
   }
  }
 }
}
