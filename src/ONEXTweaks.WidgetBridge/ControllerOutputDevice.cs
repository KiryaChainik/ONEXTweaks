// SPDX-License-Identifier: AGPL-3.0-only
namespace ONEXTweaks.WidgetBridge;
/// <summary>One owned output transport. Xbox retains its accepted XInput identity path; DS4 uses a feature nonce.</summary>
internal sealed class ControllerOutputDevice:IDisposable
{
 private readonly ControllerVirtualPad? xbox;private readonly ControllerViiperDs4Device? ds4;private readonly ControllerRumbleForwarder? rumble;private readonly ControllerGameLighting? lighting;
 private readonly object gate=new();private ControllerFrame last;private bool guide,mappedGuide,touchpadClick,disposed;private ControllerDs4Motion? motion;private int probeFeedback,rgbFeedback;
 private Ds4TouchContacts touchpadContacts;
 internal string Target{get;}internal int Slot=>xbox?.Slot??-1;internal string? Serial=>ds4?.Serial;
 internal int RgbFeedbackCount=>Volatile.Read(ref rgbFeedback);
 internal int ProbeFeedbackCount=>xbox?.ProbeFeedbackCount??Volatile.Read(ref probeFeedback);
 internal ControllerOutputDevice(int source,Func<ControllerVibration> vibration,string target="Xbox360",Func<ControllerGameLightingRequest,Task<ControllerGameLightingReply>>? gameLighting=null){
  Target=target;if(target=="Xbox360"){xbox=new(source,false,vibration:vibration);return;}
  if(target!="DualShock4")throw new InvalidDataException("This virtual controller target has not been verified");
  if(gameLighting!=null)lighting=new(gameLighting);rumble=new(vibration,(large,small)=>ControllerNative.RumbleChecked(source,large,small));
  try{ds4=new();ds4.Feedback+=feedback=>{if((feedback.Flags&2)!=0){Interlocked.Increment(ref rgbFeedback);lighting?.Color(feedback.Red,feedback.Green,feedback.Blue);}if((feedback.Flags&1)==0)return;if(feedback.Large==1&&feedback.Small==0)Interlocked.Increment(ref probeFeedback);rumble!.Feedback(feedback.Large,feedback.Small);};}
  catch{lighting?.Dispose();rumble.Dispose();throw;}
 }
 internal void ConfigureLighting(bool enabled,bool paused,string context)=>lighting?.Configure(enabled,paused,context);
 internal string LightingStatus=>Target=="Xbox360"?"Requires DualShock 4":lighting?.Status??"Requires updated hardware helper";
 internal void EnableRumbleForwarding(){if(xbox!=null)xbox.EnableRumbleForwarding();else rumble!.Enable();}
 internal void RefreshVibration(){if(xbox!=null)xbox.RefreshVibration();else rumble!.Refresh();}
 internal Task<(byte Large,byte Small)> TestVibration(ControllerVibration settings)=>xbox?.TestVibration(settings)??rumble!.Test(settings);
 internal void Submit(ControllerFrame frame,bool mappingGuide=false,bool touchClick=false,ControllerDs4Motion? imu=null,bool skipDuplicates=false,Ds4TouchContacts contacts=Ds4TouchContacts.None)
 {
  lock(gate)
  {
   ObjectDisposedException.ThrowIf(disposed,this);
   if(skipDuplicates&&last==frame&&mappedGuide==mappingGuide&&touchpadClick==touchClick&&touchpadContacts==contacts&&motion==imu)return;
   last=frame;mappedGuide=mappingGuide;touchpadClick=touchClick;touchpadContacts=contacts;motion=imu;
   if(xbox!=null)xbox.Submit(frame,mappingGuide);else Send();
  }
 }
 internal void Guide(bool pressed){lock(gate){ObjectDisposedException.ThrowIf(disposed,this);guide=pressed;if(xbox!=null)xbox.Guide(pressed);else Send();}}
 private void Send()=>ds4!.Submit(ControllerViiperDs4Device.Encode(last,guide||mappedGuide,touchpadClick,motion,touchpadContacts));
 public void Dispose(){lock(gate){if(disposed)return;disposed=true;if(xbox!=null){xbox.Dispose();return;}lighting?.Dispose();rumble!.Dispose();string serial=ds4!.Serial;ds4.Dispose();for(int n=0;n<150;n++){if(!ControllerDs4HidReader.IsOwnedPresent(serial).GetAwaiter().GetResult())return;Thread.Sleep(50);}throw new IOException("The owned DS4 HID output did not disappear after removal");}}
}
