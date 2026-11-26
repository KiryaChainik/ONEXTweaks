using System.Text.Json;
using ONEXTweaks.Power;
namespace ONEXTweaks.WidgetBridge;
internal static partial class Program
{
 internal static bool ProfileSyncRequired(JsonElement request)=>request.TryGetProperty("OnexUserInput",out var input)&&input.ValueKind==JsonValueKind.True;
 internal static bool PowerSnapshotWritable(double pl1,double pl2)=>Pl2ControlPolicy.IsWritable(pl1,pl2);
}
internal static class AvailabilityRecoveryVerification
{
 internal static int Run(string file)
 {
  int checks=0;bool passed=false;string? error=null;
  void Check(bool ok,string name){checks++;if(!ok)throw new InvalidOperationException(name);}
  try
  {
   foreach(string get in new[]{"OnexDisplayProfilesGet","OnexProfilesGet","OnexControllerGet","OnexDisplayGet"}){using var q=JsonDocument.Parse("{\""+get+"\":true}");Check(!Program.ProfileSyncRequired(q.RootElement),"Read request synchronized profile");}
   using(var edit=JsonDocument.Parse("{\"OnexUserInput\":true,\"OnexDisplaySet\":{}}"))Check(Program.ProfileSyncRequired(edit.RootElement),"Explicit edit skipped validation");
   Check(Program.PowerSnapshotWritable(8,8)&&Program.PowerSnapshotWritable(40,41),"Valid boundary excluded");Check(!Program.PowerSnapshotWritable(25,52),"OEM52 incorrectly roundtrippable");Check(!Program.PowerSnapshotWritable(41,41)&&!Program.PowerSnapshotWritable(8,42)&&!Program.PowerSnapshotWritable(7,8),"Invalid boundary accepted");
   var live=new ProfileValues(25,52,0,0,0,1,new("RTSS",false,60),0,0,true);bool rejected=false;try{live.Validate();}catch(InvalidDataException){rejected=true;}Check(rejected,"Write limit was widened");Check(live.Pl1==25&&live.Pl2==52,"Readback was clamped/mutated");passed=true;
  }catch(Exception e){error=e.Message;}
  File.WriteAllText(file,JsonSerializer.Serialize(new{passed,checks,error,hardwareWrites=0,ownerPreferenceWrites=0,simulationOnly=true},Backend.Program.Json));return passed?0:1;
 }
}
