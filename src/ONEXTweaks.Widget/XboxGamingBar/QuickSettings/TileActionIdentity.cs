// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
namespace XboxGamingBar.QuickSettings
{
    public static class TileActionIdentity
    {
        private static readonly Dictionary<string,string> Aliases=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){{"CONTROL","CTRL"},{"WINDOWS","WIN"},{"LWIN","WIN"},{"ESCAPE","ESC"},{"RETURN","ENTER"},{"SPACEBAR","SPACE"},{"BACK","BACKSPACE"},{"DEL","DELETE"},{"INS","INSERT"},{"PGUP","PAGEUP"},{"PGDN","PAGEDOWN"}};
        public static string Shortcut(string shortcut)
        {
            if(string.IsNullOrWhiteSpace(shortcut))return "";
            var keys=shortcut.Split(new[]{'+'},StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Trim().ToUpperInvariant()).Where(x=>x.Length>0).Select(x=>Aliases.TryGetValue(x,out var alias)?alias:x).Distinct(StringComparer.Ordinal).OrderBy(x=>x=="CTRL"?0:x=="ALT"?1:x=="SHIFT"?2:x=="WIN"||x=="RWIN"?3:4).ThenBy(x=>x,StringComparer.Ordinal);
            return "shortcut:"+string.Join("+",keys);
        }
        public static string Program(string value)
        {
            string text=(value??"").Trim().Trim('"').Replace('/','\\');
            bool drive=text.Length>2&&char.IsLetter(text[0])&&text[1]==':';bool unc=text.StartsWith("\\\\",StringComparison.Ordinal);
            if(!drive&&!unc)return text.ToUpperInvariant();
            string prefix=drive?text.Substring(0,2):"\\\\";var parts=new List<string>();int minimum=unc?2:0;
            foreach(string part in text.Substring(drive?2:2).Split(new[]{'\\'},StringSplitOptions.RemoveEmptyEntries))
            {if(part==".")continue;if(part==".."&&parts.Count>minimum){parts.RemoveAt(parts.Count-1);continue;}parts.Add(part);}
            return (prefix+(drive?"\\":"")+string.Join("\\",parts)).ToUpperInvariant();
        }
        public static string Url(string value)
        {
            string text=(value??"").Trim();
            if(Uri.TryCreate(text,UriKind.Absolute,out var uri))return uri.AbsoluteUri;
            return text;
        }
        public static string Action(TileActionType action,string parameter=null,string shortcut=null)
        {
            if(!string.IsNullOrWhiteSpace(shortcut))return Shortcut(shortcut);
            switch(action)
            {
                case TileActionType.None:case TileActionType.KeyboardShortcut:return "";
                case TileActionType.TDPStepUp:case TileActionType.TDPIncrBy1W:return "action:tdp+1";
                case TileActionType.TDPStepDown:case TileActionType.TDPDecrBy1W:return "action:tdp-1";
                case TileActionType.GoToDesktop:return Shortcut("Win+D");
                case TileActionType.ToggleFse:return Shortcut("Win+F11");
                case TileActionType.SteamBpmSteamMenu:return Shortcut("Ctrl+1");
                case TileActionType.SteamBpmQuickAccess:return Shortcut("Ctrl+2");
                case TileActionType.SteamGameOverlay:return Shortcut("Shift+Tab");
                case TileActionType.MediaNextTrack:return Shortcut("MediaNext");
                case TileActionType.MediaPrevTrack:return Shortcut("MediaPrev");
                case TileActionType.MediaPlayPause:return Shortcut("MediaPlayPause");
                case TileActionType.LaunchUserProgram:return "program:"+Program(parameter);
                case TileActionType.OpenUserWebsite:return "url:"+Url(parameter);
                case TileActionType.OpenGoogle:return "url:"+Url("https://www.google.com");
                case TileActionType.OpenYouTube:return "url:"+Url("https://www.youtube.com");
                case TileActionType.OpenRetroAchievements:return "url:"+Url("https://retroachievements.org");
                case TileActionType.OpenExophase:return "url:"+Url("https://www.exophase.com");
                default:return "action:"+(int)action+":"+(parameter??"").Trim();
            }
        }
        public static string BuiltIn(string id)
        {
            switch(id)
            {
                case "OnexBluetooth":return Action(TileActionType.ToggleBluetooth);
                case "OnexAirplane":return Action(TileActionType.ToggleAirplane);
                case "OnexWifi":return Action(TileActionType.ToggleWifi);
                case "Keyboard":return Action(TileActionType.ShowKeyboard);
                case "Overlay":return Action(TileActionType.CycleOverlayMode);
                case "FPSCombined":return Action(TileActionType.CycleLimiterMode);
                case "OptiScaler":return Shortcut("Insert");
                case "ReShade":return Shortcut("Home");
                case "ActionTaskManager":return Shortcut("Ctrl+Shift+Esc");
                case "ActionExplorer":return Shortcut("Win+E");
                default:return "builtin:"+id;
            }
        }
    }
}
