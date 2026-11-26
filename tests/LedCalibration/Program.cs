using System;
using System.Linq;
using ONEXTweaks.Calibration;

internal static class LedCalibrationTests
{
    private static int checks;
    private static void Check(bool value,string label){checks++;if(!value)throw new Exception(label);}
    public static int Main()
    {
        int[] aqua={0,204,87};var pale=CalibrationMath.Adjust(aqua,"saturation",-.04);
        Check(pale[0]>0,"Paler aqua gains a neutral component");Check(Math.Abs(pale[1]-204)<=1,"Paler preserves color value");
        var deep=CalibrationMath.Adjust(aqua,"value",-.04);Check(deep[1]<aqua[1]&&deep[2]<aqua[2],"Deeper changes both cyan channels");
        Check(Math.Abs((double)deep[2]/deep[1]-(double)aqua[2]/aqua[1])<.02,"Deeper preserves hue ratio");
        foreach(var wire in new[]{new[]{0,0,0},new[]{255,0,0},new[]{0,255,0},new[]{0,0,255},new[]{118,48,78},aqua}){
            foreach(string axis in new[]{"hue","saturation","value"})foreach(double direction in new[]{-1.0,1.0}){
                var adjusted=CalibrationMath.Adjust(wire,axis,direction*(axis=="hue"?3:.04));CalibrationMath.Validate(adjusted);Check(adjusted.Length==3,"Valid byte output");
            }
            var identity=CalibrationMath.Adjust(wire,"hue",0);for(int c=0;c<3;c++)Check(Math.Abs(identity[c]-wire[c])<=1,"Neutral adjustment does not jump");
        }
        for(int wire=0;wire<256;wire++)Check(Math.Abs(CalibrationMath.Decode(CalibrationMath.EncodeClosest(wire))-wire)<=1,"Old-agent inverse encoding bounded to one byte");
        foreach(int r in new[]{0,1,17,64,128,255})foreach(int g in new[]{0,1,17,64,128,255})foreach(int b in new[]{0,1,17,64,128,255}){
            int[] wire={r,g,b};var p=CalibrationMath.Describe(wire);var rebuilt=CalibrationMath.Compose(p[0],p[1],p[2]);
            for(int c=0;c<3;c++)Check(Math.Abs(rebuilt[c]-wire[c])<=1,"Absolute slider coordinates preserve existing RGB");
        }
        Check(CalibrationMath.Compose(240,1,1).SequenceEqual(new[]{0,0,255}),"Pure blue fits intensity100 percent");
        Check(CalibrationMath.Compose(0,0,1).SequenceEqual(new[]{255,255,108}),"Neutral saturation uses balanced white");
        Check(CalibrationMath.Compose(360,1,1).SequenceEqual(new[]{255,0,0}),"Hue wraps without clipping");
        Check(CalibrationSchedule.Steps.SequenceEqual(new[]{1,2,5,10,20}),"Five explicit button steps");
        var schedulePlan=new CalibrationPlan{cases=Enumerable.Range(0,24).Select(i=>new CalibrationCase{id="case"+i}).ToArray()};
        var reviewed=Enumerable.Range(0,24).Select(i=>new CaseResult{caseId="case"+i,hardwareBrightness=4,zones=new[]{1,2,5,6}.Select(id=>new ZoneResult{zoneId=id,rating="accepted"}).ToArray()}).ToList();
        Check(CalibrationSchedule.FirstMissing(schedulePlan,reviewed)==24,"Completed brightness4 continues at brightness3");
        reviewed.Add(new CaseResult{caseId="case0",hardwareBrightness=3,zones=new[]{1,2,5,6}.Select(id=>new ZoneResult{zoneId=id,rating="unrated"}).ToArray()});
        Check(CalibrationSchedule.FirstMissing(schedulePlan,reviewed)==24,"Unrated brightness3 cannot inherit approval");
        reviewed=CalibrationSchedule.Levels.SelectMany(level=>Enumerable.Range(0,24).Select(i=>new CaseResult{caseId="case"+i,hardwareBrightness=level,zones=new[]{1,2,5,6}.Select(id=>new ZoneResult{zoneId=id,rating="accepted"}).ToArray()})).ToList();
        Check(CalibrationSchedule.FirstMissing(schedulePlan,reviewed)==-1,"All96 independently reviewed samples complete the schedule");
        Console.WriteLine("{\"passed\":true,\"checks\":"+checks+",\"modelOnly\":true,\"hardwareWrites\":0}");return 0;
    }
}
