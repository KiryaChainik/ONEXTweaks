// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private MenuFlyout _onexProfileTileMenu;
  private OnexSavedCatalogSection _onexProfileTileSelection;
  private bool _onexProfileTileOpening;
  private static int OnexHomeProfileRank(JsonObject entry)=>entry.GetNamedString("id")=="global"?0:entry.GetNamedString("id").StartsWith("firmware-",StringComparison.Ordinal)?1:string.IsNullOrEmpty(entry.GetNamedString("processPath",entry.GetNamedString("path","")))?2:3;
  private JsonObject OnexPerformanceTileState()
  {
   if(_onexProfilesState==null)return null;var state=JsonObject.Parse(_onexProfilesState.Stringify());var entries=new JsonArray();foreach(var entry in OnexProfiles)entries.Add(OnexEffectiveSavedEntry(entry));state["entries"]=entries;return state;
  }
  private string OnexPerformanceTileBrief(JsonObject entry)
  {
   bool ac=_onexProfilesState.GetNamedBoolean("onAc",false);var values=entry.GetNamedObject(ac?"ac":"dc");var fps=values.GetNamedObject("fps");return values.GetNamedNumber("pl1")+" W · Boost "+OnexStrings.Get(values.GetNamedNumber("boost")>0?"On":"Off")+" · FPS "+(fps.GetNamedBoolean("enabled")?fps.GetNamedNumber("value").ToString():OnexStrings.Get("Off"));
  }
  private sealed class OnexHomeProfileCatalog
  {
   internal string Scope,Title,Settings;internal bool Presets;
   internal Func<Task<JsonObject>> Read;internal Func<JsonObject> State;internal Func<JsonObject,Task> Change;
   internal Func<JsonObject,string> Describe,Brief;internal Func<JsonObject,int> Rank;internal Action Navigate;
  }
  private OnexHomeProfileCatalog OnexPerformanceTileCatalog()=>new OnexHomeProfileCatalog{Scope="Performance",Title=global::XboxGamingBar.OnexStrings.Get("Performance Profiles"),Settings="Go to Performance Profile Settings",Read=ReadOnexPerformanceProfilesAsync,State=OnexPerformanceTileState,Change=ChangeOnexPerformanceProfileAsync,Describe=DescribeOnexPerformanceProfile,Brief=OnexPerformanceTileBrief,Rank=OnexHomeProfileRank,Navigate=()=>{_releasePowerNav.IsChecked=true;_onexPerformanceProfilePickerCard?.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false,VerticalAlignmentRatio=0});}};
  private string OnexFanTileBrief(JsonObject entry)
  {
   var values=OnexPresetValues(_onexFanProfiles,entry.GetNamedString("id"));int mode=(int)(values?.GetNamedNumber("mode",0)??0);if(mode!=3)return OnexStrings.Get("Auto");
   var curve=values.GetNamedObject("curve",null);return curve==null?OnexStrings.Get("Custom curve"):OnexStrings.Format("Custom · {0}%",string.Join("/",OnexFanPoints(curve)));
  }
  private JsonObject OnexFanTileState()
  {
   if(_onexFanProfiles==null)return null;
   var state=JsonObject.Parse(_onexFanProfiles.Stringify());
   var entries=new JsonArray();
   foreach(var item in state.GetNamedArray("entries"))
   {
    string id=item.GetObject().GetNamedString("id");
    if(id=="firmware-0"||!id.StartsWith("firmware-",StringComparison.Ordinal))entries.Add(item);
   }
   state["entries"]=entries;
   var applied=OnexPresetValues(_onexFanProfiles,state.GetNamedString("appliedId",state.GetNamedString("activeId")));
   var hardware=_onexHardwareHome?.GetNamedObject("fan",null);
   if(applied!=null&&hardware?.GetNamedBoolean("available",false)==true&&
      applied.GetNamedNumber("mode",0)!=hardware.GetNamedNumber("profile",0))
    state["appliedId"]=JsonValue.CreateStringValue("");
   return state;
  }
  private static int OnexFanTileRank(string id)=>id=="firmware-0"?-1:id=="global"?0:1;
  private OnexHomeProfileCatalog OnexFanTileCatalog()=>new OnexHomeProfileCatalog{Scope="Fan",Title=global::XboxGamingBar.OnexStrings.Get("Fan Profiles"),Settings="Go to Fan Profile Settings",Read=ReadOnexFanProfilesAsync,State=OnexFanTileState,Change=ChangeOnexFanProfilesAsync,Describe=DescribeOnexFanProfile,Brief=OnexFanTileBrief,Rank=entry=>OnexFanTileRank(entry.GetNamedString("id")),Navigate=()=>{FanNavItem.IsChecked=true;_onexFanProfilePickerCard?.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false,VerticalAlignmentRatio=0});}};
  private string OnexScreenTileBrief(JsonObject entry)
  {
   var values=OnexPresetValues(_onexScreenProfiles,entry.GetNamedString("id"));var hdr=OnexDisplayObject(values,"hdr");var colors=OnexDisplayObject(values,"colors");return "HDR "+OnexStrings.Get(hdr?.GetNamedBoolean("enabled",false)==true?"On":"Off")+(colors==null?"":" · S"+colors.GetNamedNumber("saturation")+" C"+colors.GetNamedNumber("contrast")+" B"+colors.GetNamedNumber("brightness"));
  }
  private OnexHomeProfileCatalog OnexScreenTileCatalog()=>new OnexHomeProfileCatalog{Scope="ScreenColor",Title=global::XboxGamingBar.OnexStrings.Get("Screen Color Profiles"),Settings="Go to Screen Color Settings",Read=ReadOnexScreenProfilesAsync,State=()=>_onexScreenProfiles,Change=MutateOnexScreenProfilesAsync,Describe=DescribeOnexScreenProfile,Brief=OnexScreenTileBrief,Rank=OnexHomeProfileRank,Navigate=()=>{DisplayNavItem.IsChecked=true;_onexScreenProfilePickerCard?.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false,VerticalAlignmentRatio=0});}};
  private Task ShowOnexScreenTileMenuAsync(Button anchor)=>ShowOnexHomeProfileMenuAsync(anchor,OnexScreenTileCatalog());
  private Task ShowOnexScreenTileDialogAsync(Button anchor)=>ShowOnexHomeProfileDialogAsync(anchor,OnexScreenTileCatalog());
  private Task ShowOnexPerformanceTileMenuAsync(Button anchor)=>ShowOnexHomeProfileMenuAsync(anchor,OnexPerformanceTileCatalog());
  private Task ShowOnexPerformanceTileDialogAsync(Button anchor)=>ShowOnexHomeProfileDialogAsync(anchor,OnexPerformanceTileCatalog());
  private Task ShowOnexFanTileMenuAsync(Button anchor,bool temporary=false)=>ShowOnexHomeProfileMenuAsync(anchor,OnexFanTileCatalog(),temporary);
  private Task ShowOnexFanTileDialogAsync(Button anchor)=>ShowOnexHomeProfileDialogAsync(anchor,OnexFanTileCatalog());
  private async Task ShowOnexHomeProfileMenuAsync(Button anchor,OnexHomeProfileCatalog catalog,bool temporary=false)
  {
   if(_onexProfileTileOpening||_onexDetailsDialog!=null)return;_onexProfileTileOpening=true;
   try
   {
    await catalog.Read();var menu=BuildOnexHomeProfileMenu(catalog,catalog.State(),temporary);_onexProfileTileMenu=menu;
    if(widget==null||widget.Visible)menu.ShowAt(anchor);
   }
   catch(Exception error){ShowOnexDesktopStatus(error.Message);}
   finally{_onexProfileTileOpening=false;}
  }
  private MenuFlyout BuildOnexHomeProfileMenu(OnexHomeProfileCatalog catalog,JsonObject state,bool temporary=false)
  {
    var menu=CreateOnexMenu(FlyoutPlacementMode.Bottom);int previous=-1;
    menu.MenuFlyoutPresenterStyle.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty,Math.Max(280,ActualWidth-24)));
    foreach(var entry in state.GetNamedArray("entries").Select(value=>value.GetObject()).OrderBy(catalog.Rank).ThenBy(value=>OnexCatalogEntryName(value,catalog.Presets),StringComparer.CurrentCultureIgnoreCase))
    {
     int rank=catalog.Rank(entry);if(previous>=0&&rank!=previous)menu.Items.Add(new MenuFlyoutSeparator());previous=rank;string id=entry.GetNamedString("id"),name=OnexCatalogEntryName(entry,catalog.Presets);
     var item=new MenuFlyoutItem{Tag=id,Text=catalog.Scope=="Fan"&&id=="firmware-0"?name:name+" · "+catalog.Brief(entry)};if(id==state.GetNamedString("appliedId",state.GetNamedString("activeId"))){item.Icon=new SymbolIcon(Symbol.Accept);item.IsEnabled=false;}
     ToolTipService.SetToolTip(item,item.Text);item.Click+=async(s,e)=>{try{var change=new JsonObject{{"operation",JsonValue.CreateStringValue("select")},{"id",JsonValue.CreateStringValue(id)}};if(temporary)change["temporary"]=JsonValue.CreateBooleanValue(true);await catalog.Change(change);}catch(Exception error){await ShowOnexProfileOperationErrorAsync(error.Message);}};menu.Items.Add(item);
    }
    return menu;
  }
  private OnexSavedCatalogSection BuildOnexHomeProfileSelection(OnexHomeProfileCatalog catalog)=>new OnexSavedCatalogSection(this,catalog.Scope+"Tile",catalog.State,async change=>{await catalog.Change(change);_onexProfileTileSelection?.Apply(true);},catalog.Describe,true);
  private async Task ShowOnexHomeProfileDialogAsync(Button anchor,OnexHomeProfileCatalog catalog)
  {
   if(_onexProfileTileOpening||_onexDetailsDialog!=null)return;_onexProfileTileOpening=true;bool navigate=false;
   try
   {
    CloseOnexMenus();await catalog.Read();var section=BuildOnexHomeProfileSelection(catalog);_onexProfileTileSelection=section;
    section.Header.Visibility=Visibility.Collapsed;section.Body.Visibility=Visibility.Visible;section.Apply(true);section.Accent(OnexDialogAccent);
    var dialog=CreateOnexSettingsDialog(catalog.Title,catalog.Presets?OnexStrings.Get("Choose a preset to apply it immediately."):global::XboxGamingBar.OnexStrings.Get("Выбери профиль — он применяется сразу. Здесь названия и профили не изменяются."),section.Card);dialog.PrimaryButtonText=OnexStrings.Get(catalog.Settings);dialog.CloseButtonText=global::XboxGamingBar.OnexStrings.Get("Close");dialog.DefaultButton=ContentDialogButton.Close;_onexDetailsDialog=dialog;
    navigate=await dialog.ShowAsync()==ContentDialogResult.Primary;
   }
   catch(Exception error){ShowOnexDesktopStatus(error.Message);}
   finally{_onexDetailsDialog=null;_onexProfileTileSelection=null;_onexProfileTileOpening=false;}
   if(navigate)catalog.Navigate();else anchor?.Focus(FocusState.Programmatic);
  }
 }
}
