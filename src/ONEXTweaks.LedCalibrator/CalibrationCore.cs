using System;
using System.Collections.Generic;
using System.Linq;

namespace ONEXTweaks.Calibration
{
    public sealed class CalibrationZoneGroup
    {
        public string key {get;set;} public string name {get;set;} public int[] hardwareZoneIds {get;set;}
    }
    public static class CalibrationLayout
    {
        public static CalibrationZoneGroup[] Zones {get{return new[]{
            new CalibrationZoneGroup{key="upper",name="Верхняя",hardwareZoneIds=new[]{6}},
            new CalibrationZoneGroup{key="sticks",name="Стики",hardwareZoneIds=new[]{1,2}},
            new CalibrationZoneGroup{key="lower",name="Нижняя",hardwareZoneIds=new[]{5}}
        };}}
    }
    // UI-independent DTOs/math: usable by the UWP widget without WinForms or this transport.
    public sealed class CalibrationCase
    {
        public string id {get;set;} public string name {get;set;} public string hex {get;set;}
        public Dictionary<string,int[]> initialWireByZone {get;set;}
        public string source {get;set;} public string anchorAcceptance {get;set;}
    }
    public sealed class CalibrationPlan
    {
        public int schema {get;set;} public string kind {get;set;}
        public CalibrationCase[] cases {get;set;}
    }
    public sealed class ZoneResult
    {
        public int zoneId {get;set;} public string logicalGroup {get;set;} public int[] desiredWire {get;set;}
        public int[] acknowledgedWire {get;set;} public string rating {get;set;}
    }
    public sealed class CaseResult
    {
        public string caseId {get;set;} public string referenceSrgb {get;set;}
        public int hardwareBrightness {get;set;} public string comment {get;set;}
        public string savedUtc {get;set;} public ZoneResult[] zones {get;set;}
    }
    public sealed class CalibrationArchive
    {
        public int schema {get;set;} public string sessionId {get;set;}
        public string kind {get;set;} public bool completed {get;set;}
        public CaseResult[] results {get;set;} public int protocolVersion {get;set;}
    }
    public static class CalibrationSchedule
    {
        public static int[] Levels {get{return new[]{4,3,2,1};}}
        public static int[] Steps {get{return new[]{1,2,5,10,20};}}
        public static bool Reviewed(CaseResult value)
        {return value!=null&&value.zones!=null&&value.zones.Length==4&&value.zones.All(z=>z.rating!="unrated"&&z.rating!=null);}
        public static int FirstMissing(CalibrationPlan plan,IEnumerable<CaseResult> results)
        {
            for(int stage=0;stage<Levels.Length;stage++)for(int color=0;color<plan.cases.Length;color++)
                if(!results.Any(r=>r.caseId==plan.cases[color].id&&r.hardwareBrightness==Levels[stage]&&Reviewed(r)))return stage*plan.cases.Length+color;
            return -1;
        }
    }
    public static class CalibrationMath
    {
        public const double BlueBalance=0.425;
        public static int Byte(double value){return (int)Math.Max(0,Math.Min(255,Math.Round(value,MidpointRounding.AwayFromZero)));}
        public static void Validate(int[] rgb)
        {if(rgb==null||rgb.Length!=3)throw new ArgumentException("Three RGB bytes required");foreach(int value in rgb)if(value<0||value>255)throw new ArgumentOutOfRangeException("rgb");}
        public static int Decode(int channel)
        {
            if(channel<0||channel>255)throw new ArgumentOutOfRangeException("channel");
            double c=channel/255.0;return Byte(255*(c<=0.04045?c/12.92:Math.Pow((c+0.055)/1.055,2.4)));
        }
        public static int EncodeClosest(int wire)
        {
            if(wire<0||wire>255)throw new ArgumentOutOfRangeException("wire");
            int best=0,distance=256;
            for(int value=0;value<=255;value++){int difference=Math.Abs(Decode(value)-wire);if(difference<distance){best=value;distance=difference;if(difference==0)break;}}
            return best;
        }
        public static int[] Encoded(int[] wire){Validate(wire);return new[]{EncodeClosest(wire[0]),EncodeClosest(wire[1]),EncodeClosest(wire[2])};}
        // Hue and saturation use the existing blue-balanced basis. Intensity is the
        // largest physical RGB channel, normalized to 0..1, so even pure blue fits 100%.
        public static double[] Describe(int[] wire)
        {
            Validate(wire);double r=wire[0],g=wire[1],b=wire[2]/BlueBalance;
            double max=Math.Max(r,Math.Max(g,b)),min=Math.Min(r,Math.Min(g,b)),range=max-min,h=0;
            if(range>0){h=max==r?60*((g-b)/range):max==g?60*((b-r)/range+2):60*((r-g)/range+4);h=(h+360)%360;}
            return new[]{h,max==0?0:range/max,Math.Max(wire[0],Math.Max(wire[1],wire[2]))/255.0};
        }
        public static int[] Compose(double hue,double saturation,double intensity)
        {
            if(double.IsNaN(hue)||double.IsInfinity(hue)||double.IsNaN(saturation)||double.IsInfinity(saturation)||double.IsNaN(intensity)||double.IsInfinity(intensity))throw new ArgumentException("Finite coordinates required");
            double h=(hue%360+360)%360,s=Math.Max(0,Math.Min(1,saturation)),level=Math.Max(0,Math.Min(1,intensity));
            double x=s*(1-Math.Abs((h/60)%2-1)),m=1-s,r=0,g=0,b=0;
            if(h<60){r=s;g=x;}else if(h<120){r=x;g=s;}else if(h<180){g=s;b=x;}
            else if(h<240){g=x;b=s;}else if(h<300){r=x;b=s;}else{r=s;b=x;}
            r+=m;g+=m;b=(b+m)*BlueBalance;double scale=255*level/Math.Max(r,Math.Max(g,b));
            return new[]{Byte(r*scale),Byte(g*scale),Byte(b*scale)};
        }
        public static int[] Adjust(int[] wire,string axis,double delta)
        {
            Validate(wire);
            // Work in a white-balanced basis, so "paler" adds a neutral component rather
            // than increasing the already over-strong blue emitter. These are diagnostic
            // controls, not a measured gamut conversion or a final calibration profile.
            double r=wire[0],g=wire[1],b=wire[2]/BlueBalance;
            double max=Math.Max(r,Math.Max(g,b)),min=Math.Min(r,Math.Min(g,b)),range=max-min;
            double h=0,s=max==0?0:range/max,v=max/255.0;
            if(range>0){h=max==r?60*((g-b)/range):max==g?60*((b-r)/range+2):60*((r-g)/range+4);h=(h+360)%360;}
            if(axis=="hue")h=(h+delta+360)%360;
            else if(axis=="saturation")s=Math.Max(0,Math.Min(1,s+delta));
            else if(axis=="value")v=Math.Max(0,Math.Min(1/BlueBalance,v+delta));
            else throw new ArgumentException("Unknown color axis");
            double chroma=v*255*s,x=chroma*(1-Math.Abs((h/60)%2-1)),m=v*255-chroma;
            double rr=0,gg=0,bb=0;
            if(h<60){rr=chroma;gg=x;}else if(h<120){rr=x;gg=chroma;}else if(h<180){gg=chroma;bb=x;}
            else if(h<240){gg=x;bb=chroma;}else if(h<300){rr=x;bb=chroma;}else{rr=chroma;bb=x;}
            return new[]{Byte(rr+m),Byte(gg+m),Byte((bb+m)*BlueBalance)};
        }
    }
}
