// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using System.Text.Json.Nodes;
using ONEXTweaks.Panel;
using ONEXTweaks.Power;
namespace ONEXTweaks.WidgetBridge;
internal static partial class Program
{
    private static PreferredPower? PowerSelectionFor(PowerState actual)
    {
        try{var saved=powerPreferences.Load();return saved is not null&&saved.Pl1Watts==actual.Pl1Watts&&saved.Pl2Watts==actual.Pl2Watts?saved:null;}
        catch(Exception e) when(e is IOException or JsonException){return null;}
    }
    private static string PowerStatePayload()
    {
        var actual=state;if(actual is null)return "null";var saved=PowerSelectionFor(actual);
        var value=JsonSerializer.SerializeToNode(actual,Backend.Program.Json)!.AsObject();value["separatePl2"]=saved?.SeparatePl2;value["rememberedPl2Watts"]=saved?.ManualPl2Watts;return value.ToJsonString(Backend.Program.Json);
    }
    private static string PowerSelectionSignature(PowerState actual)
    {var saved=PowerSelectionFor(actual);return actual.RawLimits+":"+saved?.SeparatePl2+":"+saved?.ManualPl2Watts;}
    private static async Task InitializePl2Selection(PowerChannel power,JsonElement request)
    {
        await powerGate.WaitAsync();
        try
        {
            var saved=powerPreferences.Load();if(saved?.SeparatePl2 is not null)return;
            var live=await power.ReadAsync();if(!live.Ok||live.State is not{CanWrite:true} actual)throw new IOException(live.Error??"No power readback");
            if(saved is not null&&(saved.Pl1Watts!=actual.Pl1Watts||saved.Pl2Watts!=actual.Pl2Watts))throw new IOException("PL2 metadata migration deferred: current limits differ from the saved preference");
            bool separate=request.GetProperty("OnexPl2SelectionInitialize").GetBoolean();int manual=request.TryGetProperty("OnexRememberedPl2Watts",out var remembered)?Integer(remembered):(int)actual.Pl2Watts;
            if (!Pl2ControlPolicy.IsWritable(actual.Pl1Watts, actual.Pl2Watts))
                throw new InvalidDataException("PL2 metadata migration deferred for an external power state.");
            if (manual is <Pl2ControlPolicy.MinimumPl1 or >Pl2ControlPolicy.ExtendedManualCeiling)
                throw new InvalidDataException("Invalid remembered PL2");
            if(!separate&&actual.Pl2Watts!=Pl2ControlPolicy.Automatic((int)actual.Pl1Watts))separate=true;
            if(separate)manual=(int)actual.Pl2Watts;
            // Metadata only: upgrading a widget must never apply a guessed wattage.
            powerPreferences.Save((int)actual.Pl1Watts,(int)actual.Pl2Watts,separate,manual);state=actual;
        }
        finally{powerGate.Release();}
    }
}
