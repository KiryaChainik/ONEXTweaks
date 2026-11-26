// SPDX-License-Identifier: AGPL-3.0-only
// One formatter supplies both the real RTSS slot and the in-widget preview.
using System.Globalization;
using System.Text.RegularExpressions;

namespace ONEXTweaks.WidgetBridge;

internal sealed record OverlayValueStatistics(double Minimum,double Average,double Maximum,double OnePercentLow);
internal sealed record OverlayValues
{
    public double? CpuLoadPercent{get;init;}
    public double? CpuTemperatureC{get;init;}
    public double? MemoryUsedGb{get;init;}
    public double? MemoryTotalGb{get;init;}
    public double? GpuLoadPercent{get;init;}
    public double? GpuTemperatureC{get;init;}
    public double? GpuPowerWatts{get;init;}
    public double? ApuPowerWatts{get;init;}
    public double? Pl1Watts{get;init;}
    public double? Pl2Watts{get;init;}
    public double? FanPercent{get;init;}
    public IReadOnlyList<uint>? CpuClocksMhz{get;init;}
    public double? GpuClockMhz{get;init;}
    public double? GpuMemoryGb{get;init;}
    public int? BatteryPercent{get;init;}
    public bool? OnAcPower{get;init;}
    public OverlayValueStatistics? Statistics{get;init;}
    public DateTime? Clock{get;init;}
}
internal sealed record OverlayPreview(int Level,string Markup,string ResolvedMarkup,string FontFace,int FontWeight,
    int TextSize,int TextBrightness,bool BackgroundEnabled,int BackgroundOpacity,int NativeZoomRatio,int NativeFontHeight,string NativeBaseColor,
    bool Graph,bool ExampleData,IReadOnlyList<double> GraphMs,bool CustomTagsPresent);

