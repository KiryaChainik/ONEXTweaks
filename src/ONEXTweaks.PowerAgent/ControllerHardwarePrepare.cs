using System;
using System.Collections.Generic;
using System.IO;

namespace ONEXTweaks.Power
{
    internal sealed partial class Hardware
    {
        private Dictionary<string,object> controllerBefore1,controllerBefore2;
        private bool controllerOwnsMapping;
        private static readonly string[] ControllerKeys={"01","02","03","04","05","06","07","08","09","0a","0b","0c","0d","0e","0f","10","22","23","24","25"};
        private static Dictionary<string,object> ControllerMapping(object value)
        {
            var input=value as Dictionary<string,object>;
            if(input==null||input.Count!=ControllerKeys.Length)throw new IOException("A complete current OEM preset is required; partial firmware readback cannot be written");
            var result=new Dictionary<string,object>();
            foreach(string id in ControllerKeys)
            {
                object rowValue;if(!input.TryGetValue(id,out rowValue))throw new IOException("OEM preset key is missing");
                var row=rowValue as Dictionary<string,object>;
                if(row==null)throw new IOException("OEM preset key is invalid");
                var copy=new Dictionary<string,object>();
                foreach(string field in new[]{"keyCode","funcCode","value1","value2","value3","value4"})
                {
                    object item;row.TryGetValue(field,out item);string text=item as string;
                    if(item!=null&&(text==null||text.Length!=2||!IsControllerHex(text)))throw new IOException("Unsupported OEM key encoding");
                    copy[field]=text;
                }
                if((string)copy["keyCode"]!=id)throw new IOException("OEM preset key identity differs");
                string function=copy["funcCode"] as string;
                if(function!="01"&&function!="02"&&function!="03"&&function!="04"&&function!="05")throw new IOException("Unsupported OEM function");
                result[id]=copy;
            }
            return result;
        }
        private static bool IsControllerHex(string value)
        {foreach(char c in value)if(!((c>='0'&&c<='9')||(c>='a'&&c<='f')||(c>='A'&&c<='F')))return false;return true;}
        private static Dictionary<string,object> ControllerKey(string id,string key)
        {return new Dictionary<string,object>{{"keyCode",id},{"funcCode","02"},{"value1","01"},{"value2",key},{"value3","00"},{"value4","00"}};}
        private void WriteControllerMapping(Dictionary<string,object> mapping,int layer)
        {oem.Call("/programhandle/ph/setKeyMappingInfo",new{keyMappingInfo=Program.Json.Serialize(mapping),mode=layer.ToString()},1500);}
        internal object ControllerHardwarePrepare(Dictionary<string,object> request)
        {
            object ready,one,two;
            if(!request.TryGetValue("routingReady",out ready)||!(ready is bool)||!(bool)ready)throw new IOException("The six-button listener and shortcut filter must be ready first");
            if(!request.TryGetValue("mapping1",out one)||!request.TryGetValue("mapping2",out two))throw new IOException("Current OEM preset was not supplied");
            var first=ControllerMapping(one);var second=ControllerMapping(two);
            if(!Convert.ToBoolean(oem.Call("/programhandle/isOpen",null,1200)))throw new IOException("OEM controller is not connected");
            controllerBefore1=ControllerMapping(one);controllerBefore2=ControllerMapping(two);
            string[] ids={"22","23","24","25"},keys={"66","67","68","69"};
            for(int n=0;n<ids.Length;n++){first[ids[n]]=ControllerKey(ids[n],keys[n]);second[ids[n]]=ControllerKey(ids[n],keys[n]);}
            controllerOwnsMapping=true;
            try{WriteControllerMapping(first,1);WriteControllerMapping(second,2);}
            catch{try{ControllerHardwareRelease();}catch{}throw;}
            return new{prepared=true,oemApiAcknowledged=true,physicalButtonVerificationRequired=true,extraButtons=4,frontShortcutButtons=2,keyboardAliases=new[]{"F13","F14","F15","F16"}};
        }
        internal void ControllerHardwareRelease()
        {
            if(!controllerOwnsMapping)return;
            // The baseline is the fresh selected OEM preset, never the incomplete
            // nine-row firmware response or a factory reset of unrelated controls.
            WriteControllerMapping(controllerBefore1,1);WriteControllerMapping(controllerBefore2,2);controllerOwnsMapping=false;
        }
    }
}
