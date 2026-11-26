// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private async Task SetOnexFanAsync(int profile)
        {
            if(_onexFanBusy)return;_onexFanBusy=true;ApplyOnexTileAvailability();
            try{var r=await SendOnexHomeRequestAsync(new ValueSet{["OnexFanSet"]=profile,["OnexFanProfileId"]=OnexAppliedFanId});ApplyOnexHardwareHomeState((string)r["OnexHardwareHomeState"]);if(r.TryGetValue("OnexFanProfilesState",out object fanProfiles))ApplyOnexFanProfilesState((string)fanProfiles);}
            catch(Exception e){ShowOnexDesktopStatus(e.Message);}
            finally{_onexFanBusy=false;ApplyOnexTileAvailability();}
        }
        private bool OnexLightingIsOn
        {
            get{var light=_onexHardwareHome?.GetNamedObject("lighting",null);return light?.GetNamedBoolean("enabled",false)==true&&light.GetNamedArray("zones",new JsonArray()).Any(z=>z.GetObject().GetNamedBoolean("enabled",false));}
        }
        private async Task ToggleOnexLightingAsync()
        {
            var light=OnexSavedLighting;if(light==null)return;
            await SetOnexLightingAsync(CreateOnexLightingToggleRequest(light));
        }
        private JsonObject CreateOnexLightingToggleRequest(JsonObject light)
        {
            bool enable=!OnexLightingIsOn;var zones=JsonArray.Parse(light.GetNamedArray("zones").Stringify());
            if(enable&&!zones.Any(z=>z.GetObject().GetNamedBoolean("enabled",false)))foreach(var z in zones)z.GetObject()["enabled"]=JsonValue.CreateBooleanValue(true);
            return new JsonObject{{"lightingEnabled",JsonValue.CreateBooleanValue(enable)},{"zones",zones}};
        }
        private async Task<bool> SetOnexLightingAsync(JsonObject settings)
        {
            if(_onexLightingBusy)return false;_onexLightingBusy=true;ApplyOnexTileAvailability();
            try{var r=await SendOnexHomeRequestAsync(new ValueSet{["OnexLightingSet"]=settings.Stringify()});ApplyOnexHardwareHomeState((string)r["OnexHardwareHomeState"]);return true;}
            catch(Exception e){ShowOnexDesktopStatus(e.Message);return false;}
            finally{_onexLightingBusy=false;ApplyOnexTileAvailability();}
        }
    }
}
