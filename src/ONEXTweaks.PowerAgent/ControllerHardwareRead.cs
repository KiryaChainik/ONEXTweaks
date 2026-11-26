using System;
using System.Collections.Generic;
using System.IO;

namespace ONEXTweaks.Power
{
    internal sealed partial class Hardware
    {
        // Inspection uses the existing OEM connection. Never initialize defaults or
        // change report mode just to read current controller firmware settings.
        internal object ControllerHardwareRead()
        {
            if(!Convert.ToBoolean(oem.Call("/programhandle/isOpen",null,1200)))
                throw new IOException("Controller connection is not open; no initialization performed");
            var first=oem.Call("/programhandle/ph/getKeyMappingInfo/1",null,1500);
            var second=oem.Call("/programhandle/ph/getKeyMappingInfo/2",null,1500);
            var analog=oem.Call("/programhandle/ph/getTriggerRockerInfo",null,1500);
            if(first==null||second==null||analog==null)throw new IOException("Controller firmware readback is incomplete");
            return new{available=true,source="OEM firmware getter",readOnly=true,readUtc=DateTime.UtcNow.ToString("o"),mapping1=first,mapping2=second,analog=analog};
        }
    }
}
