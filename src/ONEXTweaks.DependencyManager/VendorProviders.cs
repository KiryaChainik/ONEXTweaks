// SPDX-License-Identifier: AGPL-3.0-only
using System;
namespace ONEXTweaks.DependencyManager
{
 internal static class VendorProviders
 {
  internal const string OnexSource="https://onexplayerstore.com/pages/drivers-and-faqs";
  internal const string Onex3Pack="https://drive.google.com/file/d/197i2xoRkOVQjRfURqhbAprVZKd6WC_oV/view?usp=sharing";
  internal static bool SupportsOnex3(string model)
  {
   string value=System.Text.RegularExpressions.Regex.Replace(model??"","[^a-zA-Z0-9]","").ToUpperInvariant();
   return value=="ONEXPLAYER3"||value=="GI78P";
  }
  internal static string IntelSource(bool installed)=>installed?"https://www.intel.com/content/www/us/en/support/detect.html":"https://dsadata.intel.com/installer";
  internal static bool MatchesInstalled(string id,string display)
  {
   if(string.IsNullOrEmpty(display))return false;
   switch(id){
    case "pawnio":return display=="PawnIO";
    case "hidhide":return display=="HidHide";
    case "usbip":return display.StartsWith("USBip ",StringComparison.OrdinalIgnoreCase);
    case "rtss":return display.StartsWith("RivaTuner Statistics Server",StringComparison.OrdinalIgnoreCase);
    case "onexconsole":return display.StartsWith("OneXConsole",StringComparison.OrdinalIgnoreCase);
    case "inteldsa":return display.IndexOf("Intel",StringComparison.OrdinalIgnoreCase)>=0&&display.IndexOf("Driver",StringComparison.OrdinalIgnoreCase)>=0&&display.IndexOf("Support",StringComparison.OrdinalIgnoreCase)>=0&&display.IndexOf("Assistant",StringComparison.OrdinalIgnoreCase)>=0;
    default:return false;
   }
  }
 }
}
