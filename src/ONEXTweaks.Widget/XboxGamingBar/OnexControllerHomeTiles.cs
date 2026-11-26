// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using XboxGamingBar.QuickSettings;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private bool _onexControllerHomeBusy;
  private bool OnexHomeKeyboardMouse=>ControllerText(_kbmState,"mode",ControllerText(_onexControllerState,"inputMode","gamepad"))=="keyboardMouse";
  private bool OnexControllerHomeReady=>App.IsConnected&&_onexControllerState?.ContainsKey("entries")==true&&!_onexControllerHomeBusy&&!_onexControllerBusy&&!_kbmBusy;
  private async Task<JsonObject> ReadOnexControllerHomeAsync()
  {await ReadOnexControllerAsync();await ReadOnexKeyboardMouseAsync();return OnexControllerHomeCatalogState();}
  private JsonObject OnexControllerHomeCatalogState()
  {
   if(_onexControllerState==null)return null;var value=JsonObject.Parse(_onexControllerState.Stringify());
   // The remembered gamepad profile remains selectable when the active mode is KBM.
   if(OnexHomeKeyboardMouse){value["activeId"]=JsonValue.CreateStringValue("");value["appliedId"]=JsonValue.CreateStringValue("");}
   return value;
  }
  private static JsonObject OnexControllerHomePolicy(JsonObject entry)=>entry.GetNamedObject("effectiveDc",entry.GetNamedObject("dc",null));
  private string OnexControllerHomeBrief(JsonObject entry)
  {
   var policy=OnexControllerHomePolicy(entry);if(policy==null)return "—";
   int count=policy.GetNamedObject("bindings",null)?.Count(pair=>ControllerText(pair.Value.GetObject(),"kind","default")!="default")??0;
   return (policy.GetNamedBoolean("useHardwareController",false)?OnexStrings.Get("Hardware Controller"):OnexControllerTargetName(ControllerText(policy,"virtualTarget","Xbox360")))+OnexStrings.Format(" · {0} remaps",count);
  }
  private OnexHomeProfileCatalog OnexControllerHomeCatalog()=>new OnexHomeProfileCatalog
  {
   Scope="Controller",Title=OnexStrings.Get("Controller Profiles"),Settings="Go to Controller Profile Settings",
   Read=ReadOnexControllerHomeAsync,State=OnexControllerHomeCatalogState,Change=SelectOnexControllerHomeProfileAsync,
   Describe=DescribeOnexControllerPolicy,Brief=OnexControllerHomeBrief,Rank=OnexHomeProfileRank,
   Navigate=OpenOnexControllerProfileSettings
  };
  private Task ShowOnexControllerHomeMenuAsync(Button anchor)=>ShowOnexHomeProfileMenuAsync(anchor,OnexControllerHomeCatalog());
  private Task ShowOnexControllerHomeDialogAsync(Button anchor)=>ShowOnexHomeProfileDialogAsync(anchor,OnexControllerHomeCatalog());
  private async Task SetOnexHomeInputModeCoreAsync(string mode)
  {
   var reply=await SendOnexHomeRequestAsync(new ValueSet{{"OnexKeyboardMouseSet",new JsonObject{{"operation",JsonValue.CreateStringValue("mode")},{"mode",JsonValue.CreateStringValue(mode)}}.Stringify()}});
   if(reply.TryGetValue("OnexKeyboardMouseState",out var mouse))ApplyOnexKeyboardMouseState((string)mouse);
   if(reply.TryGetValue("OnexControllerState",out var controller))ApplyOnexControllerState((string)controller);
   if(OnexHomeKeyboardMouse!=(mode=="keyboardMouse"))throw new InvalidOperationException(OnexStrings.Get("Input mode did not change. Open Controller Settings to check the connection."));
  }
  private async Task SelectOnexControllerHomeProfileAsync(JsonObject change)
  {
   if(_onexControllerHomeBusy)return;
   if(change.GetNamedString("operation","")!="select")throw new InvalidOperationException("Home controller chooser only selects profiles");
   if(OnexSourceEditorsPending||_onexControllerPending!=null||_kbmTuning.Count>0)throw new InvalidOperationException(OnexStrings.Get("Confirm the current adjustment before switching input mode or profile."));
   _onexControllerHomeBusy=true;RefreshOnexControllerHomeTiles();
   try
   {
    // Select first: an invalid/deleted profile must not turn off the owner's KBM mode.
    await ChangeOnexControllerAsync(change);
    await SetOnexHomeInputModeCoreAsync("gamepad");
   }
   finally{_onexControllerHomeBusy=false;RefreshOnexControllerHomeTiles();}
  }
  private async Task ToggleOnexHomeInputModeAsync()
  {
   if(_onexControllerHomeBusy)return;
   if(OnexSourceEditorsPending||_onexControllerPending!=null||_kbmTuning.Count>0){ShowOnexDesktopStatus(OnexStrings.Get("Confirm the current adjustment before switching input mode or profile."));return;}
   _onexControllerHomeBusy=true;RefreshOnexControllerHomeTiles();
   try{await ReadOnexKeyboardMouseAsync();await SetOnexHomeInputModeCoreAsync(OnexHomeKeyboardMouse?"gamepad":"keyboardMouse");}
   catch(Exception error){try{await ReadOnexControllerHomeAsync();}catch{}ShowOnexDesktopStatus(error.Message);}
   finally{_onexControllerHomeBusy=false;RefreshOnexControllerHomeTiles();}
  }
  private void OpenOnexControllerProfileSettings()
  {
   LegionNavItem.IsChecked=true;
   _onexControllerPickerCard?.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false,VerticalAlignmentRatio=0});
  }
  private void OpenOnexHomeInputModeSettings()
  {
   CloseOnexMenus();
   if(!OnexHomeKeyboardMouse)
   {
    OpenOnexControllerProfileSettings();
    return;
   }
   EnsureOnexKeyboardMouseFrontend();
   _onexKeyboardMouseNav.IsChecked=true;
  }
  private void RefreshOnexControllerHomeTiles()
  {
   foreach(string id in new[]{"OnexControllerProfile","OnexInputMode"})if(qsTileMap.TryGetValue(id,out var tile))
   {SetTileActionable(tile,OnexControllerHomeReady);ApplyOnexControllerHomeTile(tile);ApplyOnexTileCaption(tile,OnexControllerHomeReady);if(tile.StateText!=null)UpdateTileScrollAnimation(tile);}
  }
  private void ApplyOnexControllerHomeTile(TileDefinition tile)
  {
   if(tile.StateText==null)return;
   if(tile.Id=="OnexInputMode")
   {
    tile.StateText.Text=_onexControllerHomeBusy?OnexStrings.Get("Applying…"):OnexStrings.Get(OnexHomeKeyboardMouse?"Keyboard & mouse":"Gamepad");
    UpdateOnexHomeInputModeIcon(tile);
    ToolTipService.SetToolTip(tile.TileButton,OnexStrings.Get("Tap to switch Gamepad / Keyboard & mouse. Hold or press X for current mode settings."));return;
   }
   var entry=_onexControllerState?.GetNamedArray("entries",null)?.Select(value=>value.GetObject()).FirstOrDefault(value=>ControllerText(value,"id")==ControllerText(_onexControllerState,"appliedId"));
   tile.StateText.Text=_onexControllerHomeBusy?OnexStrings.Get("Applying…"):OnexHomeKeyboardMouse?OnexStrings.Get("Off"):entry==null?OnexStrings.Get("Unavailable"):OnexCatalogEntryName(entry,false);
   ToolTipService.SetToolTip(tile.TileButton,(entry==null?"":OnexCatalogEntryName(entry,false)+" · "+OnexControllerHomeBrief(entry)+"\n")+OnexStrings.Get("Tap to choose a profile. Hold for details · Gamepad X / Shift+F10"));
  }
 }
}
