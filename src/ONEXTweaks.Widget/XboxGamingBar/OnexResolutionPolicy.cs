// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
namespace XboxGamingBar
{
    internal static class OnexResolutionPolicy
    {
        internal static readonly string[] TapOrder={"1920x1200","1680x1050","1440x900","1280x800"};
        internal static string Key(int width,int height)=>Math.Max(width,height)+"x"+Math.Min(width,height);
        internal static string Next(int width,int height,IEnumerable<string> supported)
        {
            var available=new HashSet<string>(supported??new string[0]);int current=Array.IndexOf(TapOrder,Key(width,height));
            for(int step=1;step<=TapOrder.Length;step++){string next=TapOrder[(current+step)%TapOrder.Length];if(available.Contains(next))return next;}
            throw new InvalidOperationException("Windows does not report any of the four quick resolutions for this display.");
        }
    }
}
