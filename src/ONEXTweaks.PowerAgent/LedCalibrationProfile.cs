using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web.Script.Serialization;

namespace ONEXTweaks.Power
{
    // Owner-rated reference profile. Independent optical holdout remains separate.
    // Output is already OEM bytes: never apply another sRGB decode to it.
    internal sealed class LedCalibrationProfile
    {
        public sealed class Node
        {
            public string caseId {get;set;} public string referenceSrgb {get;set;} public int[] rgb {get;set;}
        }
        public sealed class Level
        {
            public int brightness {get;set;} public int[][] wire {get;set;} public string[] ratings {get;set;}
        }
        public sealed class Group
        {
            public string key {get;set;} public int[] zoneIds {get;set;} public Level[] levels {get;set;}
        }
        public sealed class Document
        {
            public int schema {get;set;} public string method {get;set;} public string profileId {get;set;}
            public string sourceSha256 {get;set;} public int sourceSamples {get;set;}
            public Node[] nodes {get;set;} public double neutralSaturationScale {get;set;} public Group[] groups {get;set;}
        }
        private sealed class Stage
        {
            internal int[][] Wire;
            internal double[][] Residual;
        }
        private readonly Document document;
        private readonly double[][] positions;
        private readonly int[] neutrals;
        private readonly Dictionary<int,Dictionary<int,Stage>> values=new Dictionary<int,Dictionary<int,Stage>>();
        internal string ProfileId {get{return document.profileId;}}
        internal string SourceSha256 {get{return document.sourceSha256;}}
        internal static LedCalibrationProfile Parse(string text)
        {
            if(text==null||text.Length>2097152)throw new ArgumentException("Calibration profile is missing or too large");
            return new LedCalibrationProfile(new JavaScriptSerializer{MaxJsonLength=2097152,RecursionLimit=40}.Deserialize<Document>(text));
        }
        private static void Bytes(int[] rgb)
        {if(rgb==null||rgb.Length!=3||rgb.Any(x=>x<0||x>255))throw new ArgumentException("Three RGB bytes required");}
        private LedCalibrationProfile(Document input)
        {
            if(input==null||input.schema!=1||input.method!="neutral-anchored-shepard-residual-srgb-v1"||input.sourceSamples!=96||
               string.IsNullOrEmpty(input.profileId)||input.profileId.Length>80||input.sourceSha256==null||input.sourceSha256.Length!=64||input.sourceSha256.Any(c=>!Uri.IsHexDigit(c))||
               input.nodes==null||input.nodes.Length!=25||input.groups==null||input.groups.Length!=3||input.neutralSaturationScale!=.1)
                throw new ArgumentException("Unsupported calibration profile");
            document=input;var unique=new HashSet<string>();var identities=new HashSet<string>();
            foreach(var node in input.nodes)
            {
                if(node==null||string.IsNullOrEmpty(node.caseId)||!identities.Add(node.caseId))throw new ArgumentException("Duplicate or missing reference identity");Bytes(node.rgb);
                string hex=string.Format(CultureInfo.InvariantCulture,"#{0:X2}{1:X2}{2:X2}",node.rgb[0],node.rgb[1],node.rgb[2]);
                if(!hex.Equals(node.referenceSrgb,StringComparison.OrdinalIgnoreCase)||!unique.Add(hex))throw new ArgumentException("Invalid or duplicate reference color");
            }
            for(int corner=0;corner<8;corner++)
                if(!input.nodes.Any(n=>n.rgb[0]==((corner&1)!=0?255:0)&&n.rgb[1]==((corner&2)!=0?255:0)&&n.rgb[2]==((corner&4)!=0?255:0)))
                    throw new ArgumentException("Calibration does not include every cube corner");
            positions=input.nodes.Select(n=>n.rgb.Select(x=>x/255d).ToArray()).ToArray();
            neutrals=Enumerable.Range(0,positions.Length).Where(i=>input.nodes[i].rgb.Distinct().Count()==1).OrderBy(i=>positions[i][0]).ToArray();
            if(neutrals.Length!=5)throw new ArgumentException("Calibration requires four gray references plus off");
            var expected=new Dictionary<string,int[]>{{"upper",new[]{6}},{"sticks",new[]{1,2}},{"lower",new[]{5}}};
            if(input.groups.Select(g=>g==null?null:g.key).Distinct().Count()!=3)throw new ArgumentException("Duplicate calibration group");
            foreach(var group in input.groups)
            {
                if(group==null||group.key==null||!expected.ContainsKey(group.key)||group.zoneIds==null||!group.zoneIds.OrderBy(x=>x).SequenceEqual(expected[group.key])||
                   group.levels==null||group.levels.Length!=4||!group.levels.Select(l=>l==null?0:l.brightness).OrderBy(x=>x).SequenceEqual(new[]{1,2,3,4}))
                    throw new ArgumentException("Calibration zone or brightness level is missing");
                var levels=new Dictionary<int,Stage>();
                foreach(var level in group.levels)
                {
                    if(level.wire==null||level.wire.Length!=positions.Length||level.ratings==null||level.ratings.Length!=positions.Length)throw new ArgumentException("Calibration values are missing");
                    for(int i=0;i<positions.Length;i++)
                    {
                        Bytes(level.wire[i]);bool black=input.nodes[i].rgb.All(x=>x==0);
                        if(black?(level.ratings[i]!="implicit_off"||level.wire[i].Any(x=>x!=0)):(level.ratings[i]!="accepted"&&level.ratings[i]!="provisional"))
                            throw new ArgumentException("Calibration contains an unreviewed color");
                    }
                    var stage=new Stage{Wire=level.wire.Select(rgb=>(int[])rgb.Clone()).ToArray(),Residual=new double[positions.Length][]};
                    for(int i=1;i<neutrals.Length;i++)
                        if(stage.Wire[neutrals[i]].Sum()<stage.Wire[neutrals[i-1]].Sum())throw new ArgumentException("Neutral references reverse total drive");
                    for(int i=0;i<positions.Length;i++)
                    {
                        var baseline=Baseline(stage,positions[i]);double factor=ChromaFactor(positions[i]);
                        stage.Residual[i]=Enumerable.Range(0,3).Select(channel=>factor==0?0:(stage.Wire[i][channel]-baseline[channel])/factor).ToArray();
                    }
                    levels.Add(level.brightness,stage);
                }
                foreach(int zone in group.zoneIds)values.Add(zone,levels);
            }
        }
        private static double Linear(double value)
        {return value<=.04045?value/12.92:Math.Pow((value+.055)/1.055,2.4);}
        private double ChromaFactor(double[] rgb)
        {
            double max=rgb.Max(),s=max==0?0:(max-rgb.Min())/max;
            return s*s/(s*s+document.neutralSaturationScale*document.neutralSaturationScale);
        }
        private double[] Baseline(Stage stage,double[] rgb)
        {
            double max=rgb.Max();if(max==0)return new double[3];
            int segment=1;while(segment<neutrals.Length-1&&positions[neutrals[segment]][0]<max)segment++;
            int lo=neutrals[segment-1],hi=neutrals[segment];
            double fraction=(max-positions[lo][0])/(positions[hi][0]-positions[lo][0]);
            return Enumerable.Range(0,3).Select(channel=>
                (stage.Wire[lo][channel]*(1-fraction)+stage.Wire[hi][channel]*fraction)*Linear(rgb[channel])/Linear(max)).ToArray();
        }
        internal double[] MapNormalized(int zone,int brightness,double r,double g,double b)
        {
            if(brightness<1||brightness>4||!values.ContainsKey(zone))throw new ArgumentOutOfRangeException("zone or brightness");
            var point=new[]{r,g,b};if(point.Any(x=>double.IsNaN(x)||double.IsInfinity(x)||x<0||x>1))throw new ArgumentOutOfRangeException("RGB");
            var stage=values[zone][brightness];var residual=new double[3];double totalWeight=0;
            for(int i=0;i<positions.Length;i++)
            {
                double distance=0;for(int channel=0;channel<3;channel++){double delta=point[channel]-positions[i][channel];distance+=delta*delta;}
                if(distance<1e-24)return stage.Wire[i].Select(x=>(double)x).ToArray();
                double weight=1/distance;totalWeight+=weight;
                for(int channel=0;channel<3;channel++)residual[channel]+=weight*stage.Residual[i][channel];
            }
            var baseline=Baseline(stage,point);double factor=ChromaFactor(point);
            return Enumerable.Range(0,3).Select(channel=>Math.Max(0,Math.Min(255,baseline[channel]+factor*residual[channel]/totalWeight))).ToArray();
        }
        internal int[] Transform(int zone,int brightness,int r,int g,int b)
        {
            Bytes(new[]{r,g,b});if(brightness<1||brightness>4)throw new ArgumentOutOfRangeException("brightness");
            // Connector7 has not been physically assessed.
            if(zone==7)return new[]{LedColorTransform.ToLinearByte(r),LedColorTransform.ToLinearByte(g),LedColorTransform.ToLinearByte(b)};
            return MapNormalized(zone,brightness,r/255d,g/255d,b/255d).Select(x=>(int)Math.Round(x,MidpointRounding.AwayFromZero)).ToArray();
        }
    }
}
