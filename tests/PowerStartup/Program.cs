using ONEXTweaks.Power;
using System.Text.Json;
var checks=new List<string>();
void Check(bool valid,string name){if(!valid)throw new Exception(name);checks.Add(name);}
Check(PowerRules.CanRestoreCaptured(25,52),"Observed factory25/52 can be restored as captured state");
foreach(var pair in new[]{(25,52),(24,52),(25,53),(26,52),(39,42),(40,53)})
{bool rejected=false;try{PowerRules.Validate(pair.Item1,pair.Item2);}catch(ArgumentException){rejected=true;}Check(rejected,"Explicit target outside verified user bounds rejected: "+pair);}
foreach (int pl2 in Enumerable.Range(41,12)) PowerRules.Validate(40,pl2);
foreach(var pair in new[]{(24d,52d),(25d,53d),(26d,52d),(25d,51d),(12.5d,13d),(double.NaN,52d),(25d,double.PositiveInfinity)})
Check(!PowerRules.CanRestoreCaptured(pair.Item1,pair.Item2),"Unknown/fractional captured state rejected: "+pair);
Check(PowerRules.CanRestoreCaptured(22,23)&&PowerRules.CanRestoreCaptured(40,41),"Normal captured user pairs remain restorable");
Check(PowerRules.Pl1(0xA0E03,0x000081A000DD80C8)==25&&PowerRules.Pl2(0xA0E03,0x000081A000DD80C8)==52,"Real postboot raw register decodes to factory25/52");
Check(!PowerRules.IsLocked(0x000081A000DD80C8)&&PowerRules.IsEnabled(0x000081A000DD80C8),"Observed factory register enabled/unlocked");
Console.WriteLine(JsonSerializer.Serialize(new{passed=true,checks,hardwareTested=false}));
