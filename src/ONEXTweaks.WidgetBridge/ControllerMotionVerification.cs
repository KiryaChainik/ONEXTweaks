// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
namespace ONEXTweaks.WidgetBridge;
internal static class ControllerMotionVerification
{
 internal static async Task<int> Models(string file)
 {
  try{
var checks=new List<object>();int count=0;
void Check(bool value,string name){checks.Add(new{name,passed=value});count++;if(!value)throw new InvalidOperationException(name);}
GyroIdleCalibrationWindow Window(Func<int,MotionVector> value,int samples=200,int interval=10){var w=new GyroIdleCalibrationWindow();for(int n=0;n<samples;n++)w.Add(638000000000000000+n*interval*10000L,value(n));return w;}
var now=DateTimeOffset.Parse("2026-10-31T00:00:00Z");
for(int bias=-20;bias<=20;bias++){
 double x=bias/10.0;var w=Window(n=>new(x+(n%2==0?.02:-.02),-.15,.061));var r=w.Finish(now);
 Check(r.Passed,"stationary finite drift "+bias);
 Check(Math.Abs(r.Calibration!.Correct(new(x,-.15,.061)).X)<1e-12,"bias removed "+bias);
 Check(Math.Abs(r.Calibration.Correct(new(x+20,-.15,.061)).X-20)<1e-12,"actual rotation retained "+bias);
 Check(r.Calibration.CreatedUtc==now&&r.Calibration.Samples==200&&r.Calibration.DurationMilliseconds==1990,"time/sample evidence "+bias);
 var copy=JsonSerializer.Deserialize<GyroIdleCalibration>(JsonSerializer.Serialize(r.Calibration))!.Validate();Check(copy==r.Calibration,"calibration JSON roundtrip "+bias);
}
var repeat=new GyroIdleCalibrationWindow();for(int n=0;n<200;n++)repeat.Add(638000000000000000,new(.1,0,0));Check(!repeat.Finish(now).Passed&&repeat.Count==1&&repeat.DuplicateSamples==199,"one physical sample never becomes200 samples");
Check(!Window(_=>new(.1,0,0),99,20).Finish(now).Passed,"insufficient distinct samples rejected");
Check(!Window(_=>new(.1,0,0),100,10).Finish(now).Passed,"short measurement rejected");
Check(!Window(_=>new(.1,0,0),200,100).Finish(now).Passed,"overlong measurement rejected");
var pause=new GyroIdleCalibrationWindow();for(int n=0;n<200;n++)pause.Add(638000000000000000+n*100000L+(n>=100?3000000L:0),new(.1,0,0));Check(!pause.Finish(now).Passed,"sensor pause rejected");
var stale=Window(_=>new(.1,0,0));stale.Add(638000000000000001,new(.1,0,0));Check(!stale.Finish(now).Passed&&stale.StaleSamples==1,"out-of-order timestamps rejected");
foreach(double bad in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity}){var badWindow=Window(_=>new(.1,0,0));badWindow.Add(638000000002100000,new(bad,0,0));Check(!badWindow.Finish(now).Passed,"nonfinite gyro rejected "+bad);}
Check(!Window(n=>new(n%2==0?2:-2,0,0)).Finish(now).Passed,"jitter rejected");
Check(!Window(_=>new(2.01,0,0)).Finish(now).Passed,"steady rotation outside idle range rejected");
Check(!Window(n=>new(n==100?5.01:0,0,0)).Finish(now).Passed,"single strong movement rejected");
var stillGravity=Enumerable.Repeat(new MotionVector(.2,.3,Math.Sqrt(.87)),200).ToArray();
Check(Window(_=>new(.1,0,0)).Finish(now,stillGravity).Passed,"gravity allowed at arbitrary still orientation");
Check(!Window(_=>new(.1,0,0)).Finish(now,stillGravity.Take(49).ToArray()).Passed,"insufficient acceleration evidence rejected");
Check(!Window(_=>new(.1,0,0)).Finish(now,Enumerable.Repeat(new MotionVector(0,0,0),200).ToArray()).Passed,"freefall not accepted as stationary");
Check(!Window(_=>new(.1,0,0)).Finish(now,Enumerable.Repeat(new MotionVector(0,0,1.21),200).ToArray()).Passed,"acceleration outside gravity range rejected");
Check(!Window(_=>new(.1,0,0)).Finish(now,Enumerable.Range(0,200).Select(n=>new MotionVector(n%2==0?.1:-.1,0,1)).ToArray()).Passed,"tilt jitter rejected");
var overflow=new GyroIdleCalibrationWindow();for(int n=0;n<4097;n++)overflow.Add(638000000000000000+n*10000L,new(0,0,0));Check(!overflow.Finish(now).Passed,"bounded calibration memory");

long begin=638000000000000000,end=begin+30000000;
var acceleration=Enumerable.Range(0,190).Select(n=>new MotionObservation(begin+n*158000,new(0,0,1))).ToArray();
var sparse=new GyroIdleCalibrationWindow();sparse.Add(begin+10000,new(.183,-.061,.122));var accepted=sparse.FinishOnChange(begin,end,now,acceleration);
Check(accepted.Passed&&accepted.Calibration!.Samples==1&&accepted.Calibration.Method=="OnChange"&&accepted.Calibration.AccelerometerSamples==190,"one actual fresh gyro sample retains truthful count with continuous stable acceleration");
Check(Math.Abs(accepted.Calibration!.Correct(new(.183,-.061,.122)).Length)<1e-12,"sparse idle offset correction");
Check(JsonSerializer.Deserialize<GyroIdleCalibration>(JsonSerializer.Serialize(accepted.Calibration))!.Validate()==accepted.Calibration,"on-change calibration persistence");
var oldSample=new GyroIdleCalibrationWindow();oldSample.Add(begin-3000000,new(0,0,0));Check(!oldSample.FinishOnChange(begin,end,now,acceleration).Passed,"cached old gyro never accepted");
Check(!new GyroIdleCalibrationWindow().FinishOnChange(begin,end,now,acceleration).Passed,"missing gyro rejected in sparse mode");
Check(!sparse.FinishOnChange(begin,begin+20000000,now,acceleration).Passed,"sparse calibration needs actual observation duration");
Check(!sparse.FinishOnChange(begin,end,now,acceleration.Take(99).ToArray()).Passed,"sparse gyro needs100 actual acceleration samples");
Check(!sparse.FinishOnChange(begin,end,now,acceleration.Take(140).ToArray()).Passed,"acceleration must remain fresh until observation ends");
Check(!sparse.FinishOnChange(begin,end,now,acceleration.Select(v=>v with{Timestamp=begin}).ToArray()).Passed,"replayed acceleration does not prove stillness");
Check(!sparse.FinishOnChange(begin,end,now,acceleration.Select((v,n)=>v with{Value=new(n%2==0?.1:-.1,0,1)}).ToArray()).Passed,"movement aborts sparse correction");
Check(!sparse.FinishOnChange(begin,end,now,acceleration.Select(v=>v with{Value=new(0,0,0)}).ToArray()).Passed,"freefall aborts sparse correction");
Check(!sparse.FinishOnChange(begin,end,now,acceleration.Select(v=>v with{Value=new(double.NaN,0,1)}).ToArray()).Passed,"nonfinite acceleration aborts sparse correction");
bool rejected=false;try{new GyroIdleCalibration(1,now,0,0,0,1,3000).Validate();}catch(InvalidDataException){rejected=true;}Check(rejected,"single sample never claims continuous mode");

string owned=Path.Combine(Path.GetDirectoryName(file)!,"motion-model-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(owned);string path=Path.Combine(owned,"calibration.json");
try{
 var actualStart=638000000000000000L;
 ControllerMotionCapture capture=new(true,true,16,actualStart,actualStart+30000000,[new(actualStart+10000,new(.18,-.06,.12))],Enumerable.Range(0,190).Select(n=>new MotionObservation(actualStart+n*158000,new(0,0,1))).ToArray(),0,true);
 var gate=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);bool wait=true;
 await using(var motion=new ControllerMotion(path,async token=>{if(wait)await gate.Task.WaitAsync(token);return capture;})){
  Check(!File.Exists(path)&&motion.Read().Calibration==null,"Read/startup leave owner settings absent");
  motion.Begin("motionCheck");Check(motion.Read().Busy&&!File.Exists(path),"Sensor action is asynchronous");
  bool busy=false;try{motion.Begin("motionReset");}catch(InvalidOperationException){busy=true;}Check(busy,"Reset cannot race current calibration");
  wait=false;gate.SetResult(true);await motion.Completion;Check(!File.Exists(path)&&motion.Read().Error==null,"Check sensors never writes correction");
  motion.Begin("motionCalibrate");await motion.Completion;Check(File.Exists(path)&&motion.Read().Calibration is{Samples:1,Method:"OnChange",AccelerometerSamples:190},"Calibrate atomically persists real count");
  byte[] saved=File.ReadAllBytes(path);capture=capture with{Gyro=[new(actualStart+10000,new(50,0,0))]};
  motion.Begin("motionCalibrate");await motion.Completion;Check(motion.Read().Error!=null&&File.ReadAllBytes(path).SequenceEqual(saved)&&motion.Read().Calibration!=null,"Rejected motion preserves previous correction");
  capture=capture with{ErrorHResult=unchecked((int)0x80070005)};motion.Begin("motionCalibrate");await motion.Completion;Check(File.ReadAllBytes(path).SequenceEqual(saved),"Sensor failure preserves saved correction");
 }
 byte[] original=File.ReadAllBytes(path);
 await using(var loaded=new ControllerMotion(path,async token=>{await Task.Delay(Timeout.Infinite,token);return capture;})){
  Check(loaded.Read().Calibration is{Samples:1,Method:"OnChange"},"Reload retains correction metadata");
  loaded.Begin("motionCalibrate");
 }
 Check(File.ReadAllBytes(path).SequenceEqual(original),"Cancellation never replaces correction");
 await using(var reset=new ControllerMotion(path,_=>Task.FromResult(capture))){reset.Begin("motionReset");Check(!File.Exists(path)&&reset.Read().Calibration==null,"Scoped reset affects only own correction");}
 File.WriteAllText(path,"invalid own fixture");await using(var invalid=new ControllerMotion(path,_=>Task.FromResult(capture))){Check(invalid.Read().Calibration==null&&invalid.Read().Error!=null&&File.ReadAllText(path)=="invalid own fixture","Unreadable file retained unchanged");}
}finally{Directory.Delete(owned,true);}

   await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=true,checks=count,modelOnly=true,realSensorDataNotClaimed=true,ownerSettingsWrites=0},Backend.Program.Json));return 0;
  }catch(Exception e){await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{passed=false,modelOnly=true,error=e.Message},Backend.Program.Json));return 2;}
 }
 internal static async Task<int> Native(string report)
 {
  int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new IOException(why);}
  string root=Path.Combine(Path.GetDirectoryName(report)!,"motion318-owned-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);string file=Path.Combine(root,"calibration.json");
  object? readback=null;
  try{
   await using(var motion=new ControllerMotion(file)){
    Check(!File.Exists(file)&&motion.Read().Calibration==null,"Startup created calibration file");
    motion.Begin("motionCheck");Check(motion.Read().Busy,"Sensor action did not return busy immediately");await motion.Completion;
    var sensors=motion.Read();Check(sensors is{GyroAvailable:true,AccelerometerAvailable:true,TemporaryParametersRestored:true,Error:null}&&sensors.GyroSamples>=1&&sensors.AccelerometerSamples>=100,"Actual Windows sensors unavailable");Check(!File.Exists(file),"Check sensors changed saved calibration");
    motion.Begin("motionCalibrate");await motion.Completion;readback=motion.Read();Check(motion.Read().Error==null&&motion.Read().Calibration!=null&&File.Exists(file),"Real quiet calibration rejected: "+motion.Read().Status);
    Check(motion.Read().Calibration!.Samples==motion.Read().GyroSamples&&motion.Read().Calibration!.AccelerometerSamples==motion.Read().AccelerometerSamples,"Calibration did not retain truthful sample counts");
   }
   byte[] before=File.ReadAllBytes(file);
   await using(var motion=new ControllerMotion(file)){
    Check(motion.Read().Calibration!=null,"Actual gyro correction did not reload");
    motion.Begin("motionCalibrate");await Task.Delay(80);
   }
   Check(File.ReadAllBytes(file).SequenceEqual(before),"Cancelled calibration replaced current correction");
   await using(var motion=new ControllerMotion(file)){motion.Begin("motionReset");Check(!File.Exists(file)&&motion.Read().Calibration==null,"Scoped correction reset failed");}
   await File.WriteAllTextAsync(report,JsonSerializer.Serialize(new{passed=true,checks,actualWindowsSensors=true,actualCalibrationInOwnedTemporaryFile=true,ownerSettingsWrites=0,firmwareWrites=0,readback},new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true}));return 0;
  }catch(Exception e){await File.WriteAllTextAsync(report,JsonSerializer.Serialize(new{passed=false,checks,error=e.Message,readback},new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true}));return 2;}
  finally{Directory.Delete(root,true);}
 }
}
