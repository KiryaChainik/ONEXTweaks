// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Automation.Provider;
using Windows.UI.Xaml.Media;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexHomeInputModeDetailsAsync(JsonObject report)
  {
   await ReadOnexControllerHomeAsync();
   var originalTab=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);
   var originalModeState=_kbmState;
   bool modeProjection=_kbmReadOnlyProjection,tabProjection=_onexTabsQaStore;
   if(originalModeState?.ContainsKey("mode")!=true)throw new InvalidOperationException("Live input mode unavailable");
   try
   {
    _kbmReadOnlyProjection=true;
    _onexTabsQaStore=true;
    foreach(bool keyboard in new[]{false,true})
    {
     string mode=keyboard?"keyboardMouse":"gamepad";
     var projection=JsonObject.Parse(originalModeState.Stringify());
     projection["mode"]=JsonValue.CreateStringValue(mode);
     _kbmState=projection;
     RefreshOnexControllerHomeTiles();
     PerformanceNavItem.IsChecked=true;
     var tile=qsTileMap["OnexInputMode"];
     await ShowOnexTileDetailsAsync(tile.TileButton,tile.Id);
     AssertDestination("Hold");

     PerformanceNavItem.IsChecked=true;
     UpdateLayout();
     tile.TileButton.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});
     for(int attempt=0;attempt<10;attempt++)
     {
      await Task.Delay(50);
      if(tile.TileButton.Focus(FocusState.Keyboard)&&OpenFocusedOnexTileDetails())break;
     }
     AssertDestination("Gamepad X");

     void AssertDestination(string action)
     {
      var nav=keyboard?_onexKeyboardMouseNav:LegionNavItem;
      var page=keyboard?_onexKeyboardMousePage:LegionScrollViewer;
      if(nav?.IsChecked!=true||page?.Visibility!=Visibility.Visible||OnexHomeKeyboardMouse!=keyboard)
       throw new InvalidOperationException(action+" opened the wrong settings page for "+mode);
     }
    }
    report["homeInputModeRoutesPassed"]=JsonValue.CreateBooleanValue(true);
   }
   finally
   {
    _kbmState=originalModeState;
    _kbmReadOnlyProjection=modeProjection;
    _onexTabsQaStore=tabProjection;
    RefreshOnexControllerHomeTiles();
    if(originalTab!=null)originalTab.IsChecked=true;
   }
  }
  private async Task VerifyOnexControllerHomeUiAsync(JsonObject report)
  {
   int checks=0,selects=0;void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
   async Task Wait(Func<bool> ready,string why){for(int n=0;n<160;n++){if(ready())return;await Task.Delay(50);}throw new TimeoutException(why);}
   void Invoke(Button button)=>((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
   var original=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);Border fixture=null;MenuFlyout menu=null;Task dialog=null;
   string Layout()=>string.Join("|",ApplicationData.Current.LocalSettings.Values.Where(p=>IsOnexLayoutSetting(p.Key)).OrderBy(p=>p.Key).Select(p=>p.Key+"="+p.Value));
   try{
    Check(!OnexSourceEditorsPending&&_onexDetailsDialog==null,"Preserve owner pending edits");
    await ReadOnexControllerHomeAsync();Check(_onexControllerState.ContainsKey("entries")&&_kbmState.ContainsKey("mode"),"Live controller/mode unavailable");
    string oldLayout=Layout();InsertOnexControllerHomeTiles();Check(Layout()==oldLayout,"Repeated tile migration changed owner layout");
    string[] expected={"OnexWifi","OnexBluetooth","OnexAirplane","PowerMode","ChargeLimiter","CPUBoost","Profile","OnexFanMode","ReShade","LedToggle","OnexControllerProfile","OnexInputMode","FPSCombined","Overlay","OptiScaler","OnexFse","OnexRotationLock","Fullscreen","ActionTaskManager","ActionExplorer","Keyboard","ActionEndTask","OnexAwake","Power"};
    Check(OnexFirstInstallTileIds.SequenceEqual(expected),"Independent24-tile default order mismatch");
    var isolated=expected.Concat(new[]{"OwnerCustom"}).Select(id=>new TileDefinition{Id=id,IsVisible=false,Order=200}).ToArray();SetOnexFirstInstallTileDefaults(isolated);
    Check(isolated.Where(tile=>tile.IsVisible).OrderBy(tile=>tile.Order).Select(tile=>tile.Id).SequenceEqual(expected)&&!isolated.Last().IsVisible,"Fresh default in-memory order/visibility incorrect");
    Check(PlanOnexControllerHomeTileOrder(new[]{"Keyboard","LedToggle","OwnerCustom","Profile"},false,false).SequenceEqual(new[]{"Keyboard","LedToggle","OnexControllerProfile","OnexInputMode","OwnerCustom","Profile"}),"Custom old order not retained by insertion");
    Check(PlanOnexControllerHomeTileOrder(new[]{"LedToggle","OnexControllerProfile","Keyboard"},true,false).SequenceEqual(new[]{"LedToggle","OnexControllerProfile","OnexInputMode","Keyboard"}),"Partial profile tile insertion incorrect");
    Check(PlanOnexControllerHomeTileOrder(new[]{"Keyboard","OnexInputMode","LedToggle"},false,true).SequenceEqual(new[]{"Keyboard","OnexControllerProfile","OnexInputMode","LedToggle"}),"Existing mode position not retained");
    var customOrder=new[]{"OnexInputMode","Keyboard","LedToggle","OnexControllerProfile"};Check(PlanOnexControllerHomeTileOrder(customOrder,true,true).SequenceEqual(customOrder),"Moved new tiles forcibly reset");
    Check(PlanOnexControllerHomeTileOrder(new[]{"Keyboard","Profile"},false,false).SequenceEqual(new[]{"Keyboard","Profile","OnexControllerProfile","OnexInputMode"}),"Missing-LED append fallback incorrect");
    var raw=JsonObject.Parse(ControllerEditorPolicy.Stringify());var effective=JsonObject.Parse(raw.Stringify());effective["virtualTarget"]=JsonValue.CreateStringValue("DualShock4");effective["useHardwareController"]=JsonValue.CreateBooleanValue(false);
    var tuned=effective.GetNamedObject("tuning",new JsonObject());tuned["leftDeadzone"]=JsonValue.CreateNumberValue(9);effective["tuning"]=tuned;
    JsonObject Entry(string id,string name,string modified)=>new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"name",JsonValue.CreateStringValue(name)},{"modifiedUtc",JsonValue.CreateStringValue(modified)},{"dc",JsonObject.Parse(raw.Stringify())},{"ac",JsonObject.Parse(raw.Stringify())},{"effectiveDc",JsonObject.Parse(effective.Stringify())},{"effectiveAc",JsonObject.Parse(effective.Stringify())}};
    var entries=new JsonArray{Entry("global","Global Profile","2026-01-01"),Entry("qa-z","Zulu preview","2026-01-02"),Entry("qa-a","Alpha preview","2026-01-04"),Entry("qa-name","Delete","2026-01-03")};
    var state=new JsonObject{{"activeId",JsonValue.CreateStringValue("global")},{"appliedId",JsonValue.CreateStringValue("global")},{"entries",entries}};
    var catalog=OnexControllerHomeCatalog();catalog.State=()=>state;catalog.Read=()=>Task.FromResult(state);catalog.Change=change=>{Check(change.GetNamedString("operation")=="select","Home chooser changed a profile");state["activeId"]=state["appliedId"]=change.GetNamedValue("id");selects++;return Task.CompletedTask;};
    var section=BuildOnexHomeProfileSelection(catalog);section.Header.Visibility=Visibility.Collapsed;section.Body.Visibility=Visibility.Visible;section.Apply(true);
    fixture=new Border{Background=OnexSurface((_onexAppliedTheme??OnexPalette.CreateTheme()).CardBackground),Margin=new Thickness(12),Child=new ScrollViewer{Content=WrapOnexFocusContent(section.Card),VerticalScrollBarVisibility=ScrollBarVisibility.Auto}};
    Canvas.SetZIndex(fixture,200000);OnexStartupRoot.Children.Add(fixture);UpdateLayout();await Task.Delay(70);
    menu=BuildOnexHomeProfileMenu(catalog,state);Check(menu.Items.OfType<MenuFlyoutItem>().Count()==4&&menu.Items.OfType<MenuFlyoutSeparator>().Count()==1,"Compact controller catalog/grouping incorrect");
    Check(menu.Items.OfType<MenuFlyoutItem>().All(row=>row.Text.Contains("DualShock 4")),"Compact chooser omitted inherited/effective target");
    Check(menu.Items.OfType<MenuFlyoutItem>().Single(row=>row.Tag as string=="qa-name").Text.StartsWith("Delete · ",StringComparison.Ordinal),"Owner-created name translated");
    menu.ShowAt(section.Sort);await Task.Delay(80);var alpha=menu.Items.OfType<MenuFlyoutItem>().Single(row=>row.Tag as string=="qa-a");((IInvokeProvider)new MenuFlyoutItemAutomationPeer(alpha).GetPattern(PatternInterface.Invoke)).Invoke();await Wait(()=>selects==1,"Compact synthetic UI select failed");menu.Hide();menu=null;
    section.Apply(true);Check(section.Rows.Values.All(row=>row.Rename.Visibility==Visibility.Collapsed&&row.Remove.Visibility==Visibility.Collapsed),"Detailed chooser exposed editing actions");
    Check(section.Rows.Values.All(row=>row.Details.Text.Contains("DualShock 4")&&row.Details.Text.Contains("9%")),"Detailed controller summaries used raw policy rather than effective");
    string[][] order={new[]{"qa-a","qa-name","qa-z"},new[]{"qa-a","qa-name","qa-z"},new[]{"qa-z","qa-name","qa-a"}};
    for(int mode=0;mode<3;mode++){section.Sort.SelectedIndex=mode;section.Apply(true);Check(section.Ids.Skip(1).SequenceEqual(order[mode]),"Controller chooser name/date sorting incorrect");}
    Invoke(section.Rows["qa-z"].Choose);await Wait(()=>selects==2,"Detailed synthetic UI select failed");section.Apply(true);
    Check(section.Rows["qa-z"].Active&&!section.Rows["qa-z"].Choose.IsEnabled,"Selected controller profile not marked");
    state["activeId"]=state["appliedId"]=JsonValue.CreateStringValue("");section.Apply(true);Check(section.Rows.Values.All(row=>row.Choose.IsEnabled&&!row.Active),"KBM remembered profile cannot be selected to resume gamepad");
    await SaveOnexImage("onex-controller434-"+OnexStrings.Language+"-chooser.png",fixture,60);OnexStartupRoot.Children.Remove(fixture);fixture=null;

    var beforeKbm=_kbmState;bool modeWas=OnexHomeKeyboardMouse;
    try{
     foreach(bool keyboard in new[]{false,true}){
      var projection=JsonObject.Parse(beforeKbm.Stringify());projection["mode"]=JsonValue.CreateStringValue(keyboard?"keyboardMouse":"gamepad");_kbmState=projection;RefreshOnexControllerHomeTiles();
      Check(qsTileMap["OnexInputMode"].StateText.Text==OnexStrings.Get(keyboard?"Keyboard & mouse":"Gamepad"),"Mode tile caption incorrect");
      Check((qsTileMap["OnexInputMode"].IconElement as Viewbox)?.Tag as string==(keyboard?"keyboardMouse":"gamepad"),"Mode tile icon not updated");
      if(keyboard){Check(qsTileMap["OnexControllerProfile"].StateText.Text==OnexStrings.Get("Off"),"KBM controller tile does not say Off");var projected=OnexControllerHomeCatalogState();Check(projected.GetNamedString("appliedId")==""&&projected.GetNamedString("activeId")=="","Remembered profile incorrectly active in KBM");}
      else Check(OnexMainTileIcon(qsTileMap["OnexInputMode"]) is BitmapIcon originalPad&&originalPad.ShowAsMonochrome&&originalPad.UriSource.AbsoluteUri.EndsWith("/controller_xboxseries.png"),"Gamepad mode uses alternate artwork");
     }
     var art=new StackPanel{Spacing=14};foreach(bool light in new[]{false,true}){
      var ink=OnexSolidBrush(light?Color.FromArgb(255,25,25,25):Colors.White);var row=new StackPanel{Orientation=Orientation.Horizontal,Spacing=24,Padding=new Thickness(18)};
      row.Children.Add(CreateOnexControllerHomeIcon("OnexControllerProfile",56,ink));
      foreach(bool keyboard in new[]{false,true}){var projection=JsonObject.Parse(beforeKbm.Stringify());projection["mode"]=JsonValue.CreateStringValue(keyboard?"keyboardMouse":"gamepad");_kbmState=projection;var icon=(Viewbox)CreateOnexControllerHomeIcon("OnexInputMode",56,ink);Check(((IconElement)icon.Child).Foreground==ink,"Mode artwork tint not owned");if(icon.Child is PathIcon path){var bounds=path.Data.Bounds;Check(bounds.Width>15&&bounds.Height>15&&bounds.Width<=32&&bounds.Height<=32,"Mode vector geometry invalid/clipped");}row.Children.Add(icon);}
      art.Children.Add(new Border{Background=OnexSolidBrush(light?Color.FromArgb(255,245,245,245):Color.FromArgb(255,22,22,22)),Child=row});
     }
     fixture=new Border{Background=OnexSolidBrush(Colors.Gray),Child=art,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};Canvas.SetZIndex(fixture,200000);OnexStartupRoot.Children.Add(fixture);UpdateLayout();await SaveOnexImage("onex-controller434-mode-icons.png",fixture,60);OnexStartupRoot.Children.Remove(fixture);fixture=null;
    }finally{_kbmState=beforeKbm;await ReadOnexControllerHomeAsync();}
    PerformanceNavItem.IsChecked=true;await Task.Delay(80);var homeProfileTile=qsTileMap["OnexControllerProfile"];var oldMenu=_onexProfileTileMenu;
    Check(TryOnexHomeAction(homeProfileTile.TileButton,homeProfileTile.Id),"Profile tile action missing");await Wait(()=>_onexProfileTileMenu!=null&&!ReferenceEquals(oldMenu,_onexProfileTileMenu),"Real controller tile menu did not open");_onexProfileTileMenu.Hide();
    dialog=ShowOnexTileDetailsAsync(homeProfileTile.TileButton,homeProfileTile.Id);await Wait(()=>_onexDetailsDialog!=null&&_onexProfileTileSelection!=null,"Controller hold did not open detailed selection");
    Check(_onexDetailsDialog.PrimaryButtonText==OnexStrings.Get("Go to Controller Profile Settings"),"Controller chooser settings link missing");
    _onexDetailsDialog.ApplyTemplate();await Task.Delay(80);var primary=FindDescendantByName(_onexDetailsDialog,"PrimaryButton") as Button;
    if(primary==null)foreach(var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot)){primary=FindDescendantByName(popup.Child,"PrimaryButton") as Button;if(primary!=null)break;}
    Check(primary!=null,"Controller settings action inaccessible");Invoke(primary);await dialog;dialog=null;Check(LegionNavItem.IsChecked==true,"Controller settings action opened wrong tab");
    await VerifyOnexHomeInputModeDetailsAsync(report);
    Check(Layout()==oldLayout,"Read-only UI test changed saved tile layout");Check(_onexControllerPending==null&&_kbmTuning.Count==0,"Read-only UI test queued settings");
    report["checks"]=JsonValue.CreateNumberValue(checks);report["language"]=JsonValue.CreateStringValue(OnexStrings.Language);report["syntheticSelections"]=JsonValue.CreateNumberValue(selects);
    report["pureLayoutModelsPassed"]=JsonValue.CreateBooleanValue(true);report["modeLabelsAndDynamicIcons"]=JsonValue.CreateBooleanValue(true);report["effectivePolicySummaries"]=JsonValue.CreateBooleanValue(true);report["ownerSettingsWritten"]=JsonValue.CreateBooleanValue(false);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);report["compositionFocusPixelProof"]=JsonValue.CreateBooleanValue(false);
   }finally{
    menu?.Hide();_onexProfileTileMenu?.Hide();if(dialog!=null){_onexDetailsDialog?.Hide();await dialog;}if(fixture!=null)OnexStartupRoot.Children.Remove(fixture);
    await ReadOnexControllerHomeAsync();if(original!=null)original.IsChecked=true;
   }
  }
  private async Task VerifyOnexControllerHomeLiveAsync(JsonObject report,bool restart)
  {
   int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
   async Task Wait(Func<bool> ready,string why){for(int n=0;n<300;n++){if(ready())return;await Task.Delay(100);}throw new TimeoutException(why);}
   void Invoke(Button button)=>((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
   string path=Path.Combine(ApplicationData.Current.LocalFolder.Path,"onex-controller-home434-live-proof.json");
   string lease=Path.Combine(ApplicationData.Current.LocalFolder.Path,"onex-controller-home434-live-lease.json");
   var original=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);
   bool owned=false;string priorId=null,priorPolicy=null;bool priorManual=false;
   try{
    Check(!OnexSourceEditorsPending&&_onexControllerPending==null&&_kbmTuning.Count==0&&_onexDetailsDialog==null,"Preserve owner pending controller edits");
    await ReadOnexControllerHomeAsync();
    if(restart){
     Check(File.Exists(path),"Live selection proof missing");var expected=JsonObject.Parse(File.ReadAllText(path));
     Check(OnexHomeKeyboardMouse==expected.GetNamedBoolean("keyboardMouse"),"Input mode changed after widget restart; fresh state retained");
     Check(ControllerText(_onexControllerState,"appliedId")==expected.GetNamedString("profileId"),"Selected gamepad profile changed; fresh state retained");
     Check(ControllerEditorPolicy.Stringify()==expected.GetNamedString("policy"),"Controller settings changed; fresh state retained");
     RefreshOnexControllerHomeTiles();Check(qsTileMap["OnexInputMode"].TileButton!=null&&qsTileMap["OnexControllerProfile"].TileButton!=null,"New home tiles missing after restart");
     Check((bool)ApplicationData.Current.LocalSettings.Values[OnexControllerHomeLayoutMarker],"Insertion marker not persisted");
     report["checks"]=JsonValue.CreateNumberValue(checks);report["actualWidgetRestartReadback"]=JsonValue.CreateBooleanValue(true);report["ownerPhysicalInputTested"]=JsonValue.CreateBooleanValue(false);File.Delete(path);return;
    }
    Check(!File.Exists(lease)&&!File.Exists(path),"Inspect unfinished own controller tile QA first");
    priorId=ControllerText(_onexControllerState,"appliedId");priorPolicy=ControllerEditorPolicy.Stringify();priorManual=_onexControllerState.GetNamedBoolean("manualOverride",false);
    bool safe=!OnexHomeKeyboardMouse&&ControllerEditorId==priorId&&!OnexHasGame;
    report["liveModeSkippedToPreserveOwnerSession"]=JsonValue.CreateBooleanValue(!safe);
    if(safe){
     File.WriteAllText(lease,new JsonObject{{"profileId",JsonValue.CreateStringValue(priorId)},{"policy",JsonValue.CreateStringValue(priorPolicy)},{"manual",JsonValue.CreateBooleanValue(priorManual)}}.Stringify());owned=true;
     PerformanceNavItem.IsChecked=true;await Task.Delay(100);var modeTile=qsTileMap["OnexInputMode"];Check(modeTile.IsActionable,"Mode tile not actionable");
     Invoke(modeTile.TileButton);await Wait(()=>OnexHomeKeyboardMouse&&!_onexControllerHomeBusy&&_kbmState.GetNamedObject("runtime",new JsonObject()).GetNamedBoolean("running",false),"Real KBM mode did not start through tile");
     Check(qsTileMap["OnexControllerProfile"].StateText.Text==OnexStrings.Get("Off"),"Live KBM profile tile not Off");
     Check((qsTileMap["OnexInputMode"].IconElement as Viewbox)?.Tag as string=="keyboardMouse","Live KBM icon not synchronized");
     Check(ControllerEditorId==priorId&&ControllerEditorPolicy.Stringify()==priorPolicy,"Mode tile changed gamepad profile settings");
     var previousMenu=_onexProfileTileMenu;Check(TryOnexHomeAction(qsTileMap["OnexControllerProfile"].TileButton,"OnexControllerProfile"),"Profile tile did not handle action");
     await Wait(()=>_onexProfileTileMenu!=null&&!ReferenceEquals(previousMenu,_onexProfileTileMenu),"Live controller profile menu missing");
     var item=_onexProfileTileMenu.Items.OfType<MenuFlyoutItem>().Single(row=>row.Tag as string==priorId);
     Check(item.IsEnabled,"Remembered profile is disabled in live KBM mode");
     ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(item).GetPattern(PatternInterface.Invoke)).Invoke();
     await Wait(()=>!OnexHomeKeyboardMouse&&!_onexControllerHomeBusy&&ControllerText(_onexControllerState,"appliedId")==priorId,"Profile choice did not resume Gamepad");
     _onexProfileTileMenu.Hide();
     Check(OnexMainTileIcon(qsTileMap["OnexInputMode"]) is BitmapIcon,"Live Gamepad icon not restored");
     Check(ControllerEditorPolicy.Stringify()==priorPolicy,"Resume altered controller policy");
     if(!priorManual)await ChangeOnexControllerAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("automatic")}});
     await ReadOnexControllerHomeAsync();Check(ControllerText(_onexControllerState,"appliedId")==priorId&&_onexControllerState.GetNamedBoolean("manualOverride",false)==priorManual,"Original selection policy not restored");
     Check(ControllerEditorPolicy.Stringify()==priorPolicy&&!OnexHomeKeyboardMouse,"Owner mode/profile settings not preserved");
     File.Delete(lease);owned=false;
     report["realViiperKbmCaptureStarted"]=JsonValue.CreateBooleanValue(true);report["realHomeProfileChoiceResumedGamepad"]=JsonValue.CreateBooleanValue(true);report["originalSelectionModeRestored"]=JsonValue.CreateBooleanValue(true);
    }
    File.WriteAllText(path,new JsonObject{{"keyboardMouse",JsonValue.CreateBooleanValue(OnexHomeKeyboardMouse)},{"profileId",JsonValue.CreateStringValue(ControllerText(_onexControllerState,"appliedId"))},{"policy",JsonValue.CreateStringValue(ControllerEditorPolicy.Stringify())}}.Stringify());
    report["checks"]=JsonValue.CreateNumberValue(checks);report["ownerPhysicalInputTested"]=JsonValue.CreateBooleanValue(false);report["controllerPolicyWrites"]=JsonValue.CreateNumberValue(0);
   }finally{
    _onexProfileTileMenu?.Hide();
    if(owned){
     await ReadOnexControllerHomeAsync();
     if(ControllerText(_onexControllerState,"appliedId")==priorId&&ControllerEditorPolicy.Stringify()==priorPolicy){
      if(OnexHomeKeyboardMouse)await SetOnexHomeInputModeCoreAsync("gamepad");
      if(!priorManual&&_onexControllerState.GetNamedBoolean("manualOverride",false))await ChangeOnexControllerAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("automatic")}});
      await ReadOnexControllerHomeAsync();if(!OnexHomeKeyboardMouse&&_onexControllerState.GetNamedBoolean("manualOverride",false)==priorManual)File.Delete(lease);
     }
    }
    if(original!=null)original.IsChecked=true;
   }
  }

 }
}
