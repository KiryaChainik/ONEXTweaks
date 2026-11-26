// SPDX-License-Identifier: AGPL-3.0-only
namespace ONEXTweaks.WidgetBridge;
// Device-wide idle-rate correction, in degrees/second. Gravity is never subtracted.
internal readonly record struct MotionVector(double X,double Y,double Z)
{
 internal bool Finite=>double.IsFinite(X)&&double.IsFinite(Y)&&double.IsFinite(Z);
 internal double Maximum=>Math.Max(Math.Abs(X),Math.Max(Math.Abs(Y),Math.Abs(Z)));
 internal double Length=>Math.Sqrt(X*X+Y*Y+Z*Z);
 internal MotionVector Minus(MotionVector other)=>new(X-other.X,Y-other.Y,Z-other.Z);
}
internal readonly record struct MotionObservation(long Timestamp,MotionVector Value);
internal sealed record GyroIdleCalibration(int Schema,DateTimeOffset CreatedUtc,double X,double Y,double Z,int Samples,double DurationMilliseconds,string Method="Continuous",int AccelerometerSamples=0)
{
 internal GyroIdleCalibration Validate()
 {
  if(Schema!=1||Method is not("Continuous" or "OnChange")||Samples<(Method=="OnChange"?1:100)||Samples>4096||Method=="OnChange"&&AccelerometerSamples<100||!double.IsFinite(DurationMilliseconds)||DurationMilliseconds<1500||DurationMilliseconds>10000||!new MotionVector(X,Y,Z).Finite||new MotionVector(X,Y,Z).Maximum>2)
   throw new InvalidDataException("Invalid gyro idle calibration");
  return this;
 }
 internal MotionVector Correct(MotionVector raw)
 {
  if(!raw.Finite)throw new InvalidDataException("Invalid gyro sample");
  return raw.Minus(new(X,Y,Z));
 }
}
internal sealed record GyroCalibrationResult(bool Passed,string Reason,GyroIdleCalibration? Calibration,int DistinctSamples,double DurationMilliseconds,MotionVector Deviation);
internal sealed class GyroIdleCalibrationWindow
{
 private readonly List<(long Stamp,MotionVector Value)> rows=[];
 private bool invalid;private int duplicateSamples,staleSamples;
 internal int Count=>rows.Count;
 internal int DuplicateSamples=>duplicateSamples;
 internal int StaleSamples=>staleSamples;
 internal void Add(long timestampTicks,MotionVector velocity)
 {
  if(timestampTicks<=0||!velocity.Finite){invalid=true;return;}
  if(rows.Count>0&&timestampTicks<=rows[^1].Stamp){if(timestampTicks==rows[^1].Stamp)duplicateSamples++;else{staleSamples++;invalid=true;}return;}
  if(rows.Count>=4096){invalid=true;return;}rows.Add((timestampTicks,velocity));
 }
 // An on-change sensor can hold one genuine fresh reading while still. A
 // continuously advancing, stable accelerometer establishes the observation
 // window. The stored count remains the real count (including a count of one).
 internal GyroCalibrationResult FinishOnChange(long start,long end,DateTimeOffset now,IReadOnlyList<MotionObservation> acceleration)
 {
  double duration=(end-start)/10000.0;
  GyroCalibrationResult Fail(string why,MotionVector deviation=default)=>new(false,why,null,rows.Count,duration,deviation);
  if(invalid||start<=0||end<=start||duration is <2500 or >10000)return Fail("Invalid calibration observation window");
  if(rows.Count<1||rows[0].Stamp<start-2500000||rows[^1].Stamp>end+500000)return Fail("No fresh gyro reading in this calibration");
  if(acceleration.Count<100||acceleration.Any(v=>!v.Value.Finite||v.Value.Length is <.8 or >1.2))return Fail("Keep the console still on a stable surface");
  if(acceleration[0].Timestamp<start-2500000||acceleration[0].Timestamp>start+5000000||acceleration[^1].Timestamp<end-2500000||acceleration[^1].Timestamp>end+500000||acceleration[^1].Timestamp-acceleration[0].Timestamp<15000000)return Fail("Fresh acceleration did not cover calibration");
  if(acceleration.Zip(acceleration.Skip(1),(a,b)=>b.Timestamp-a.Timestamp).Any(gap=>gap<=0||gap>2500000))return Fail("Acceleration stream paused or repeated");
  var gravity=new MotionVector(acceleration.Average(v=>v.Value.X),acceleration.Average(v=>v.Value.Y),acceleration.Average(v=>v.Value.Z));
  double jitter=Math.Sqrt(acceleration.Average(v=>Math.Pow(v.Value.X-gravity.X,2)+Math.Pow(v.Value.Y-gravity.Y,2)+Math.Pow(v.Value.Z-gravity.Z,2)));
  var mean=new MotionVector(rows.Average(v=>v.Value.X),rows.Average(v=>v.Value.Y),rows.Average(v=>v.Value.Z));
  var deviation=new MotionVector(Math.Sqrt(rows.Average(v=>Math.Pow(v.Value.X-mean.X,2))),Math.Sqrt(rows.Average(v=>Math.Pow(v.Value.Y-mean.Y,2))),Math.Sqrt(rows.Average(v=>Math.Pow(v.Value.Z-mean.Z,2))));
  if(jitter>.03||mean.Maximum>2||deviation.Maximum>.5||rows.Any(v=>v.Value.Maximum>5))return Fail("Movement detected; keep the console still and repeat",deviation);
  var calibration=new GyroIdleCalibration(1,now,mean.X,mean.Y,mean.Z,rows.Count,duration,"OnChange",acceleration.Count).Validate();
  return new(true,"Idle drift measured",calibration,rows.Count,duration,deviation);
 }

