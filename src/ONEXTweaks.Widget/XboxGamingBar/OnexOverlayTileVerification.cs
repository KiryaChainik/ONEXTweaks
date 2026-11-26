// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Automation.Provider;
using Windows.UI.Xaml.Media;

namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexOverlayTilePickerAsync(JsonObject report)
  {
   int checks=0,changes=0;void Check(bool value,string why){checks++;if(!value)throw new InvalidOperationException(why);}
   async Task Wait(Func<bool> ready,string why){for(int n=0;n<160;n++){if(ready())return;await Task.Delay(50);}throw new TimeoutException(why);}
   void Invoke(Button button)=>((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
   void InvokeItem(MenuFlyoutItem item)=>((IInvokeProvider)new MenuFlyoutItemAutomationPeer(item).GetPattern(PatternInterface.Invoke)).Invoke();
   var original=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);MenuFlyout fixtureMenu=null;Border fixture=null;Task detail=null;
   try{
    Check(_onexDetailsDialog==null&&!OnexSourceEditorsPending,"Protect pending owner edits");
    await ReadOnexOverlayPresetsAsync();Check(_onexOverlayPresets.GetNamedArray("entries").Count>=5,"Real preset catalog missing standard entries");
    string realActive=_onexOverlayPresets.GetNamedString("activeId");
    var entries=new JsonArray();
    JsonObject Entry(string id,string name,int level,string modified,bool builtin)=>new JsonObject{
     {"id",JsonValue.CreateStringValue(id)},{"name",JsonValue.CreateStringValue(name)},{"builtIn",JsonValue.CreateBooleanValue(builtin)},{"modifiedUtc",JsonValue.CreateStringValue(modified)},{"split",JsonValue.CreateBooleanValue(false)},
     {"dc",new JsonObject{{"level",JsonValue.CreateNumberValue(level)},{"config",JsonValue.CreateStringValue("TextSize:150;Font:Bahnschrift|400;Opacity:90;Background:1|60")}}}
    };
    entries.Add(Entry("global","Default Preset",0,"2026-01-01T00:00:00Z",true));
    string[] standard={"Basic","Horizontal","Horizontal Custom","Vertical Custom"};
    for(int i=0;i<4;i++)entries.Add(Entry("firmware-overlay-"+(i+1),standard[i],i+1,"2026-01-01T00:00:00Z",true));
    entries.Add(Entry("qa-z","Zulu preview",3,"2026-01-02T00:00:00Z",false));
    entries.Add(Entry("qa-a","Alpha preview",1,"2026-01-04T00:00:00Z",false));
    entries.Add(Entry("qa-name","Delete",2,"2026-01-03T00:00:00Z",false));
    var state=new JsonObject{{"activeId",JsonValue.CreateStringValue("global")},{"appliedId",JsonValue.CreateStringValue("global")},{"entries",entries}};
    var catalog=OnexOverlayTileCatalog();catalog.Read=()=>Task.FromResult(state);catalog.State=()=>state;catalog.Change=change=>{
     Check(change.GetNamedString("operation")=="select","Chooser exposed a non-select mutation");
     state["activeId"]=state["appliedId"]=JsonValue.CreateStringValue(change.GetNamedString("id"));changes++;return Task.CompletedTask;
    };
    var section=BuildOnexHomeProfileSelection(catalog);section.Header.Visibility=Visibility.Collapsed;section.Body.Visibility=Visibility.Visible;section.Apply(true);
    fixture=new Border{Background=OnexSurface((_onexAppliedTheme??OnexPalette.CreateTheme()).CardBackground),Margin=new Thickness(12),Child=new ScrollViewer{Content=WrapOnexFocusContent(section.Card),VerticalScrollBarVisibility=ScrollBarVisibility.Auto}};
    Canvas.SetZIndex(fixture,200000);OnexStartupRoot.Children.Add(fixture);UpdateLayout();await Task.Delay(100);
    fixtureMenu=BuildOnexHomeProfileMenu(catalog,state);
    Check(fixtureMenu.Items.Count==9&&fixtureMenu.Items.OfType<MenuFlyoutSeparator>().Count()==1,"Compact preset grouping/divider incorrect");
    var choices=fixtureMenu.Items.OfType<MenuFlyoutItem>().ToArray();
    Check(choices.Take(5).All(item=>item.Tag as string=="global"||(item.Tag as string).StartsWith("firmware-",StringComparison.Ordinal)),"Built-in presets not first");
    Check(choices.Last().Tag as string=="qa-z","Compact custom A-Z order incorrect");
    Check(choices.All(item=>!item.Text.Contains("\n")&&item.Text.Contains("150%")),"Compact summaries missing layout/size or multiline");
    Check(choices.Single(item=>item.Tag as string=="qa-name").Text.StartsWith("Delete · ",StringComparison.Ordinal),"Owner name was translated");
    for(int i=0;i<4;i++)Check(choices.Single(item=>item.Tag as string=="firmware-overlay-"+(i+1)).Text.StartsWith(standard[i]+" · ",StringComparison.Ordinal),"RTSS standard name changed");
    Check(choices.Single(item=>item.Tag as string=="global").Icon is SymbolIcon&&!choices.Single(item=>item.Tag as string=="global").IsEnabled,"Current preset not marked");
    fixtureMenu.ShowAt(section.Sort);await Task.Delay(120);
    InvokeItem(choices.Single(item=>item.Tag as string=="qa-a"));await Wait(()=>changes==1,"Compact UIA select did not dispatch");fixtureMenu.Hide();fixtureMenu=null;
    section.Apply(true);
    Check(section.Rows.Count==8&&section.Ids.Take(5).All(id=>id=="global"||id.StartsWith("firmware-",StringComparison.Ordinal)),"Detailed rows/grouping incorrect");
    Check(section.List.Children.OfType<Border>().Count(border=>border.Height==1)==1,"Detailed divider incorrect");
    Check(section.Rows.Values.All(row=>row.Rename.Visibility==Visibility.Collapsed&&row.Remove.Visibility==Visibility.Collapsed),"Selection chooser has edit/delete actions");
    Check(section.Rows.Values.All(row=>row.Choose.IsEnabled==!row.Active),"Selectable preset incorrectly disabled");
    Check(section.Rows["qa-a"].Active&&!section.Rows["qa-a"].Choose.IsEnabled,"Detailed active marker not synchronized");
    Check(section.Rows.Values.All(row=>row.Details.Text.Contains("Bahnschrift")&&row.Details.Text.Contains("90")),"Detailed font/opacity summary missing");
    string[][] sorted={new[]{"qa-a","qa-name","qa-z"},new[]{"qa-a","qa-name","qa-z"},new[]{"qa-z","qa-name","qa-a"}};
    for(int mode=0;mode<3;mode++){section.Sort.SelectedIndex=mode;section.Apply(true);Check(section.Ids.Skip(5).SequenceEqual(sorted[mode]),"Detailed name/date sorting incorrect");}
    section.Sort.SelectedIndex=0;Invoke(section.Rows["qa-z"].Choose);await Wait(()=>changes==2,"Detailed UIA select did not dispatch");section.Apply(true);
    Check(section.Rows["qa-z"].Active,"Detailed current marker did not refresh");
    await SaveOnexImage("onex-overlay433-"+OnexStrings.Language+"-selection.png",fixture,60);
    OnexStartupRoot.Children.Remove(fixture);fixture=null;
    Check(_onexOverlayPresets.GetNamedString("activeId")==realActive,"Injected catalog changed real owner preset");
    PerformanceNavItem.IsChecked=true;await Task.Delay(120);var tile=qsTileMap["Overlay"];var previousMenu=_onexProfileTileMenu;int activeLevel=OnexOverlayLevel;
    Check(TryOnexHomeAction(tile.TileButton,"Overlay"),"Home tap route not handled");
    await Wait(()=>_onexProfileTileMenu!=null&&!ReferenceEquals(previousMenu,_onexProfileTileMenu),"Home tap did not build catalog");
    Check(_onexProfileTileMenu.Items.OfType<MenuFlyoutItem>().Count()==_onexOverlayPresets.GetNamedArray("entries").Count,"Home tap still uses legacy five levels");
    Check(activeLevel==OnexOverlayLevel&&realActive==_onexOverlayPresets.GetNamedString("activeId"),"Opening compact list changed overlay");
    _onexProfileTileMenu.Hide();await Task.Delay(100);
    detail=ShowOnexTileDetailsAsync(tile.TileButton,"Overlay");await Wait(()=>_onexDetailsDialog!=null&&_onexProfileTileSelection!=null,"Hold/detail route did not open preset cards");
    Check(_onexDetailsDialog.Title as string==OnexStrings.Get("Overlay Presets"),"Detail dialog title not localized preset terminology");
    Check(_onexDetailsDialog.PrimaryButtonText==OnexStrings.Get("Go to Overlay Settings"),"Detail settings link missing");
    Check(_onexProfileTileSelection.Rows.Count==_onexOverlayPresets.GetNamedArray("entries").Count,"Real detailed catalog loses presets");
    _onexDetailsDialog.ApplyTemplate();await Task.Delay(100);var primary=FindDescendantByName(_onexDetailsDialog,"PrimaryButton") as Button;
    if(primary==null)foreach(var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot)){primary=FindDescendantByName(popup.Child,"PrimaryButton") as Button;if(primary!=null)break;}
    Check(primary!=null,"Detail dialog settings action not accessible");Invoke(primary);await detail;detail=null;
    Check(_releaseOsdNav.IsChecked==true&&_onexOverlayPresetHeader.IsLoaded,"Settings action opened wrong page");
    Check(OnexOwnedContent(_onexOverlayPresetPicker.Root).OfType<TextBlock>().Any(text=>text.Text==OnexCatalogEntryName(_onexOverlayPresets.GetNamedArray("entries").Select(value=>value.GetObject()).Single(entry=>entry.GetNamedString("id")==realActive),true)),"Settings selected preset not synchronized");
    report["checks"]=JsonValue.CreateNumberValue(checks);report["language"]=JsonValue.CreateStringValue(OnexStrings.Language);
    report["syntheticCatalogEntries"]=JsonValue.CreateNumberValue(8);report["syntheticSelectActions"]=JsonValue.CreateNumberValue(changes);
    report["realCatalogReadAndHomeTap"]=JsonValue.CreateBooleanValue(true);report["realDetailedDialogAndSettingsNavigation"]=JsonValue.CreateBooleanValue(true);
    report["realOwnerPresetChanged"]=JsonValue.CreateBooleanValue(false);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);
    report["renderTargetCapturesAreNotCompositionFocusProof"]=JsonValue.CreateBooleanValue(true);
   }finally{
    fixtureMenu?.Hide();_onexProfileTileMenu?.Hide();_onexDetailsDialog?.Hide();if(detail!=null)await detail;
    if(fixture!=null)OnexStartupRoot.Children.Remove(fixture);if(original!=null)original.IsChecked=true;
   }
  }
  private async Task VerifyOnexOverlayTilePersistenceAsync(JsonObject report,bool afterRestart)
  {
   int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
   async Task Wait(Func<bool> ready,string why){for(int n=0;n<200;n++){if(ready())return;await Task.Delay(50);}throw new TimeoutException(why);}
   void Invoke(Button button)=>((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
   string proof=Path.Combine(ApplicationData.Current.LocalFolder.Path,"onex-overlay433-selection-proof.json");
   string lease=Path.Combine(ApplicationData.Current.LocalFolder.Path,"onex-overlay433-own-preset.json");
   var originalPage=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);string own=null,originalId=null,originalValues=null,ownName="ONEXQA433-"+Guid.NewGuid().ToString("N");Task detail=null;
   JsonObject Entry(string id)=>_onexOverlayPresets.GetNamedArray("entries").Select(value=>value.GetObject()).SingleOrDefault(entry=>entry.GetNamedString("id")==id);
   Task Change(string operation,string id)=>ChangeOnexOverlayPresetsAsync(new JsonObject{{"operation",JsonValue.CreateStringValue(operation)},{"id",JsonValue.CreateStringValue(id)}});
   try{
    Check(_onexDetailsDialog==null&&!OnexSourceEditorsPending,"Pending owner edits must be kept");
    await ReadOnexOverlayPresetsAsync();await ReadOnexPerformanceProfilesAsync();
    if(afterRestart){
     Check(File.Exists(proof),"Selection persistence proof missing");var expected=JsonObject.Parse(File.ReadAllText(proof));
     Check(_onexOverlayPresets.GetNamedString("activeId")==expected.GetNamedString("id"),"Selected preset changed between restart checks; latest state retained");
     Check(Entry(expected.GetNamedString("id")).GetNamedObject("dc").Stringify()==expected.GetNamedString("values"),"Preset settings changed between checks; latest state retained");
     Check(OnexFrontend.OverlayPresetId==expected.GetNamedString("id"),"Frontend active preset not restored");
     ApplyOnexTileAvailability();Check(qsTileMap["Overlay"].StateText.Text==OnexCatalogEntryName(Entry(expected.GetNamedString("id")),true),"Home tile not synchronized after restart");
     report["actualWidgetRestartReadback"]=JsonValue.CreateBooleanValue(true);report["checks"]=JsonValue.CreateNumberValue(checks);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);File.Delete(proof);return;
    }
    Check(!File.Exists(lease)&&!File.Exists(proof),"Existing own preset verification must be inspected first");
    _releaseOsdNav.IsChecked=true;await OpenOnexOverlaySettingsAsync();await Wait(()=>!_onexOverlayOpening&&!isLoadingOSDConfig&&_onexOverlayConfigReady,"Overlay configuration not ready");
    originalId=_onexOverlayPresets.GetNamedString("activeId");originalValues=Entry(originalId).GetNamedObject("dc").Stringify();
    bool ac=_onexProfilesState.GetNamedBoolean("onAc",false)&&OnexActiveProfile.GetNamedBoolean("split",false);
    bool safe=OnexActiveProfile.GetNamedString("id")=="global"&&(int)OnexActiveProfile.GetNamedObject(ac?"ac":"dc").GetNamedNumber("overlay")==OnexOverlayLevel;
    report["realPresetApplySkippedToPreserveProfileInheritance"]=JsonValue.CreateBooleanValue(!safe);
    if(safe){
     File.WriteAllText(lease,new JsonObject{{"name",JsonValue.CreateStringValue(ownName)},{"previousId",JsonValue.CreateStringValue(originalId)},{"previousValues",JsonValue.CreateStringValue(originalValues)}}.Stringify());
     await ChangeOnexOverlayPresetsAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("create")},{"name",JsonValue.CreateStringValue(ownName)},{"activate",JsonValue.CreateBooleanValue(false)}});
     own=_onexOverlayPresets.GetNamedArray("entries").Select(value=>value.GetObject()).Single(entry=>entry.GetNamedString("name")==ownName).GetNamedString("id");
     var journal=JsonObject.Parse(File.ReadAllText(lease));journal["id"]=JsonValue.CreateStringValue(own);File.WriteAllText(lease,journal.Stringify());
     Check(Entry(own).GetNamedObject("dc").Stringify()==originalValues,"Live settings differ from current preset; refusing a changing apply");
     PerformanceNavItem.IsChecked=true;await Task.Delay(100);
     var menu=BuildOnexHomeProfileMenu(OnexOverlayTileCatalog(),_onexOverlayPresets);_onexProfileTileMenu=menu;menu.ShowAt(qsTileMap["Overlay"].TileButton);await Task.Delay(100);
     var item=menu.Items.OfType<MenuFlyoutItem>().Single(row=>row.Tag as string==own);((IInvokeProvider)new MenuFlyoutItemAutomationPeer(item).GetPattern(PatternInterface.Invoke)).Invoke();
     await Wait(()=>_onexOverlayPresets.GetNamedString("activeId")==own&&!_onexOverlayPresetChanging,"Compact selection did not apply real clone");
     menu.Hide();Check(qsTileMap["Overlay"].StateText.Text==ownName&&OnexFrontend.OverlayPresetId==own,"Real tile/selected state not synchronized");
     detail=ShowOnexOverlayTileDialogAsync(qsTileMap["Overlay"].TileButton);await Wait(()=>_onexProfileTileSelection!=null&&_onexDetailsDialog!=null,"Real detailed selector missing");
     Invoke(_onexProfileTileSelection.Rows[originalId].Choose);
     await Wait(()=>_onexOverlayPresets.GetNamedString("activeId")==originalId&&!_onexOverlayPresetChanging,"Detailed selection did not restore unchanged previous preset");
     Check(Entry(originalId).GetNamedObject("dc").Stringify()==originalValues,"Original preset was overwritten");
     _onexDetailsDialog.Hide();await detail;detail=null;
     await Change("delete",own);Check(Entry(own)==null,"Own clone not removed");own=null;File.Delete(lease);
     report["realRtssPreferencesAppliedThroughUi"]=JsonValue.CreateBooleanValue(true);
     report["ownCloneSelectedByCompactAndPreviousByDetailedUi"]=JsonValue.CreateBooleanValue(true);
     report["ownTemporaryPresetRemoved"]=JsonValue.CreateBooleanValue(true);
    }
    await ReadOnexOverlayPresetsAsync();File.WriteAllText(proof,new JsonObject{{"id",JsonValue.CreateStringValue(_onexOverlayPresets.GetNamedString("activeId"))},{"values",JsonValue.CreateStringValue(Entry(_onexOverlayPresets.GetNamedString("activeId")).GetNamedObject("dc").Stringify())}}.Stringify());
    report["checks"]=JsonValue.CreateNumberValue(checks);report["actualPresetApiRead"]=JsonValue.CreateBooleanValue(true);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);
   }finally{
    _onexProfileTileMenu?.Hide();_onexDetailsDialog?.Hide();if(detail!=null)await detail;
    if(own!=null){
     await ReadOnexOverlayPresetsAsync();var entry=Entry(own);
     if(entry?.GetNamedString("name")==ownName){
      if(_onexOverlayPresets.GetNamedString("activeId")==own&&Entry(originalId)?.GetNamedObject("dc").Stringify()==originalValues)await Change("select",originalId);
      if(_onexOverlayPresets.GetNamedString("activeId")!=own){await Change("delete",own);if(File.Exists(lease))File.Delete(lease);}
     }
    }else if(File.Exists(lease)&&!_onexOverlayPresets.GetNamedArray("entries").Any(value=>value.GetObject().GetNamedString("name")==ownName))File.Delete(lease);
    if(originalPage!=null)originalPage.IsChecked=true;
   }
  }

 }
}
