using System;
using XboxGamingBar;
internal static class BatteryCapTests
{
    private static int checks;
    private static void Check(bool ok,string label){checks++;if(!ok)throw new Exception(label);}
    public static int Main()
    {
        foreach(int cap in new[]{30,35,50,65,80,85,90,95,100}){
            Check(OnexBatteryCapPolicy.Select(cap-1,true,cap,false)==OnexBatteryTone.White,"Below acknowledged cap");
            Check(OnexBatteryCapPolicy.Select(cap,true,cap,false)==OnexBatteryTone.AtCapGreen,"At cap");
            Check(OnexBatteryCapPolicy.Select(cap,true,cap,true)==OnexBatteryTone.AtCapGreen,"Reached cap takes priority over saver");
            Check(OnexBatteryCapPolicy.Select(cap-.01,true,cap,true)==OnexBatteryTone.EnergySaverYellow,"Fraction below cap cannot fabricate full");
            if(cap<100)Check(OnexBatteryCapPolicy.Select(cap+1,true,cap,true)==OnexBatteryTone.AtCapGreen,"Above lowered cap");
        }
        Check(OnexBatteryCapPolicy.Select(99,true,100,false)==OnexBatteryTone.White,"Disabled limit uses100");
        Check(OnexBatteryCapPolicy.Select(100,true,100,false)==OnexBatteryTone.AtCapGreen,"Unlimited full battery");
        Check(OnexBatteryCapPolicy.Select(90,true,95,true)==OnexBatteryTone.EnergySaverYellow,"Raising acknowledged cap clears green");
        Check(OnexBatteryCapPolicy.Select(90,true,85,true)==OnexBatteryTone.AtCapGreen,"Lowering acknowledged cap sets green");
        Check(OnexBatteryCapPolicy.Select(100,false,80,false)==OnexBatteryTone.White,"Unknown/disconnected cap never assumed reached");
        Check(OnexBatteryCapPolicy.Select(90,true,null,true)==OnexBatteryTone.EnergySaverYellow,"Missing cap permits only known saver indication");
        foreach(double? invalid in new double?[]{null,-1,101,double.NaN,double.PositiveInfinity,double.NegativeInfinity})
            Check(OnexBatteryCapPolicy.Select(invalid,true,90,true)==OnexBatteryTone.White,"Unavailable battery stays neutral");
        foreach(int invalid in new[]{0,29,101})Check(OnexBatteryCapPolicy.Select(95,true,invalid,false)==OnexBatteryTone.White,"Invalid cap cannot trigger green");
        Console.WriteLine("{\"passed\":true,\"checks\":"+checks+",\"syntheticPolicyOnly\":true,\"hardwareWrites\":0}");return 0;
    }
}