 internal GyroCalibrationResult Finish(DateTimeOffset now,IReadOnlyList<MotionVector>? acceleration=null)
 {
  double duration=rows.Count<2?0:(rows[^1].Stamp-rows[0].Stamp)/10000.0;
  GyroCalibrationResult Fail(string why,MotionVector deviation=default)=>new(false,why,null,rows.Count,duration,deviation);
  if(invalid)return Fail("Invalid or out-of-order sensor data");
  if(rows.Count<100||duration<1500)return Fail("Not enough fresh gyro samples");
  if(duration>10000||rows.Zip(rows.Skip(1),(a,b)=>(b.Stamp-a.Stamp)/10000.0).Any(gap=>gap>250))return Fail("The gyro stream paused; repeat calibration");
  var mean=new MotionVector(rows.Average(v=>v.Value.X),rows.Average(v=>v.Value.Y),rows.Average(v=>v.Value.Z));
  var deviation=new MotionVector(Math.Sqrt(rows.Average(v=>Math.Pow(v.Value.X-mean.X,2))),Math.Sqrt(rows.Average(v=>Math.Pow(v.Value.Y-mean.Y,2))),Math.Sqrt(rows.Average(v=>Math.Pow(v.Value.Z-mean.Z,2))));
  if(mean.Maximum>2||deviation.Maximum>.5||rows.Any(v=>v.Value.Maximum>5))return Fail("Keep the console still and repeat calibration",deviation);
  if(acceleration!=null&&(acceleration.Count<50||acceleration.Any(v=>!v.Finite||v.Length is <.8 or >1.2)))return Fail("Keep the console still on a stable surface",deviation);
  if(acceleration!=null){
   var average=new MotionVector(acceleration.Average(v=>v.X),acceleration.Average(v=>v.Y),acceleration.Average(v=>v.Z));
   double jitter=Math.Sqrt(acceleration.Average(v=>Math.Pow(v.X-average.X,2)+Math.Pow(v.Y-average.Y,2)+Math.Pow(v.Z-average.Z,2)));
   if(jitter>.03)return Fail("Movement detected; repeat calibration",deviation);
  }
  var calibration=new GyroIdleCalibration(1,now,mean.X,mean.Y,mean.Z,rows.Count,duration).Validate();
  return new(true,"Idle drift measured",calibration,rows.Count,duration,deviation);
 }
}
