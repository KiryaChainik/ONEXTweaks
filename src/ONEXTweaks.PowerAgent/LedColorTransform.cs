using System;
using System.Globalization;

namespace ONEXTweaks.Power
{
    internal static class LedColorTransform
    {
        // The picker/history stores display sRGB. Decode only at the physical RGB boundary;
        // never save PWM bytes back as the user's selected color. Pure primaries stay exact.
        // On this ONEXPLAYER, the owner compared raw FF5000 (pale) with FF1400 (correct).
        // This is a perceptual correction, not a measured LED gamut/white-point calibration.
        internal static int ToLinearByte(int channel)
        {
            if(channel<0||channel>255)throw new ArgumentOutOfRangeException("channel");
            double value=channel/255.0;
            double linear=value<=0.04045?value/12.92:Math.Pow((value+0.055)/1.055,2.4);
            return (int)Math.Round(linear*255,MidpointRounding.AwayFromZero);
        }

        internal static string Command(int id,int r,int g,int b,bool breathing)
        {
            return CommandRaw(id,new[]{ToLinearByte(r),ToLinearByte(g),ToLinearByte(b)},breathing);
        }
        internal static string CommandRaw(int id,int[] rgb,bool breathing)
        {
            if(rgb==null||rgb.Length!=3)throw new ArgumentException("Three RGB bytes required");
            foreach(int channel in rgb)if(channel<0||channel>255)throw new ArgumentOutOfRangeException("rgb");
            return string.Format(CultureInfo.InvariantCulture,"/rgbPartition/setColor/{0}/{1}/{2}/{3}/{4}",
                id,rgb[0],rgb[1],rgb[2],breathing?"true":"false");
        }
    }
}
