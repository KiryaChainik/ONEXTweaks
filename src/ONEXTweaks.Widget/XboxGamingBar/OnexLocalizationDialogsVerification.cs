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
  private async Task VerifyOnexLocaleSurfacesAsync(Action<DependencyObject> inspect,Action<string> caption,Action<bool,string> check,JsonObject report)
  {
   int sections=0,menus=0,dialogs=0,fixtures=0,mutations=0;
   string perf=_onexPerfOpen,controller=_onexControllerOpen;
   try
   {
    _releasePowerNav.IsChecked=true;
    if(_onexPerfSections!=null)foreach(var section in _onexPerfSections)
    {
     if(_onexPerfOpen!=section.Id)await SetOnexPerfCardAsync(section.Id);
     UpdateLayout();await Task.Delay(100);inspect(section.Detail);sections++;
    }
   }
   finally{if(_onexPerfOpen!=perf)await SetOnexPerfCardAsync(perf);}
   try
   {
    LegionNavItem.IsChecked=true;
    if(_onexControllerSections!=null)foreach(var section in _onexControllerSections)
    {
     if(!section.Header.IsEnabled)continue;
     if(_onexControllerOpen!=section.Id)SetOnexControllerCard(section.Id);
     UpdateLayout();await Task.Delay(70);inspect(section.Detail);sections++;
    }
   }
   finally{if(_onexControllerOpen!=controller)SetOnexControllerCard(controller);}
   SystemNavItem.IsChecked=true;
   var parent=(Panel)SystemScrollViewer.Content;
   var host=new StackPanel{Spacing=12};parent.Children.Add(host);
   try
   {
    void Menu(MenuFlyout menu)
    {
     foreach(var item in menu.Items){if(item is MenuFlyoutItem choice)caption(choice.Text);else if(item is MenuFlyoutSubItem sub){caption(sub.Text);foreach(var child in sub.Items.OfType<MenuFlyoutItem>())caption(child.Text);}}
    }
    foreach(string scope in new[]{"Fan","ScreenColor","Controller","OverlayPreset"})
    {
     var entries=new JsonArray{
      new JsonObject{{"id",JsonValue.CreateStringValue("global")},{"name",JsonValue.CreateStringValue("Global Profile")},{"dc",new JsonObject()},{"ac",new JsonObject()}},
      new JsonObject{{"id",JsonValue.CreateStringValue("qa-locale-owner-name")},{"name",JsonValue.CreateStringValue("Delete")},{"dc",new JsonObject()},{"ac",new JsonObject()}}
     };
     var state=new JsonObject{{"activeId",JsonValue.CreateStringValue("qa-locale-owner-name")},{"entries",entries},{"candidates",new JsonArray()}};
     bool preset=scope=="OverlayPreset";if(preset)state["presetCatalog"]=JsonValue.CreateBooleanValue(true);
     if(scope=="Fan")state["creationParents"]=new JsonArray();
     MenuFlyout current=null;
     var picker=new OnexScopedProfilePicker(()=>Task.FromResult(state),value=>{mutations++;return Task.CompletedTask;},()=>current=CreateOnexMenu(),value=>OnexStrings.Get("Custom curve"),(Style)Resources["StandaloneButtonStyle"],tileTextBrush,control=>{});
     picker.Apply(state);host.Children.Add(picker.Root);picker.Root.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});UpdateLayout();
     check((string)picker.Choose.Content==OnexStrings.Get(preset?"Choose Preset":"Choose Profile"),"Choose caption differs from language");
     check((string)picker.Create.Content==OnexStrings.Get(preset?"+ Create Preset":"+ Create Profile"),"Create caption differs from language");
     check(OnexOwnedContent(picker.Root).OfType<TextBlock>().Any(text=>text.Text=="Delete"),"Custom profile name was translated");
     await picker.OpenChooseAsync();await Task.Delay(60);if(current!=null){Menu(current);current.Hide();menus++;}
     await picker.OpenCreateAsync();await Task.Delay(60);if(current!=null){Menu(current);current.Hide();menus++;}
     var saved=new OnexSavedCatalogSection(this,scope,()=>state,value=>{mutations++;return Task.CompletedTask;},value=>OnexStrings.Get("Custom curve"));
     host.Children.Add(saved.Card);saved.Body.Visibility=Visibility.Visible;saved.Apply(true);UpdateLayout();inspect(saved.Card);fixtures++;
     check(saved.Rows["qa-locale-owner-name"].Name.Text=="Delete","Saved card translated a custom name");
     host.Children.Remove(saved.Card);host.Children.Remove(picker.Root);
    }
    foreach(bool preset in new[]{false,true})
    {
     var pending=PromptOnexProfileNameAsync("Delete",preset);await Task.Delay(100);
     var dialog=_onexDetailsDialog;check(dialog!=null,"Actual rename dialog did not open");
     check(dialog.PrimaryButtonText==OnexStrings.Get("Rename")&&dialog.CloseButtonText==OnexStrings.Get("Cancel"),"Rename dialog buttons not localized");
     inspect(dialog);caption(dialog.Title as string);dialog.Hide();await pending;dialogs++;
    }
    var removeEntry=new JsonObject{{"id",JsonValue.CreateStringValue("qa-locale-no-persisted-record")},{"name",JsonValue.CreateStringValue("Delete")}};
    var deletion=DeleteOnexSavedProfileAsync(removeEntry);await Task.Delay(100);var confirm=_onexDetailsDialog;
    check(confirm!=null,"Actual delete confirmation did not open");
    check(confirm.PrimaryButtonText==OnexStrings.Get("Delete")&&confirm.CloseButtonText==OnexStrings.Get("Keep It"),"Delete/Keep buttons not localized");
    inspect(confirm);caption(confirm.Title as string);confirm.Hide();await deletion;dialogs++;
    EnsureOnexDriverSettings();var realRows=_onexDriverData;var driverVisible=_onexDriversBody.Visibility==Visibility.Visible;
    try
    {
     var rows=new JsonArray();int rowIndex=0;
     foreach(string status in new[]{"Up to date","New version detected","Not installed","Vendor-managed"})
      rows.Add(new JsonObject{{"id",JsonValue.CreateStringValue("qa-locale-driver-"+(rowIndex++))},{"name",JsonValue.CreateStringValue("VIIPER")},{"status",JsonValue.CreateStringValue(status)},{"purpose",JsonValue.CreateStringValue("Virtual Controller")},{"current",JsonValue.CreateStringValue("1.0")},{"latest",JsonValue.CreateStringValue("1.1")},{"service",JsonValue.CreateStringValue("Running · Updater: Stopped")},{"detail",JsonValue.CreateStringValue("Ready")}});
     ApplyOnexDriverRows(rows);SetOnexDriversExpanded(true);UpdateLayout();inspect(_onexDriverRows);
     foreach(string status in new[]{"Up to date","New version detected","Not installed","Vendor-managed"})
      check(OnexOwnedContent(_onexDriverRows).OfType<TextBlock>().Any(text=>text.Text==OnexStrings.Get(status)),"Driver status differs from selected language");
     report["syntheticDriverRowsRenderedByProductionUi"]=JsonValue.CreateNumberValue(rows.Count);
     report["driverChecksDownloadsOrInstallsRequested"]=JsonValue.CreateNumberValue(0);
    }
    finally{ApplyOnexDriverRows(realRows);SetOnexDriversExpanded(driverVisible);}
    check(mutations==0,"Read-only locale fixture requested a profile change");
   }
   finally{_onexDetailsDialog?.Hide();parent.Children.Remove(host);}
   report["expandedPerformanceControllerSections"]=JsonValue.CreateNumberValue(sections);
   report["actualChooseCreateMenus"]=JsonValue.CreateNumberValue(menus);
   report["actualCancelOnlyProfileDialogs"]=JsonValue.CreateNumberValue(dialogs);
   report["syntheticSavedCatalogFixtures"]=JsonValue.CreateNumberValue(fixtures);
   report["syntheticFixtureMutationRequests"]=JsonValue.CreateNumberValue(mutations);
  }
 }
}
