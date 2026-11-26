// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Core;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  // Diagnostic-only, UI-thread writer. Allocate once before timed navigation;
  // no JsonObject, file IO, labels, stack or heap contents during measurement.
  private struct OnexNavigationTracePoint
  {internal long Started,Finished;internal string Kind,Key,Tab;internal int Gc0,Gc1,Gc2,Lap;internal bool Warm;}
  private OnexNavigationTracePoint[] _onexNavigationTrace;
  private int _onexNavigationTraceCount,_onexNavigationTraceDropped,_onexNavigationTraceLap;
  private long _onexNavigationTraceOrigin;private DateTimeOffset _onexNavigationTraceUtc;
  private string _onexNavigationTraceTab;private bool _onexNavigationTraceWarm;
  private void BeginOnexNavigationCorrelation()
  {
   _onexNavigationTrace=new OnexNavigationTracePoint[2048];_onexNavigationTraceCount=_onexNavigationTraceDropped=0;
   _onexNavigationTraceUtc=DateTimeOffset.UtcNow;_onexNavigationTraceOrigin=Stopwatch.GetTimestamp();
  }
  private void RecordOnexNavigationPoint(string kind,string key=null,long started=0)
  {
   if(!_onexNavigationPerfCapture||_onexNavigationTrace==null)return;
   if(_onexNavigationTraceCount==_onexNavigationTrace.Length){_onexNavigationTraceDropped++;return;}
   long now=Stopwatch.GetTimestamp();_onexNavigationTrace[_onexNavigationTraceCount++]=new OnexNavigationTracePoint{Started=started==0?now:started,Finished=now,Kind=kind,Key=key,Tab=_onexNavigationTraceTab,Lap=_onexNavigationTraceLap,Warm=_onexNavigationTraceWarm,Gc0=GC.CollectionCount(0),Gc1=GC.CollectionCount(1),Gc2=GC.CollectionCount(2)};
  }
  private async Task ProbeOnexNormalDispatcherAsync(CoreDispatcher dispatcher,CancellationToken token)
  {
   try
   {
    while(!token.IsCancellationRequested)
    {
     await Task.Delay(25,token).ConfigureAwait(false);if(token.IsCancellationRequested)break;
     // Measure from actual submission, excluding delay on the worker thread.
     long submitted=Stopwatch.GetTimestamp();
     await dispatcher.RunAsync(CoreDispatcherPriority.Normal,()=>RecordOnexNavigationPoint("normal-probe",null,submitted));
    }
   }
   catch(OperationCanceledException)when(token.IsCancellationRequested){}
  }
  private void WriteOnexNavigationCorrelation(JsonObject report)
  {
   var points=_onexNavigationTrace;if(points==null)return;
   var rows=new JsonArray();double scale=1000d/Stopwatch.Frequency;
   for(int index=0;index<_onexNavigationTraceCount;index++)
   {
    var point=points[index];rows.Add(new JsonObject{{"startMs",JsonValue.CreateNumberValue((point.Started-_onexNavigationTraceOrigin)*scale)},{"endMs",JsonValue.CreateNumberValue((point.Finished-_onexNavigationTraceOrigin)*scale)},{"kind",JsonValue.CreateStringValue(point.Kind)},{"key",JsonValue.CreateStringValue(point.Key??"")},{"tab",JsonValue.CreateStringValue(point.Tab??"")},{"lap",JsonValue.CreateNumberValue(point.Lap)},{"warm",JsonValue.CreateBooleanValue(point.Warm)},{"gc0",JsonValue.CreateNumberValue(point.Gc0)},{"gc1",JsonValue.CreateNumberValue(point.Gc1)},{"gc2",JsonValue.CreateNumberValue(point.Gc2)}});
   }
   report["correlationUtcOrigin"]=JsonValue.CreateStringValue(_onexNavigationTraceUtc.ToString("o"));report["correlationPoints"]=rows;report["correlationDropped"]=JsonValue.CreateNumberValue(_onexNavigationTraceDropped);
   report["correlationAllocatedFixedBufferBeforeTiming"]=JsonValue.CreateBooleanValue(true);report["correlationContainsOwnerTextOrHeap"]=JsonValue.CreateBooleanValue(false);
   _onexNavigationTrace=null;
  }
 }
}
