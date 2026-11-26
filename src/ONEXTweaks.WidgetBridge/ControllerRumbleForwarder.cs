// SPDX-License-Identifier: AGPL-3.0-only
namespace ONEXTweaks.WidgetBridge;
// Feedback and a bounded UI pulse share one physical sink. Stop never waits for
// a native callback while holding its lock; USB teardown may drain callbacks.
internal sealed class ControllerRumbleForwarder:IDisposable
{
 private readonly object gate=new();private readonly SemaphoreSlim tests=new(1,1);private readonly CancellationTokenSource lifetime=new();
 private readonly Func<ControllerVibration> settings;private readonly Action<byte,byte> sink;
 private byte latestLarge,latestSmall;private bool enabled,testing,disposed;
 internal ControllerRumbleForwarder(Func<ControllerVibration> current,Action<byte,byte> output){settings=current;sink=output;}
 internal void Enable(){lock(gate){ObjectDisposedException.ThrowIf(disposed,this);enabled=true;}}
 internal void Feedback(byte large,byte small){lock(gate){if(disposed)return;latestLarge=large;latestSmall=small;if(enabled&&!testing){var scaled=settings().Scale(large,small);sink(scaled.Large,scaled.Small);}}}
 internal void Refresh(){lock(gate){if(!disposed&&enabled&&!testing){var scaled=settings().Scale(latestLarge,latestSmall);sink(scaled.Large,scaled.Small);}}}
 internal async Task<(byte Large,byte Small)> Test(ControllerVibration value,int durationMs=400)
 {
  value.Validate();if(durationMs is <20 or >1000)throw new InvalidDataException("Vibration test duration is outside its supported range");await tests.WaitAsync();bool started=false,completed=false;var pulse=value.Scale(255,255);
  try
  {
   lock(gate){ObjectDisposedException.ThrowIf(disposed,this);testing=true;started=true;sink(pulse.Large,pulse.Small);}
   await Task.Delay(durationMs,lifetime.Token);completed=true;return pulse;
  }
  finally
  {
   try{lock(gate){if(started){testing=false;if(!disposed){try{var restore=enabled?settings().Scale(latestLarge,latestSmall):(Large:(byte)0,Small:(byte)0);sink(restore.Large,restore.Small);}catch(Exception error){try{sink(0,0);}catch{}if(completed)throw new IOException("The controller did not restore vibration after the test",error);}}}}}finally{tests.Release();}
  }
 }
 public void Dispose(){lock(gate){if(disposed)return;disposed=true;enabled=false;testing=false;try{sink(0,0);}catch{}}lifetime.Cancel();}
}
internal sealed record ControllerVibrationTestResult(int Intensity,int DurationMs,int Large,int Small,string Route,bool ApiSucceeded=true);
