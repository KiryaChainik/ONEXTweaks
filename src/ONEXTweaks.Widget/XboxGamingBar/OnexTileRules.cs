// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using Windows.Data.Json;
using Windows.Storage;
using XboxGamingBar.QuickSettings;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private static readonly string[] OnexFirstInstallTileIds={"OnexWifi","OnexBluetooth","OnexAirplane","PowerMode","ChargeLimiter","CPUBoost","Profile","OnexFanMode","ReShade","LedToggle","OnexControllerProfile","OnexInputMode","FPSCombined","Overlay","OptiScaler","OnexFse","OnexRotationLock","Fullscreen","ActionTaskManager","ActionExplorer","Keyboard","ActionEndTask","OnexAwake","Power"};
        private static void SetOnexFirstInstallTileDefaults(System.Collections.Generic.IEnumerable<TileDefinition> tiles)
        {
            int extra=OnexFirstInstallTileIds.Length;
            foreach(var tile in tiles){int index=Array.IndexOf(OnexFirstInstallTileIds,tile.Id);tile.IsVisible=index>=0;tile.Order=index>=0?index:extra++;}
        }
        private void ApplyOnexFirstInstallLayout()
        {
            var values=ApplicationData.Current.LocalSettings.Values;
            if(values.ContainsKey("QuickSettingsTiles")||values.Keys.Any(k=>k.StartsWith("QS_")&&(k.EndsWith("_Order")||k.EndsWith("_Visible"))))return;
            SetOnexFirstInstallTileDefaults(qsTileDefinitions);
            foreach(var tile in qsTileDefinitions){values["QS_"+tile.Id+"_Order"]=tile.Order;values["QS_"+tile.Id+"_Visible"]=tile.IsVisible;}
            // Reserve first-install positions for Bluetooth/Airplane before their sequential implementation.
            for(int i=0;i<OnexFirstInstallTileIds.Length;i++){values["QS_"+OnexFirstInstallTileIds[i]+"_Order"]=i;values["QS_"+OnexFirstInstallTileIds[i]+"_Visible"]=true;}
        }
        private int _onexRecoveredKeyboardCount;
        private Windows.UI.Xaml.Media.Brush OnexMutedTileBrush=>OnexSolidBrush(OnexCurrentPalette.Muted);
        private void ApplyOnexTileCaption(TileDefinition tile,bool available)
        {
            if(tile?.StateText==null)return;
            var primary=OnexSolidBrush(_onexAppliedTheme?.TextPrimary??Windows.UI.Colors.White);
            if(tile.NameText!=null)tile.NameText.Foreground=available?primary:OnexMutedTileBrush;
            if(tile.TileButton!=null)tile.TileButton.Foreground=primary;
            bool toggle=tile.Id=="OnexAwake"||tile.Id=="OnexAirplane"||tile.Id=="OnexBluetooth"||tile.Id=="OnexWifi"||tile.Id=="OnexFse"||tile.Id=="OnexRotationLock"||tile.Id=="CPUBoost"||tile.Id=="ChargeLimiter"||tile.Id=="LedToggle"||tile.Id=="Overlay"||tile.Id=="FPSCombined"||tile.Id=="Profile"||tile.Id=="OnexControllerProfile";
            bool active=tile.Id=="OnexAwake"?OnexAwakeOn:tile.Id=="OnexAirplane"?OnexAirplaneOn:tile.Id=="OnexBluetooth"?OnexBluetoothOn:tile.Id=="OnexWifi"?OnexWifiOn:tile.Id=="OnexFse"?OnexFseActive:tile.Id=="OnexRotationLock"?OnexRotationLocked:tile.Id=="CPUBoost"?_onexBoostOn:tile.Id=="ChargeLimiter"?_onexChargeOn:tile.Id=="LedToggle"?OnexLightingIsOn:tile.Id=="Overlay"?OnexOverlayLevel>0:tile.Id=="FPSCombined"?OnexFpsEnabled:tile.Id=="OnexControllerProfile"?!OnexHomeKeyboardMouse:tile.Id=="Profile"&&PerGameProfileToggle.IsOn;
            PaintOnexTileColors(tile, active, available);
            tile.StateText.Foreground = !available || toggle && !active ? OnexMutedTileBrush : primary;
            // Theme arrives asynchronously on cold load. Refresh the actual icon too,
            // rather than retaining a Windows accent or a stale construction-time brush.
            var icon=OnexMainTileIcon(tile);
            if(icon!=null)
                icon.Foreground = available ? OnexSolidBrush(OnexSemanticAccent(OnexTileColorRole(tile.Id))) : OnexMutedTileBrush;
            if(tile.IconElement!=null)tile.IconElement.Opacity=!available?.55:1;
            if(tile.TileButton?.Content is Windows.UI.Xaml.Controls.Grid wrapper)
                foreach(var hint in wrapper.Children.OfType<Windows.UI.Xaml.Controls.FontIcon>().Where(i=>i.Glyph=="\uE712"))hint.Foreground=tileTextBrush??OnexSettingsBrush(160,160,160);
        }
        private static OnexThemeRole OnexTileColorRole(string id)
        {
            switch (id)
            {
                case "OnexWifi": case "OnexBluetooth": return OnexThemeRole.Network;
                case "OnexAirplane": return OnexThemeRole.Warning;
                case "OnexFanMode": return OnexThemeRole.Cooling;
                case "OnexControllerProfile": case "OnexInputMode": case "Keyboard": return OnexThemeRole.Controller;
                case "OnexResolution": case "OnexRefreshRate": case "Fullscreen": case "OnexRotationLock":
                case "ReShade": case "OnexFse": case "FPSCombined": case "Overlay": case "OptiScaler":
                    return OnexThemeRole.Display;
                case "LedToggle": return OnexThemeRole.Lighting;
                case "ActionEndTask": return OnexThemeRole.Danger;
                default: return OnexThemeRole.Power;
            }
        }

        private void PaintOnexTileColors(TileDefinition tile, bool on, bool available)
        {
            if (tile.TileButton == null) return;
            var palette = OnexCurrentPalette;
            var accent = palette.Accent(OnexTileColorRole(tile.Id));
            var background = on && available
                ? OnexApprovedTheme.Blend(palette.Surface, accent, palette.Wash)
                : palette.Surface;
            tile.TileButton.Background = OnexSurface(background);
            tile.TileButton.BorderBrush = OnexSolidBrush(on && available ? accent : palette.Line);
        }

        private static Windows.UI.Xaml.Controls.IconElement OnexMainTileIcon(TileDefinition tile)=>tile?.IconElement as Windows.UI.Xaml.Controls.IconElement??(tile?.IconElement as Windows.UI.Xaml.Controls.Viewbox)?.Child as Windows.UI.Xaml.Controls.IconElement;
        private string OnexTileDisplayName(TileDefinition tile)
        {
            if(OnexFrontend.UsesOwnBackend&&tile.Id=="ChargeLimiter")return OnexStrings.Get("Charge Limit");
            if(OnexFrontend.UsesOwnBackend)
            {
                switch(tile.Id)
                {
                    case "CPUBoost":return OnexStrings.Get("CPU Boost");case "OnexFanMode":return OnexStrings.Get("Fan Profile");
                    case "OnexControllerProfile":return OnexStrings.Get("Controller Profile");case "OnexInputMode":return OnexStrings.Get("Mode");
                    case "Profile":return OnexStrings.Get("Perf Profile");case "ReShade":return OnexStrings.Get("ScrColor Profile");case "PowerMode":return OnexStrings.Get("Power Mode");
                }
            }
            if(!OnexFrontend.UsesOwnBackend||qsColumnCount<5)return qsCustomShortcuts.Contains(tile)?tile.Name:OnexStrings.Get(tile.Name);
            switch(tile.Id)
            {
                case "OnexRefreshRate":return OnexStrings.Get("Refresh");
                case "ActionTaskManager":return OnexStrings.Get("Task Mgr");
                case "ActionEndTask":return OnexStrings.Get("End Task");case "FPSCombined":return OnexStrings.Get("FPS Limit");
            }
            if(qsCustomShortcuts.Contains(tile)&&tile.Name!=TileActionHelper.GetDisplayName(tile.ActionType)&&tile.Name!=TileActionHelper.GetShortName(tile.ActionType))return tile.Name;
            switch(tile.ActionType)
            {
                case TileActionType.SteamBpmSteamMenu:return OnexStrings.Get("Steam Menu");
                case TileActionType.SteamBpmQuickAccess:return OnexStrings.Get("Steam QAM");
                case TileActionType.SteamGameOverlay:return OnexStrings.Get("Steam OSD");
                case TileActionType.SteamMenuContextual:return OnexStrings.Get("Steam Menu");
                default:return qsCustomShortcuts.Contains(tile)?tile.Name:OnexStrings.Get(tile.Name);
            }
        }
        private void FitOnexChargeTitle(TileDefinition tile)
        {
            if(!OnexFrontend.UsesOwnBackend||!OnexHasFullTileTitle(tile.Id)||tile.NameText==null||tile.TileButton==null||tile.TileButton.ActualWidth<=0)return;
            var label=tile.NameText;
            double available=Math.Max(0,tile.TileButton.ActualWidth-tile.TileButton.Padding.Left-tile.TileButton.Padding.Right-tile.TileButton.BorderThickness.Left-tile.TileButton.BorderThickness.Right);
            if(available<=0)return;
            // A centered ContentPresenter can measure this label at its current text width.
            // Use the actual tile's content area so a short label can grow back to the full name.
            label.Width=available;
            var measure=new Windows.UI.Xaml.Controls.TextBlock{FontFamily=label.FontFamily,FontSize=label.FontSize,FontWeight=label.FontWeight,FontStyle=label.FontStyle,FontStretch=label.FontStretch};
            if(tile.Id!="ChargeLimiter")
            {
                label.Text=OnexTileDisplayName(tile);measure.Text=label.Text;measure.FontSize=14;
                measure.Measure(new Windows.Foundation.Size(double.PositiveInfinity,double.PositiveInfinity));
                label.FontSize=measure.DesiredSize.Width<=available?14:Math.Max(10,Math.Floor(14*available/measure.DesiredSize.Width*2)/2);
                return;
            }
            foreach(string name in new[]{"Charge Limit","Crg Limit","Limit"})
            {
                measure.Text=OnexStrings.Get(name);measure.Measure(new Windows.Foundation.Size(double.PositiveInfinity,double.PositiveInfinity));
                if(measure.DesiredSize.Width<=available||name=="Limit"){label.Text=OnexStrings.Get(name);break;}
            }
        }
        private static bool OnexHasFullTileTitle(string id)=>id=="ChargeLimiter"||id=="CPUBoost"||id=="OnexFanMode"||id=="Profile"||id=="ReShade"||id=="PowerMode"||id=="OnexControllerProfile";
        private static string OnexProfileCaption(JsonObject profile)=>profile==null?global::XboxGamingBar.OnexStrings.Get("Unavailable"):profile.GetNamedString("id","")=="global"?global::XboxGamingBar.OnexStrings.Get("Global"):profile.GetNamedString("name","Unavailable");
        private JsonObject OnexEffectiveDisplayProfile
        {
            get
            {
                var active=OnexActiveProfile;if(active==null||active.GetNamedString("id")=="global")return active;
                bool ac=_onexProfilesState.GetNamedBoolean("onAc",false);
                return _onexProfilesState.GetNamedObject("flags").GetNamedBoolean("Display",true)&&OnexDisplayObject(active.GetNamedObject(ac?"ac":"dc"),"display")!=null?active:OnexProfileByName("global");
            }
        }
        private string OnexFanProfileCaption
        {
            get
            {
                var fan=_onexHardwareHome?.GetNamedObject("fan",null);if(fan?.GetNamedBoolean("available",false)!=true)return OnexStrings.Get("Unavailable");
                int? mode=OnexFanReadbackMode(fan);
                var selected=OnexPreset(_onexFanProfiles,OnexSupportedFanId(OnexAppliedFanId));
                var values=OnexPresetValues(_onexFanProfiles,OnexAppliedFanId);
                if(mode!=null&&selected!=null&&values?.GetNamedNumber("mode",0)==mode&&selected.GetNamedString("id")!="global")
                    return OnexPresetName(selected);
                return OnexStrings.Get(mode==3?"Custom":mode==0?"Auto":mode==1?"Manual":"Unavailable");
            }
        }
        private bool CanRemoveOnexTile(TileDefinition tile)=>tile!=null&&tile.Id!="Keyboard"&&QuickSettingsConfig.Instance.GetTile(tile.Id)?.Type==TileType.CustomShortcut;
        private string OnexTileIdentity(TileDefinition tile)
        {
            if(tile.Id=="ReShade"&&!qsCustomShortcuts.Contains(tile))return "builtin:screen-color-profile";
            string action=TileActionIdentity.Action(tile.ActionType,tile.ActionParam,tile.CustomShortcut);
            return action.Length>0?action:qsCustomShortcuts.Contains(tile)?"":TileActionIdentity.BuiltIn(tile.Id);
        }
        private bool RejectDuplicateOnexTileInput(TileActionType action,string parameter=null,string shortcut=null)
        {
            if(!QuickSettingsConfig.Instance.IsDuplicateOnexAction(action,parameter,shortcut))return false;
            string message=OnexStrings.Get("This action already has a tile. Show or move the existing tile.");
            if(CustomShortcutNameBox!=null)CustomShortcutNameBox.Description=message;
            ShowOnexDesktopStatus(message);return true;
        }
        private void NormalizeOnexDuplicateTiles()
        {
            var seen=new System.Collections.Generic.Dictionary<string,TileDefinition>(StringComparer.Ordinal);
            foreach(var tile in qsTileDefinitions.Where(t=>!qsCustomShortcuts.Contains(t)&&!ShouldSkipTile(t)))
            {string key=OnexTileIdentity(tile);if(key.Length>0&&!seen.ContainsKey(key))seen.Add(key,tile);}
            var duplicates=new System.Collections.Generic.List<System.Tuple<TileDefinition,TileDefinition>>();
            foreach(var tile in qsCustomShortcuts.OrderByDescending(t=>t.IsVisible).ThenBy(t=>t.Order))
            {
                string key=OnexTileIdentity(tile);if(key.Length==0)continue;
                if(seen.TryGetValue(key,out var keeper))duplicates.Add(System.Tuple.Create(tile,keeper));else seen.Add(key,tile);
            }
            if(duplicates.Count==0)return;
            if(!_onexVerifying)
            {
                string path=System.IO.Path.Combine(ApplicationData.Current.LocalFolder.Path,"onex-tiles-before-dedup.json");
                if(!System.IO.File.Exists(path))
                {
                    var backup=new Windows.Data.Json.JsonObject();
                    foreach(var pair in ApplicationData.Current.LocalSettings.Values.Where(x=>x.Key.StartsWith("QS_")||x.Key=="QuickSettingsTiles").ToArray())backup[pair.Key]=pair.Value is bool b?Windows.Data.Json.JsonValue.CreateBooleanValue(b):pair.Value is int n?Windows.Data.Json.JsonValue.CreateNumberValue(n):Windows.Data.Json.JsonValue.CreateStringValue(pair.Value.ToString());
                    System.IO.File.WriteAllText(path,backup.Stringify());
                }
            }
            var config=QuickSettingsConfig.Instance;var values=ApplicationData.Current.LocalSettings.Values;
            foreach(var pair in duplicates)
            {
                var tile=pair.Item1;var keeper=pair.Item2;
                if(!keeper.IsVisible&&tile.IsVisible){keeper.IsVisible=true;keeper.Order=tile.Order;}
                if(string.IsNullOrEmpty(keeper.ControllerHotkey))keeper.ControllerHotkey=tile.ControllerHotkey;
                var saved=config.Tiles.LastOrDefault(t=>t.Id==tile.Id&&t.Type==TileType.CustomShortcut&&TileActionIdentity.Action(t.ActionType,t.ActionParam,t.CustomShortcut)==OnexTileIdentity(tile));
                if(saved!=null)config.Tiles.Remove(saved);
                qsTileDefinitions.Remove(tile);qsCustomShortcuts.Remove(tile);
                if(!qsTileDefinitions.Any(t=>t.Id==tile.Id))foreach(string suffix in new[]{"_Order","_Visible","_Hotkey"})values.Remove("QS_"+tile.Id+suffix);
            }
            qsTileMap.Clear();foreach(var tile in qsTileDefinitions)qsTileMap[tile.Id]=tile;
            config.Save();SaveQuickSettingsConfig();Logger.Info("Merged "+duplicates.Count+" repeated tile actions");
        }
        private void RestoreCanonicalOnexKeyboard()
        {
            var canonical=qsTileDefinitions.FirstOrDefault(t=>t.Id=="Keyboard"&&!qsCustomShortcuts.Contains(t));
            if(canonical==null){AddTileDefinition("Keyboard","Keyboard","\uE765",isTrigger:true,order:qsTileDefinitions.Count==0?0:qsTileDefinitions.Max(t=>t.Order)+1);canonical=qsTileDefinitions.Last();}
            var replacements=qsCustomShortcuts.Where(t=>t.ActionType==TileActionType.ShowKeyboard||t.Id=="Keyboard").OrderByDescending(t=>t.IsVisible).ThenBy(t=>t.Order).ToArray();
            qsTileMap["Keyboard"]=canonical;
            if(replacements.Length==0)return;
            _onexRecoveredKeyboardCount+=replacements.Length;
            var preferred=replacements[0];canonical.Order=preferred.Order;canonical.IsVisible=preferred.IsVisible;
            if(!string.IsNullOrEmpty(preferred.ControllerHotkey))canonical.ControllerHotkey=preferred.ControllerHotkey;
            var values=ApplicationData.Current.LocalSettings.Values;
            foreach(var replacement in replacements)
            {
                qsTileDefinitions.Remove(replacement);qsCustomShortcuts.Remove(replacement);
                if(replacement.Id!="Keyboard")
                {
                    qsTileMap.Remove(replacement.Id);
                    foreach(string suffix in new[]{"_Order","_Visible","_Hotkey"})values.Remove("QS_"+replacement.Id+suffix);
                }
            }
            var config=QuickSettingsConfig.Instance;
            foreach(var saved in config.Tiles.Where(t=>t.Type==TileType.CustomShortcut&&(t.ActionType==TileActionType.ShowKeyboard||t.Id=="Keyboard")).ToArray())config.Tiles.Remove(saved);
            config.Save();SaveQuickSettingsConfig();
            Logger.Info("Restored built-in Keyboard from custom replacement, retaining position, visibility and binding");
        }
    }
}
