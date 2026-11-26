using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using ONEXTweaks.Calibration;
using ONEXTweaks.Power;

internal static class TestLedProfile
{
    private static int checks;
    private static void Check(bool condition,string label){checks++;if(!condition)throw new Exception(label);}
    private static string Sha(byte[] value)
    {using(var sha=SHA256.Create())return string.Concat(sha.ComputeHash(value).Select(x=>x.ToString("x2",CultureInfo.InvariantCulture)));}
    private static int[] Hex(string hex)
    {return new[]{Convert.ToInt32(hex.Substring(1,2),16),Convert.ToInt32(hex.Substring(3,2),16),Convert.ToInt32(hex.Substring(5,2),16)};}
    private static double[] Cross(double[] a,double[] b)
    {return new[]{a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]};}
    private static double[] Sub(double[] a,double[] b){return a.Zip(b,(x,y)=>x-y).ToArray();}
    private static void Reject(Action action,string label)
    {bool rejected=false;try{action();}catch(ArgumentException){rejected=true;}Check(rejected,label);}
    public static int Main(string[] args)
    {
        if(args.Length!=2)throw new ArgumentException("Profile and original archive paths required");
        var json=new JavaScriptSerializer{MaxJsonLength=2097152,RecursionLimit=40};
        string text=File.ReadAllText(args[0]);var profile=LedCalibrationProfile.Parse(text);
        var document=json.Deserialize<LedCalibrationProfile.Document>(text);
        var archive=json.Deserialize<CalibrationArchive>(File.ReadAllText(args[1]));
        Check(profile.SourceSha256==Sha(File.ReadAllBytes(args[1])),"Candidate is linked to the exact original owner archive");
        Check(archive.completed&&archive.results.Length==96,"Completed owner archive has96 samples");
        int anchors=0;
        foreach(var row in archive.results)
        {
            var rgb=Hex(row.referenceSrgb);
            foreach(var zone in row.zones)
            {
                var actual=profile.Transform(zone.zoneId,row.hardwareBrightness,rgb[0],rgb[1],rgb[2]);
                Check(actual.SequenceEqual(zone.acknowledgedWire),"Every reference reproduces the actual acknowledged physical bytes");anchors++;
            }
        }
        int gridPoints=0;int maximumNeutralMaxChannelDrop=0;
        foreach(int zone in new[]{1,5,6})foreach(int brightness in new[]{1,2,3,4})
        {
            for(int r=0;r<=16;r++)for(int g=0;g<=16;g++)for(int b=0;b<=16;b++)
            {
                var mapped=profile.MapNormalized(zone,brightness,r/16d,g/16d,b/16d);
                Check(mapped.All(x=>!double.IsNaN(x)&&!double.IsInfinity(x)&&x>= -1e-9&&x<=255+1e-9),"Full cube grid is covered without extrapolation or out-of-range output");gridPoints++;
            }
            int previous=-1;double previousTotal=-1;
            for(int value=0;value<=255;value++)
            {
                var mapped=profile.Transform(zone,brightness,value,value,value);
                maximumNeutralMaxChannelDrop=Math.Max(maximumNeutralMaxChannelDrop,previous-mapped.Max());previous=mapped.Max();
                // The owner changes channel balance as well as level. Max(R,G,B)
                // can dip when the largest channel changes (e.g. blue to green).
                // Check total unrounded drive; neither quantity measures luminance.
                double total=profile.MapNormalized(zone,brightness,value/255d,value/255d,value/255d).Sum();
                Check(total+1e-8>=previousTotal,"Neutral ramp has no reversed total RGB drive");previousTotal=total;
            }
            Check(profile.Transform(zone,brightness,0,0,0).All(x=>x==0),"Black remains off at every zone/brightness");
        }
        foreach(var row in archive.results)
        {
            var rgb=Hex(row.referenceSrgb);
            var unmapped=profile.Transform(7,row.hardwareBrightness,rgb[0],rgb[1],rgb[2]);
            Check(unmapped.SequenceEqual(rgb.Select(LedColorTransform.ToLinearByte)),"Unassessed connector stays on the existing transport");
            Check(profile.Transform(1,row.hardwareBrightness,rgb[0],rgb[1],rgb[2]).SequenceEqual(profile.Transform(2,row.hardwareBrightness,rgb[0],rgb[1],rgb[2])),"Linked sticks remain identical");
        }
        var positions=document.nodes.Select(n=>n.rgb.Select(x=>x/255d).ToArray()).ToArray();
        double largestNearSampleDifference=0,largestSingleByteStep=0;int sampleNeighborhoods=0;
        foreach(var point in positions)foreach(int axis in new[]{0,1,2})foreach(int sign in new[]{-1,1})
        {
            if(point[axis]+sign*1e-8<0||point[axis]+sign*1e-8>1)continue;
            var neighbor=(double[])point.Clone();neighbor[axis]+=sign*1e-8;
            foreach(int zone in new[]{1,5,6})foreach(int brightness in new[]{1,2,3,4})
            {
                var exact=profile.MapNormalized(zone,brightness,point[0],point[1],point[2]);
                var near=profile.MapNormalized(zone,brightness,neighbor[0],neighbor[1],neighbor[2]);
                double jump=exact.Zip(near,(a,b)=>Math.Abs(a-b)).Max();largestNearSampleDifference=Math.Max(largestNearSampleDifference,jump);
                Check(jump<.001,"No discontinuity near reference or neutral-ramp knots");
                if(point[axis]+sign/255d>=0&&point[axis]+sign/255d<=1)
                {
                    var pixel=(double[])point.Clone();pixel[axis]+=sign/255d;var mapped=profile.MapNormalized(zone,brightness,pixel[0],pixel[1],pixel[2]);
                    double change=exact.Zip(mapped,(a,b)=>Math.Abs(a-b)).Max();largestSingleByteStep=Math.Max(largestSingleByteStep,change);
                    Check(change<32,"No extreme one-byte input jump next to a reference");
                }
            }
            sampleNeighborhoods++;
        }
        double largestHueStep=0;
        foreach(int zone in new[]{1,5,6})foreach(int brightness in new[]{1,2,3,4})
        {
            double[] previous=null,first=null;
            for(int step=0;step<3600;step++)
            {
                double h=step/600d,x=1-Math.Abs(h%2-1);double[] p;
                if(h<1)p=new[]{1d,x,0d};else if(h<2)p=new[]{x,1d,0d};else if(h<3)p=new[]{0d,1d,x};
                else if(h<4)p=new[]{0d,x,1d};else if(h<5)p=new[]{x,0d,1d};else p=new[]{1d,0d,x};
                var value=profile.MapNormalized(zone,brightness,p[0],p[1],p[2]);if(first==null)first=value;
                if(previous!=null)largestHueStep=Math.Max(largestHueStep,value.Zip(previous,(a,b)=>Math.Abs(a-b)).Max());previous=value;
            }
            largestHueStep=Math.Max(largestHueStep,first.Zip(previous,(a,b)=>Math.Abs(a-b)).Max());
        }
        Check(largestHueStep<3,"Hue sweep, including wrap, has no abrupt jumps at .1 degree steps");
        Check(document.groups.Any(g=>g.levels.Skip(1).Any(l=>l.wire.Where((rgb,i)=>!rgb.SequenceEqual(g.levels[0].wire[i])).Any())),"Independent brightness adjustments were retained");
        Reject(()=>profile.Transform(6,0,1,2,3),"Brightness0 rejected");Reject(()=>profile.Transform(6,5,1,2,3),"Brightness5 rejected");
        Reject(()=>profile.Transform(9,4,1,2,3),"Unknown zone rejected");Reject(()=>profile.Transform(6,4,-1,2,3),"Negative RGB rejected");
        Reject(()=>profile.MapNormalized(6,4,double.NaN,0,0),"NaN rejected");Reject(()=>profile.MapNormalized(6,4,0,0,double.PositiveInfinity),"Infinity rejected");
        foreach(string mutation in new[]{"schema","missingLevel","badWire","duplicateZone","duplicateNode","unreviewed","badBlack","badMethod","badScale","badNeutral"})
        {
            var changed=json.Deserialize<LedCalibrationProfile.Document>(text);
            if(mutation=="schema")changed.schema=2;
            if(mutation=="missingLevel")changed.groups[0].levels=changed.groups[0].levels.Take(3).ToArray();
            if(mutation=="badWire")changed.groups[0].levels[0].wire[1][0]=256;
            if(mutation=="duplicateZone")changed.groups[0].zoneIds=new[]{1,2};
            if(mutation=="duplicateNode")changed.nodes[1]=changed.nodes[2];
            if(mutation=="unreviewed")changed.groups[0].levels[0].ratings[1]="needs_work";
            if(mutation=="badBlack")changed.groups[0].levels[0].wire[0][0]=1;
            if(mutation=="badMethod")changed.method="unsupported";
            if(mutation=="badScale")changed.neutralSaturationScale=double.NaN;
            if(mutation=="badNeutral")changed.groups[0].levels[0].wire[Array.FindIndex(changed.nodes,n=>n.caseId=="N025")]=new[]{255,255,255};
            Reject(()=>LedCalibrationProfile.Parse(json.Serialize(changed)),"Invalid profile rejected: "+mutation);
        }
        Console.WriteLine(json.Serialize(new{passed=true,checks=checks,ownerAnchorZoneCases=anchors,cubeGridPoints=gridPoints,sampleNeighborhoods=sampleNeighborhoods,
            maximumNearSampleDifference=largestNearSampleDifference,maximumOneByteStep=largestSingleByteStep,maximumHueStep=largestHueStep,maximumNeutralMaxChannelDrop=maximumNeutralMaxChannelDrop,
            neutralMetric="total RGB drive, not measured luminance; max-channel dip separately reported",method="neutral-anchored Shepard residual; acknowledged output bytes",
            independentOwnerHoldoutTested=false,colorimetryMeasured=false,modelOnly=true,hardwareWrites=0,productionInstalled=false}));
        return 0;
    }
}
