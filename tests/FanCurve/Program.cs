using System;
using ONEXTweaks.Power;
internal static class FanCurveTests
{
    private static int count;
    private static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;}
    private static void Reject(Action action,string name){bool rejected=false;try{action();}catch(ArgumentException){rejected=true;}Check(rejected,name);}
    private static int Main()
    {
        try
        {
            int[] points={20,40,60,80,90};FanCurvePolicy.Validate(points);
            Check(FanCurvePolicy.PercentAt(20,points)==20,"Cold floor");
            Check(FanCurvePolicy.PercentAt(45,points)==20,"First anchor");
            Check(FanCurvePolicy.PercentAt(50,points)==30,"First interpolation");
            Check(FanCurvePolicy.PercentAt(55,points)==40,"Second anchor");
            Check(FanCurvePolicy.PercentAt(65,points)==60,"Third anchor");
            Check(FanCurvePolicy.PercentAt(75,points)==80,"Fourth anchor");
            Check(FanCurvePolicy.PercentAt(85,points)==90,"Fifth anchor");
            Check(FanCurvePolicy.PercentAt(90,points)==95,"Safeguard ramp");
            Check(FanCurvePolicy.PercentAt(95,points)==100,"Thermal safeguard");
            Check(FanCurvePolicy.PercentAt(110,points)==100,"Hot safeguard");
            Check(FanCurvePolicy.PercentAt(88,new[]{20,20,20,20,20})==44,"Low curve ramps to safe duty");
            Check(FanCurvePolicy.PercentAt(30,new[]{100,100,100,100,100})==100,"Full duty");
            Reject(()=>FanCurvePolicy.Validate(null),"Missing curve");Reject(()=>FanCurvePolicy.Validate(new[]{20,30}),"Missing points");
            Reject(()=>FanCurvePolicy.Validate(new[]{19,30,40,50,60}),"Unsafe floor");Reject(()=>FanCurvePolicy.Validate(new[]{20,30,40,50,101}),"Excess duty");Reject(()=>FanCurvePolicy.Validate(new[]{20,40,30,50,60}),"Falling curve");
            Reject(()=>FanCurvePolicy.PercentAt(double.NaN,points),"Missing sensor");Reject(()=>FanCurvePolicy.PercentAt(double.PositiveInfinity,points),"Infinite sensor");Reject(()=>FanCurvePolicy.PercentAt(0,points),"Invalid sensor low");Reject(()=>FanCurvePolicy.PercentAt(111,points),"Invalid sensor high");
            Console.WriteLine("{\"passed\":true,\"checks\":"+count+",\"modelOnly\":true,\"hardwareTested\":false}");return 0;
        }
        catch(Exception failure){Console.WriteLine("{\"passed\":false,\"error\":\""+failure.Message+"\"}");return 1;}
    }
}
