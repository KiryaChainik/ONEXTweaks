// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private string OnexAppliedFanId=>_onexFanProfiles?.GetNamedString("appliedId",_onexFanProfiles?.GetNamedString("activeId","global")??"global")??"global";
        private const string OnexAutoFanId = "firmware-0";
        private static string OnexSupportedFanId(string id) => id == "firmware-1" || id == "firmware-2" ? OnexAutoFanId : id;
        private JsonObject[] OnexOrderedFanEntries()
        {
            var entries=_onexFanProfiles?.GetNamedArray("entries",null)?.Select(value=>value.GetObject()).ToArray()??new JsonObject[0];
            return entries.Where(entry=>OnexSupportedFanId(entry.GetNamedString("id"))==entry.GetNamedString("id")).OrderBy(entry=>entry.GetNamedString("id").StartsWith("firmware-",StringComparison.Ordinal)?0:1).ThenBy(entry=>entry.GetNamedString("id").StartsWith("firmware-",StringComparison.Ordinal)?entry.GetNamedString("id"):entry.GetNamedString("id")=="global"?"":entry.GetNamedString("name"),StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        private async Task OpenOnexFanSettingsAsync(string id)
        {
            try
            {
                if(string.IsNullOrEmpty(id))id=OnexAppliedFanId;
                await ChangeOnexFanProfilesAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("edit")},{"id",JsonValue.CreateStringValue(id)}});
                FanNavItem.IsChecked=true;FanScrollViewer.ChangeView(null,0,null,true);_onexFanProfilePicker.Choose.Focus(Windows.UI.Xaml.FocusState.Keyboard);
            }
            catch(Exception error){ShowOnexDesktopStatus(error.Message);}
        }
    }
}
