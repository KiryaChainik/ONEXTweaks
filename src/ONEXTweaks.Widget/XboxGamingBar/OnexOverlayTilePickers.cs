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
  private static bool OnexCatalogBuiltIn(JsonObject entry)
  {
   string id=entry.GetNamedString("id","");
   return entry.GetNamedBoolean("builtIn",id=="global"||id.StartsWith("firmware-",StringComparison.Ordinal));
  }
  private static string OnexCatalogEntryName(JsonObject entry,bool presets)
  {
   string id=entry.GetNamedString("id",""),name=entry.GetNamedString("name","");
   if(id=="global")return OnexStrings.Get(presets?"Default Preset":"Global Profile");
   // RTSS standard preset names and all owner-created names are identities.
   return !presets&&id.StartsWith("firmware-",StringComparison.Ordinal)?OnexStrings.Get(name):name;
  }
  private static int OnexOverlayPresetRank(JsonObject entry)=>OnexCatalogBuiltIn(entry)?0:1;
  private string OnexOverlayTileBrief(JsonObject entry)
  {
   var values=entry.GetNamedObject("dc",null);if(values==null)return "—";
   var parts=DescribeOnexOverlayPreset(values).Split('\n')[0].Split(new[]{" · "},StringSplitOptions.None);
   return string.Join(" · ",parts.Take(2));
  }
  private OnexHomeProfileCatalog OnexOverlayTileCatalog()=>new OnexHomeProfileCatalog
  {
   Scope="OverlayPreset",Presets=true,Title=OnexStrings.Get("Overlay Presets"),Settings="Go to Overlay Settings",
   Read=ReadOnexOverlayPresetsAsync,State=()=>_onexOverlayPresets,Change=ChangeOnexOverlayPresetsAsync,
   Describe=DescribeOnexOverlayPreset,Brief=OnexOverlayTileBrief,Rank=OnexOverlayPresetRank,
   Navigate=()=>{_releaseOsdNav.IsChecked=true;_onexOverlayPresetHeader?.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false,VerticalAlignmentRatio=0});}
  };
  private Task ShowOnexOverlayTileMenuAsync(Button anchor)=>ShowOnexHomeProfileMenuAsync(anchor,OnexOverlayTileCatalog());
  private Task ShowOnexOverlayTileDialogAsync(Button anchor)=>ShowOnexHomeProfileDialogAsync(anchor,OnexOverlayTileCatalog());
 }
}