internal static class OverlayFormatting
{
    private static string[] DefaultItems(int level)=>level switch
    {
        1=>["FPS","Battery","Time"],2=>["FPS","Battery","CPU","Time"],
        3=>["AppName","FPS","CPU","CPUClock","GPU","GPUClock","Battery","Blank1","Memory","Time","TDPLimits","Fan"],
        _=>["AppName","FPS","FrametimeGraph","FpsStats","Blank1","GPU","CPU","CPUClock","CPUCores","TDPLimits","Memory","VRAM","Battery","GPUClock","Fan","Time"]
    };
    internal static string EditorDefaults()
    {
        var values=new List<string>{"TextSize:150","Font:Bahnschrift|400","TextColor:DYNAMIC","LabelColor:DEFAULT","Opacity:100","Background:0|60"};
        for(int level=1;level<=4;level++){string items=string.Join(',',DefaultItems(level));values.Add("L"+level+":"+items);values.Add("L"+level+"_Order:"+items);values.Add("L"+level+"_Columns:"+(level==4?1:3));}
        return string.Join(';',values);
    }
    internal static Dictionary<string,string> Parse(string config)
    {
        var result=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(string segment in config.Split(';')){int colon=segment.IndexOf(':');if(colon>0)result[segment[..colon]]=segment[(colon+1)..];}
        return result;
    }
    private static string Number(double? value,string suffix="",string format="0")=>value is {} n&&double.IsFinite(n)?n.ToString(format,CultureInfo.InvariantCulture)+suffix:"--";
    private static string Hex(string? value,string fallback)=>value!=null&&Regex.IsMatch(value,"\\A[0-9a-fA-F]{6}\\z")?value:fallback;
    private static int Integer(string value,int fallback,int minimum,int maximum)=>int.TryParse(value,out int n)?Math.Clamp(n,minimum,maximum):fallback;
    internal static string DimColor(string hex,int brightness)
    {
        if(brightness>=100)return hex;
        return string.Concat(Enumerable.Range(0,3).Select(i=>((Convert.ToInt32(hex.Substring(i*2,2),16)*brightness+50)/100).ToString("X2",CultureInfo.InvariantCulture)));
    }
    internal static (string Text,bool Graph) Build(int level,IReadOnlyDictionary<string,string> config,OverlayValues value)
    {
        if(level==0)return("",false);
        string Get(string key,string fallback)=>config.TryGetValue(key,out string? result)?result:fallback;
        string prefix="L"+level;
        string[] defaults=DefaultItems(level);
        var enabled=Get(prefix,string.Join(',',defaults)).Split(',').ToHashSet(StringComparer.Ordinal);
        var order=Get(prefix+"_Order",string.Join(',',defaults)).Split(',').Concat(defaults).Distinct().ToArray();
        var rows=new List<string>();bool graph=false;var line=new List<string>();bool isVertical=level==4&&Get(prefix+"_Columns","1")=="1";
        string globalColor=Get("TextColor","DYNAMIC"),labelColor=Get("LabelColor","DEFAULT");
        int brightness=Integer(Get("Opacity","100"),100,10,100);
        string Color(string id,string text,bool label=false)
        {
            string fallback=id switch{"CPU" or "CPUClock" or "CPUCores"=>"59DBFF","GPU" or "GPUClock"=>"AD9AFF","Battery"=>"9BE397",_=>"FFFFFF"};
            string color=label?Hex(Get(prefix+"_"+id+"_Color",labelColor),fallback):Hex(globalColor,fallback);
            return "<C="+DimColor(color,brightness)+">"+text+"<C>";
        }
        foreach(string id in order.Where(enabled.Contains))
        {
            if(id.StartsWith("Blank",StringComparison.Ordinal)){if(isVertical)rows.Add("");else if(line.Count>0){rows.Add(string.Join("   ",line));line.Clear();}continue;}
            string text=id switch
            {
                "AppName"=>"<APP>",
                "Time"=>(value.Clock??DateTime.Now).ToString("HH:mm",CultureInfo.InvariantCulture),
                "FPS"=>"<FR> FPS  <FT> ms",
                "Battery"=>"BAT "+Number(value.BatteryPercent,"%")+(value.OnAcPower==true?" AC":""),
                "Memory"=>"RAM "+Number(value.MemoryUsedGb," GB","0.0")+" / "+Number(value.MemoryTotalGb," GB","0.0"),
                "CPU"=>"CPU "+Number(value.CpuLoadPercent,"%")+"  "+Number(value.CpuTemperatureC," C"),
                "GPU"=>"GPU "+Number(value.GpuLoadPercent,"%")+"  "+Number(value.GpuPowerWatts," W","0.0")+"  "+Number(value.GpuTemperatureC," C"),
                "TDPLimits"=>"APU "+Number(value.ApuPowerWatts," W","0.0")+" (PL1 "+Number(value.Pl1Watts," W")+" / PL2 "+Number(value.Pl2Watts," W")+")",
                "Fan"=>"FAN "+Number(value.FanPercent,"%"),
                "CPUClock"=>"CPU CLOCK "+Number(value.CpuClocksMhz?.Count>0?value.CpuClocksMhz.Average(x=>(double)x):null," MHz"),
                "CPUCores"=>value.CpuClocksMhz?.Count>0?"CORES "+string.Join(" / ",value.CpuClocksMhz.Select(x=>(x/1000.0).ToString("0.0",CultureInfo.InvariantCulture)))+" GHz":"CORES --",
                "GPUClock"=>"GPU CLOCK "+Number(value.GpuClockMhz," MHz"),
                "VRAM"=>"GPU MEM "+Number(value.GpuMemoryGb," GB","0.0"),
                "FrametimeGraph"=>"<OBJ=00000000>",
                "FpsStats"=>"MIN "+Number(value.Statistics?.Minimum)+"  AVG "+Number(value.Statistics?.Average)+"  MAX "+Number(value.Statistics?.Maximum)+"  1% "+Number(value.Statistics?.OnePercentLow),
                _=>""
            };
            if(text.Length==0)continue;if(id=="FrametimeGraph")graph=true;
            int separator=text.IndexOf(' ');
            text=separator>0&&!text.StartsWith("<",StringComparison.Ordinal)?Color(id,text[..separator],true)+" "+Color(id,text[(separator+1)..]):Color(id,text);if(isVertical)rows.Add(text);else line.Add(text);
        }
        if(line.Count>0)rows.Add(string.Join("   ",line));
        string tags=Get(prefix+"_Custom","");if(tags.Length>2000)throw new InvalidDataException("Custom RTSS tags are too long");
        int size=Integer(Get("TextSize","150"),150,50,300);
        return("<S="+size+">"+string.Join('\n',rows)+(tags.Length>0?"\n"+tags:"")+"<S>",graph);
    }
    internal static OverlayValues Example()=>new(){CpuLoadPercent=28,CpuTemperatureC=55,MemoryUsedGb=10.5,MemoryTotalGb=32,
        GpuLoadPercent=72,GpuTemperatureC=53,GpuPowerWatts=6.2,ApuPowerWatts=10.8,Pl1Watts=15,Pl2Watts=20,FanPercent=38,
        CpuClocksMhz=new uint[]{2400,2600,2800,2200},GpuClockMhz=1250,GpuMemoryGb=1.5,BatteryPercent=80,OnAcPower=true,
        Statistics=new(52,60,64,49),Clock=new DateTime(2000,1,1,18,42,0)};
    internal static OverlayPreview Preview(int level,string config,int zoomRatio=2,int fontHeight=9,string baseColor="FFFFFF")
    {
        if(level is <1 or >4||config.Length>16000||config.Contains('\0'))throw new InvalidDataException("Invalid overlay preview request");
        var parsed=Parse(config);string Get(string key,string fallback)=>parsed.TryGetValue(key,out string? value)?value:fallback;
        var built=Build(level,parsed,Example());string[] font=Get("Font","Bahnschrift|400").Split('|');
        if(font[0].Length is <1 or >80||font[0].Any(c=>c<32||c>126))throw new InvalidDataException("Invalid RTSS font");
        string[] background=Get("Background","0|60").Split('|');
        return new(level,built.Text,built.Text.Replace("<APP>","D3D11").Replace("<FR>","60").Replace("<FT>","16.7"),font[0],
            Integer(font.Length>1?font[1]:"400",400,100,900),Integer(Get("TextSize","150"),150,50,300),Integer(Get("Opacity","100"),100,10,100),
            background[0]=="1",Integer(background.Length>1?background[1]:"60",60,10,100),Math.Clamp(zoomRatio,1,8),Math.Clamp(fontHeight,1,96),Hex(baseColor,"FFFFFF"),built.Graph,true,
            new double[]{16.7,16.5,16.8,17.1,16.5,16.7,16.6,20.2,16.8,16.5,16.7,16.6,16.8,17.2,16.6,16.7},Get("L"+level+"_Custom","").Length>0);
    }
}
