// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using ONEXTweaks.Power;

namespace ONEXTweaks.WidgetBridge;
internal sealed record PreferredPower(int SchemaVersion,int Pl1Watts,int Pl2Watts,DateTimeOffset UpdatedUtc,bool? SeparatePl2=null,int? ManualPl2Watts=null);
internal sealed class PowerPreferenceStore(string path)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented=true };
    internal PreferredPower? Load()
    {
        if(!File.Exists(path))return null;
        var value=ReadSaved() ?? throw new InvalidDataException("Empty saved power settings");
        if(value.SchemaVersion!=1)throw new InvalidDataException("Unsupported saved power settings");
        Validate(value.Pl1Watts,value.Pl2Watts);ValidateSelection(value.Pl1Watts,value.Pl2Watts,value.SeparatePl2,value.ManualPl2Watts);return value;
    }
    private PreferredPower? ReadSaved()
    {using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);return JsonSerializer.Deserialize<PreferredPower>(stream,Json);}
    internal void Save(int pl1,int pl2,bool? separatePl2=null,int? manualPl2Watts=null)
    {
        Validate(pl1,pl2);
        if(separatePl2 is null)
        {try{var prior=Load();if(prior?.SeparatePl2 is not null){separatePl2=prior.SeparatePl2;manualPl2Watts=prior.ManualPl2Watts;}}catch(Exception e) when(e is IOException or JsonException){}}
        ValidateSelection(pl1,pl2,separatePl2,manualPl2Watts);
        // A CPU/display/profile transition can preserve exactly the same limits.
        // Keep both owner values and the original modification time in that case.
        try{var existing=Load();if(existing!=null&&existing.Pl1Watts==pl1&&existing.Pl2Watts==pl2&&existing.SeparatePl2==separatePl2&&existing.ManualPl2Watts==manualPl2Watts)return;}
        catch(Exception error) when(error is IOException or JsonException){}
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp=path+".tmp";
        File.WriteAllText(temp,JsonSerializer.Serialize(new PreferredPower(1,pl1,pl2,DateTimeOffset.UtcNow,separatePl2,manualPl2Watts),Json));
        File.Move(temp,path,true);
    }
    internal void RestoreAfterFactoryRollback(PreferredPower? previous, PreferredPower? expected)
    {
        var current = Load();
        if (current != expected)
            throw new IOException("Preferred power settings changed during the profile rollback.");
        if (previous == current) return;
        if (previous == null) File.Delete(path);
        else
        {
            string temporary = path + ".rollback.tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(previous, Json));
            File.Move(temporary, path, true);
        }
    }
    private static void ValidateSelection(int pl1,int pl2,bool? separate,int? manual)
    {if(separate.HasValue!=manual.HasValue||manual<Pl2ControlPolicy.MinimumPl1||manual>Pl2ControlPolicy.ExtendedManualCeiling||separate==false&&pl2!=Pl2ControlPolicy.Automatic(pl1))throw new InvalidDataException("Saved PL2 selection is inconsistent with effective limits");}
    private static void Validate(int pl1,int pl2)
    {if(!Pl2ControlPolicy.IsWritable(pl1,pl2))throw new InvalidDataException("Saved power limits are outside verified device bounds");}
}
