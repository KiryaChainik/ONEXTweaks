// SPDX-License-Identifier: AGPL-3.0-only
namespace ONEXTweaks.WidgetBridge;
internal sealed record ControllerVibration(int Intensity=100,string Profile="Balanced",bool SwapMotors=false)
{
 internal static readonly string[] Profiles=["Balanced","Sharp","Soft","Impact","Boosted"];
 internal ControllerVibration Validate(){if(Intensity is <0 or >100||!Profiles.Contains(Profile,StringComparer.Ordinal))throw new InvalidDataException("Choose a valid vibration profile and intensity0–100%");return this;}
 internal (byte Large,byte Small) Scale(byte large,byte small)
 {
  // Portable response gains. Balanced retains the original exact integer scaling.
  (int L,int S) gains=Profile switch{"Sharp"=>(80,120),"Soft"=>(65,65),"Impact"=>(120,80),"Boosted"=>(125,125),_=>(100,100)};
  int l=Math.Min(255,(large*gains.L+50)/100),s=Math.Min(255,(small*gains.S+50)/100);if(SwapMotors)(l,s)=(s,l);
  return((byte)Math.Clamp((l*Intensity+50)/100,0,255),(byte)Math.Clamp((s*Intensity+50)/100,0,255));
 }
}
