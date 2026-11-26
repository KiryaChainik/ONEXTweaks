// SPDX-License-Identifier: AGPL-3.0-only
using ONEXTweaks.Panel;
namespace ONEXTweaks.WidgetBridge;

internal interface IControllerOemRuntime
{
 bool Prepared{get;}
 object Read();
 Task Prepare();
 Task Release();
}

internal sealed class ControllerOemManager:IAsyncDisposable,IControllerOemRuntime
{
 private readonly ControllerOemRawInput input;private readonly PowerChannel power;private readonly Func<ControllerOemPolicy> policy;
 private readonly Action<string>? chordPressed;private readonly ControllerOemSystemActions actions;private readonly SemaphoreSlim gate=new(1,1);
 private ControllerOemChordHook? hook;private int active,prepared;private long preparedSession;private string? error;
 public bool Prepared=>Volatile.Read(ref prepared)==1&&OwnsPreparation(Volatile.Read(ref preparedSession),power.SessionGeneration);
 internal static bool OwnsPreparation(long preparedSession,long currentSession)=>preparedSession!=0&&preparedSession==currentSession;
 internal ControllerOemManager(ControllerOemRawInput source,PowerChannel channel,Func<ControllerOemPolicy> current,GameBarEntry entry,Func<Task>? switchMode=null,Func<string,ControllerBinding>? actionBinding=null,Func<Task>? secureAttention=null,Action<string>? knownChordPressed=null)
 {input=source;power=channel;policy=current;chordPressed=knownChordPressed;actions=new(source,current,()=>Volatile.Read(ref active)==1,entry,switchMode,actionBinding,secureAttention);if(input.KeyboardSwitchEnabled)EnsureHook();}
 private void EnsureHook(){hook??=new(input,()=>Volatile.Read(ref active)==1||input.KeyboardSwitchEnabled,()=>Volatile.Read(ref active)==1,()=>Volatile.Read(ref active)==0,chordPressed);}
 public object Read()=>new{prepared=Prepared,active=Volatile.Read(ref active)==1,preparedSession=Volatile.Read(ref preparedSession),powerSession=power.SessionGeneration,error,input=input.Read(),system=actions.Read(),physicalVerificationRequired=true};
 public async Task Prepare()
 {
  await gate.WaitAsync();bool hardwareRequested=false;
  try
  {
   if(Prepared)return;
   // The agent releases its hardware mappings when this pipe session ends.
   Volatile.Write(ref prepared,0);
   _=policy().Validate();
   for(int n=0;n<40&&!input.Listening;n++)await Task.Delay(50);
   if(!input.Listening)throw new IOException("The OEM input listener is unavailable; hardware assignments were not changed");
   if(input.HeldMask!=0)throw new IOException("Release all extra buttons before preparing their assignments");
   // The complete fresh OEM preset is required. The partial nine-row getter
   // response must never be used as an update/restore template.
   var snapshot=ControllerOemPresetReader.Read();
   EnsureHook();
   input.FirmwareAliases(true);Volatile.Write(ref active,1);
   hardwareRequested=true;var result=await power.PrepareControllerHardwareAsync(snapshot.Mapping1,snapshot.Mapping2,
    session=>Volatile.Write(ref preparedSession,session));
   if(!result.Ok||result.Controller?["prepared"]?.GetValue<bool>()!=true)throw new IOException(result.Error??"OEM mapping preparation was not acknowledged");
   Volatile.Write(ref prepared,1);error=null;
  }
  catch(Exception e)
  {
   error=e.Message;
   // Keep the listener/filter alive until the original Guide mapping returns.
   if(hardwareRequested)
   {
    try{var restored=await power.ReleaseControllerHardwareAsync();if(!restored.Ok)throw new IOException(restored.Error);}
    catch{Volatile.Write(ref prepared,1);throw new IOException("OEM restore is pending; the widget button listener remains active");}
   }
   Volatile.Write(ref prepared,0);Volatile.Write(ref active,0);input.FirmwareAliases(false);if(!input.KeyboardSwitchEnabled){hook?.Dispose();hook=null;}input.ReleaseSource("keyboard");input.ReleaseSource("vendor");
   throw;
  }
  finally{gate.Release();}
 }
 public async Task Release()
 {
  await gate.WaitAsync();
  try
  {
   if(Prepared){var result=await power.ReleaseControllerHardwareAsync();if(!result.Ok)throw new IOException(result.Error??"OEM mapping restore was not acknowledged");}
   Volatile.Write(ref prepared,0);Volatile.Write(ref active,0);input.FirmwareAliases(false);if(!input.KeyboardSwitchEnabled){hook?.Dispose();hook=null;}input.ReleaseSource("keyboard");input.ReleaseSource("vendor");
  }
  finally{gate.Release();}
 }
 public async ValueTask DisposeAsync(){try{await Release();}finally{hook?.Dispose();hook=null;actions.Dispose();gate.Dispose();}}
}
