// SPDX-License-Identifier: AGPL-3.0-only
namespace ONEXTweaks.WidgetBridge;
/// <summary>Explicit gyro-to-stick response. Disabled/absent retains the legacy180°/s mapping.</summary>
internal sealed record ControllerGyroStickCurve(bool Enabled=false,int Sensitivity=100,int Deadzone=2,int MinInput=0,int MaxInput=220,int MinOutput=0,int MaxOutput=100,int Power=100,int PrecisionSpeed=0,int OutputMix=0)
{
 internal ControllerGyroStickCurve Validate(){
  if(Sensitivity is <1 or >400||Deadzone is <0 or >50||MinInput is <0 or >100||MaxInput is <50 or >720||MaxInput<=Math.Max(Deadzone,MinInput)||MinOutput is <0 or >100||MaxOutput is <1 or >100||MinOutput>MaxOutput||Power is <10 or >400||PrecisionSpeed is <0 or >100||OutputMix is <-100 or >100)throw new InvalidDataException("Unsupported gyro stick response range");
  return this;
 }
 internal (double X,double Y) Map(double horizontal,double vertical){
  if(!double.IsFinite(horizontal)||!double.IsFinite(vertical))return default;
  // Bias suppresses the other axis, without amplifying the chosen axis.
  double x=horizontal*(OutputMix<0?1+OutputMix/100.0:1),y=vertical*(OutputMix>0?1-OutputMix/100.0:1);
  double speed=Math.Sqrt(x*x+y*y),lower=Math.Max(Deadzone,MinInput);if(speed<=lower)return default;
  double t=Math.Clamp((speed-lower)*Sensitivity/100.0/(MaxInput-lower),0,1);
  if(PrecisionSpeed>0)t*=Math.Clamp((speed-lower)/PrecisionSpeed,0,1);
  double radius=(MinOutput+(MaxOutput-MinOutput)*Math.Pow(t,Power/100.0))/100*32767;
  return(x/speed*radius,y/speed*radius);
 }
}
