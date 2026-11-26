using System;
using ONEXTweaks.Power;

internal static class TestLedColor
{
    private static int checks;
    private static void Check(bool condition,string label){checks++;if(!condition)throw new Exception(label);}
    public static int Main()
    {
        Check(LedColorTransform.Command(1,255,0,0,false)=="/rgbPartition/setColor/1/255/0/0/false","Red primary unchanged");
        Check(LedColorTransform.Command(2,0,255,0,false)=="/rgbPartition/setColor/2/0/255/0/false","Green primary unchanged");
        Check(LedColorTransform.Command(7,0,0,255,false)=="/rgbPartition/setColor/7/0/0/255/false","Blue primary unchanged");
        Check(LedColorTransform.Command(5,255,80,0,false)=="/rgbPartition/setColor/5/255/20/0/false","Owner-accepted orange vector, all three channels and zone preserved");
        Check(LedColorTransform.Command(6,255,80,0,true)=="/rgbPartition/setColor/6/255/20/0/true","Breathing receives exactly one conversion");
        Check(LedColorTransform.Command(1,255,255,255,false)=="/rgbPartition/setColor/1/255/255/255/false","White unchanged");
        Check(LedColorTransform.Command(1,0,0,0,false)=="/rgbPartition/setColor/1/0/0/0/false","Black unchanged");
        Check(LedColorTransform.ToLinearByte(128)==55,"sRGB midpoint decoded, not a channel swap");
        int previous=-1;
        for(int i=0;i<256;i++){
            int current=LedColorTransform.ToLinearByte(i);
            Check(current>=0&&current<=255,"No byte overflow");
            Check(current>=previous,"No reversed intensity steps");
            Check(current<=i,"Intermediate component does not brighten the mixture");
            previous=current;
        }
        foreach(int invalid in new[]{-1,256}){
            bool rejected=false;try{LedColorTransform.ToLinearByte(invalid);}catch(ArgumentOutOfRangeException){rejected=true;}
            Check(rejected,"Out-of-range input rejected");
        }
        Console.WriteLine("{\"passed\":true,\"checks\":"+checks+",\"modelOnly\":true,\"hardwareWrites\":0}");
        return 0;
    }
}
