// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal static class DisplaySyncVerification
{
    internal static async Task<int> Run(string path)
    {
        using var native=new IntelDisplayNative();var before=native.Read3D(9);var scope=native.Read3D(14);var cases=new List<object>();bool restored=false;string? error=null;
        if(!before.Available||!before.Reported||before.Value is not int original){await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new{passed=false,before,scope,error="Frame Sync unavailable",hardwareWrites=0},Backend.Program.Json));return 1;}
        try
        {
            foreach(int value in IntelSyncPolicy.Values)
            {
                try{native.Set3D(9,value);await Task.Delay(250);var actual=native.Read3D(9);cases.Add(new{requested=value,accepted=actual.Available&&actual.Value==value,actual,error=(string?)null});}
                catch(Exception failure){cases.Add(new{requested=value,accepted=false,actual=native.Read3D(9),error=failure.Message});}
                finally{if(native.Read3D(9).Value!=original)native.Set3D(9,original);}
            }
        }
        catch(Exception failure){error=failure.Message;}
        finally{try{if(native.Read3D(9).Value!=original)native.Set3D(9,original);restored=native.Read3D(9).Value==original;}catch(Exception failure){error=failure.Message;}}
        await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new{passed=restored&&error==null,before,after=native.Read3D(9),scope,cases,restored,error,scopeWrites=0,tdpWrites=0,colorHdrWrites=0,physicalOwnerInputTested=false},Backend.Program.Json));return restored&&error==null?0:1;
    }
}
