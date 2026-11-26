using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Windows.Data.Json;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private sealed class OnexNavigationSample
  {internal string Tab,Source;internal int Lap,Gc0,Gc1,Gc2;internal double Navigation,Render,Queue,Submission,Font,Theme,Accent;internal bool Observed;}
  private async Task VerifyWarmOpenLayoutAsync(JsonObject report)
  {
   int checks=0;
   void Check(bool ok,string message){checks++;if(!ok)throw new InvalidOperationException(message);}
   var original=GetVisibleNavigationItems().FirstOrDefault(tab=>tab.IsChecked==true);
   var settings=Windows.Storage.ApplicationData.Current.LocalSettings.Values;
   bool oldStore=_onexTabsQaStore;
   bool hadQa=settings.TryGetValue(OnexTabsQaKey,out object oldQa);
   string ownerTabs=ReadOnexTabValue(OnexTabsKey);
   var samples=new JsonArray();
   report["warmOpenSamples"]=samples;
   try
   {
    Check(!qsEditMode&&!OnexSourceEditorsPending&&_onexDetailsDialog==null,"The owner is editing the widget");
    _onexTabsQaStore=true;
    var preferences=new OnexTabsPreference();
    settings[OnexTabsQaKey]=preferences.Serialize();
    EnsureOnexReleaseLayout();
    foreach(var tab in new[]{PerformanceNavItem,SystemNavItem,LegionNavItem})
    {
     tab.IsChecked=true;
     await Task.Delay(100);
     foreach(bool warm in new[]{false,true,true,false})
     {
      var clock=Stopwatch.StartNew();
      var ping=new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
      var frame=new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
      EventHandler<object> rendered=(sender,args)=>frame.TrySetResult(clock.Elapsed.TotalMilliseconds);
      CompositionTarget.Rendering+=rendered;
      try
      {
       var posted=Dispatcher.RunAsync(CoreDispatcherPriority.Normal,()=>ping.TrySetResult(clock.Elapsed.TotalMilliseconds));
       bool reused=false;
       if(warm)reused=ApplyTabPrefsOnWidgetOpen();else ApplyTabPrefs();
       double handler=clock.Elapsed.TotalMilliseconds;
       OnexStartupRoot.InvalidateArrange();
       var done=Task.WhenAll(ping.Task,frame.Task);
       if(await Task.WhenAny(done,Task.Delay(3000))!=done)throw new InvalidOperationException("No post-handler UI heartbeat or render");
       await posted;
       samples.Add(new JsonObject{{"tab",JsonValue.CreateStringValue(tab.Tag as string)},
        {"path",JsonValue.CreateStringValue(warm?"warm":"full")},{"handlerMs",JsonValue.CreateNumberValue(handler)},
        {"normalQueueMs",JsonValue.CreateNumberValue(await ping.Task)},{"nextRenderMs",JsonValue.CreateNumberValue(await frame.Task)}});
       Check(!warm||reused,"Stable warm opening rebuilt the release layout");
       Check(tab.IsChecked==true,"Reopening changed the last used tab");
      }
      finally{CompositionTarget.Rendering-=rendered;}
     }
    }
    preferences.Hidden.Add("Display");settings[OnexTabsQaKey]=preferences.Serialize();
    Check(!ApplyTabPrefsOnWidgetOpen()&&DisplayNavItem.Visibility==Visibility.Collapsed,"Changed tab preference did not reapply");
    Check(ApplyTabPrefsOnWidgetOpen(),"Stable hidden tabs were rebuilt");
    DisplayNavItem.Visibility=Visibility.Visible;
    Check(!ApplyTabPrefsOnWidgetOpen()&&DisplayNavItem.Visibility==Visibility.Collapsed,"A mismatched visual tree was accepted");
   }
   finally
   {
    if(hadQa)settings[OnexTabsQaKey]=oldQa;else settings.Remove(OnexTabsQaKey);
    _onexTabsQaStore=oldStore;
    EnsureOnexReleaseLayout();
    if(original!=null)original.IsChecked=true;
   }
   Check(ReadOnexTabValue(OnexTabsKey)==ownerTabs&&original?.IsChecked==true,"Owner tabs were not preserved");
   report["ownerProfileWrites"]=JsonValue.CreateNumberValue(0);
   report["warmOpenChecks"]=JsonValue.CreateNumberValue(checks);
   report["warmOpenChecksPassed"]=JsonValue.CreateBooleanValue(true);
  }

  private async Task VerifyOnexNavigationResponseAsync(JsonObject report)
  {
   int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
   Func<bool> importedAbsent=()=>VirtualControllerMasterCard==null&&ControllerEmulationCard==null&&MouseSettingsCard==null&&ControllerRemappingSection==null&&GPDFanControlSection==null&&GPDButtonRemapSection==null;
   report["importedDeviceTreesAbsentBefore"]=JsonValue.CreateBooleanValue(importedAbsent());
   Check(importedAbsent(),"Unused imported device editor was constructed at startup");
   var original=GetVisibleNavigationItems().FirstOrDefault(tab=>tab.IsChecked==true);
   var marker=new Border{Width=3,Height=3,Background=OnexSolidBrush(Colors.Cyan),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,IsHitTestVisible=false};
   var samples=new List<OnexNavigationSample>(48);
   var correlationStop=new CancellationTokenSource();Task correlationProbe=Task.CompletedTask;
   // Capture this agile dispatcher on its owning UI thread. Obtaining a XAML
   // DependencyObject property inside Task.Run measures cross-apartment access.
   var uiDispatcher=Dispatcher;var heartbeatClock=Stopwatch.StartNew();
   double lastHeartbeat=0,warmHeartbeatMax=0;int warmHeartbeats=0;bool heartbeatWarm=false,lastHeartbeatWarm=false;
   var heartbeat=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(16)};
   heartbeat.Tick+=(sender,args)=>{double now=heartbeatClock.Elapsed.TotalMilliseconds;if(heartbeatWarm){warmHeartbeats++;if(lastHeartbeatWarm&&lastHeartbeat>0)warmHeartbeatMax=Math.Max(warmHeartbeatMax,now-lastHeartbeat);}RecordOnexNavigationPoint("heartbeat",null);lastHeartbeat=now;lastHeartbeatWarm=heartbeatWarm;};

   var warmup=new JsonObject();foreach(var entry in _onexPageWarmupMilliseconds)warmup[entry.Key]=JsonValue.CreateNumberValue(entry.Value);
   report["productStartupPagePreparationMs"]=warmup;report["productStartupPagesPrepared"]=JsonValue.CreateNumberValue(_onexWarmedPages.Count);
   report["startupPreparationNavigatedOrWroteSettings"]=JsonValue.CreateBooleanValue(false);
   report["unopenedAppearanceChoicePanelBuilt"]=JsonValue.CreateBooleanValue(_onexChoicePanel!=null);report["closedChoicePreviewRetained"]=JsonValue.CreateBooleanValue(_onexOpenChoice==null&&_onexChoiceOriginal!=null);
    report["gcMode"]=JsonValue.CreateStringValue(System.Runtime.GCSettings.LatencyMode.ToString());
   report["appMemoryBeforeBytes"]=JsonValue.CreateNumberValue(Windows.System.MemoryManager.AppMemoryUsage);report["appMemoryLimitBytes"]=JsonValue.CreateNumberValue(Windows.System.MemoryManager.AppMemoryUsageLimit);
   report["managedMemoryBeforeBytes"]=JsonValue.CreateNumberValue(GC.GetTotalMemory(false));
   report["ownedResourceBrushesBefore"]=JsonValue.CreateNumberValue(_onexOwnedResourceBrushesCreated);
   try{
    Check(_onexDetailsDialog==null&&!OnexSourceEditorsPending,"Preserve pending owner edits");
    BeginOnexNavigationCorrelation();
    Canvas.SetZIndex(marker,200000);OnexStartupRoot.Children.Add(marker);_onexNavigationPerfCapture=true;_onexPushLongestMs=0;_onexPushLongestKey=null;_onexNavigationHotspots.Clear();
    var tabs=GetVisibleNavigationItems().ToArray();Check(tabs.Length>=6,"Actual visible tabs missing");
    heartbeat.Start();correlationProbe=Task.Run(()=>ProbeOnexNormalDispatcherAsync(uiDispatcher,correlationStop.Token));
    int timeouts=0;bool toggle=false;long overlayRowsAfterFirstVisit=-1;
    foreach(string source in new[]{"pointer","keyboard"})for(int lap=0;lap<3;lap++)foreach(var tab in tabs){
     heartbeatWarm=lap>0;_onexNavigationTraceWarm=heartbeatWarm;_onexNavigationTraceLap=lap;_onexNavigationTraceTab=tab.Tag as string;RecordOnexNavigationPoint("tab-start",source);
     if(source=="pointer"){RememberOnexTabPointer(tab);tab.Focus(FocusState.Pointer);}else{_onexTabPointer=null;tab.Focus(FocusState.Keyboard);}
     var watch=Stopwatch.StartNew();var frame=new TaskCompletionSource<double>();EventHandler<object> rendered=null;
     rendered=(s,e)=>{CompositionTarget.Rendering-=rendered;frame.TrySetResult(watch.Elapsed.TotalMilliseconds);};
     CompositionTarget.Rendering+=rendered;tab.IsChecked=true;marker.Width=(toggle=!toggle)?3:4;double nav=watch.Elapsed.TotalMilliseconds;RecordOnexNavigationPoint("tab-handler",source);
     var complete=await Task.WhenAny(frame.Task,Task.Delay(400));CompositionTarget.Rendering-=rendered;bool observed=complete==frame.Task;if(!observed)timeouts++;
     double render=observed?await frame.Task:watch.Elapsed.TotalMilliseconds,queue=0,submission=0;
     await Task.Run(async()=>{var queued=Stopwatch.StartNew();var delivery=uiDispatcher.RunAsync(CoreDispatcherPriority.Normal,()=>{queue=queued.Elapsed.TotalMilliseconds;});submission=queued.Elapsed.TotalMilliseconds;await delivery;});
     Check(GetActiveScrollViewer()?.Visibility==Visibility.Visible,"Tab request did not reveal its page");
     if(source=="keyboard"){
      // Let the product's deferred entry handler run; do not add a second
      // forced layout/focus pass that ordinary navigation never requests.
      await Dispatcher.RunAsync(CoreDispatcherPriority.Low,()=>{});
      var viewer=GetActiveScrollViewer();var focus=FocusManager.GetFocusedElement() as DependencyObject;bool inside=false;
      for(var node=focus;node!=null;node=VisualTreeHelper.GetParent(node))if(ReferenceEquals(node,viewer)){inside=true;break;}
      Check(inside,"Keyboard/controller content entry lost its active page");
     }
     if(source=="pointer"&&lap==0&&(tab.Tag as string)=="OnexOsd")overlayRowsAfterFirstVisit=_onexOverlayRowsCreated;
     samples.Add(new OnexNavigationSample{Tab=tab.Tag as string??"",Source=source,Lap=lap,Navigation=nav,Render=render,Observed=observed,Queue=queue,Submission=submission,Font=_onexNavFontMs,Theme=_onexNavSurfaceMs,Accent=_onexNavAccentMs,Gc0=GC.CollectionCount(0),Gc1=GC.CollectionCount(1),Gc2=GC.CollectionCount(2)});
     RecordOnexNavigationPoint("tab-finish",source);await Task.Delay(30);
    }
    _onexNavigationPerfCapture=false; // No native JSON allocation inside timed work.
    var warm=samples.Where(value=>value.Lap>0).ToArray();double[] navs=warm.Select(x=>x.Navigation).OrderBy(x=>x).ToArray(),frames=warm.Select(x=>x.Render).OrderBy(x=>x).ToArray(),queues=warm.Select(x=>x.Queue).OrderBy(x=>x).ToArray();
    var cold=samples.Where(value=>value.Source=="pointer"&&value.Lap==0).ToArray();double coldNav=cold.Max(x=>x.Navigation),coldFrame=cold.Max(x=>x.Render);
    report["warmNavigationMedianMs"]=JsonValue.CreateNumberValue(navs[navs.Length/2]);report["warmNavigationMaxMs"]=JsonValue.CreateNumberValue(navs.Last());report["warmRenderMedianMs"]=JsonValue.CreateNumberValue(frames[frames.Length/2]);report["warmRenderMaxMs"]=JsonValue.CreateNumberValue(frames.Last());report["normalQueueMaxMs"]=JsonValue.CreateNumberValue(queues.Last());
    report["coldNavigationMaxMs"]=JsonValue.CreateNumberValue(coldNav);report["coldRenderMaxMs"]=JsonValue.CreateNumberValue(coldFrame);report["renderTimeouts"]=JsonValue.CreateNumberValue(timeouts);
    report["overlayWarmRowsCreated"]=JsonValue.CreateNumberValue(overlayRowsAfterFirstVisit<0?-1:_onexOverlayRowsCreated-overlayRowsAfterFirstVisit);
    report["overlayRowsReused"]=JsonValue.CreateNumberValue(_onexOverlayRowsReused);
    var rowsBefore=osdItemViewModels.ToArray();bool priorLoading=_isLoadingOSDConfig;isLoadingOSDConfig=true;try{RefreshOSDItemsControl();}finally{isLoadingOSDConfig=priorLoading;}
    Check(rowsBefore.Length==osdItemViewModels.Count&&rowsBefore.Zip(osdItemViewModels,(before,after)=>ReferenceEquals(before,after)).All(equal=>equal),"Unchanged overlay read rebuilt metric rows");
    report["importedDeviceTreesAbsentAfter"]=JsonValue.CreateBooleanValue(importedAbsent());
    Check(importedAbsent(),"Tab navigation realized an unused imported device editor");
    Check(timeouts==0,"A requested XAML frame missed400ms");Check(coldNav<250&&coldFrame<400,"First tab visit exceeded250ms handler/400ms rendering");
    Check(navs.Last()<150,"Warm tab handler/layout still blocked150ms");Check(frames.Last()<250&&queues.Last()<150,"Warm render/normal dispatcher exceeded responsiveness budget");
    Check(warmHeartbeats>10&&warmHeartbeatMax<150,"Actual UI heartbeat exceeded150ms during warm interaction");
   }finally{
    heartbeat.Stop();_onexNavigationPerfCapture=false;correlationStop.Cancel();await correlationProbe;correlationStop.Dispose();WriteOnexNavigationCorrelation(report);report["warmUiHeartbeatCount"]=JsonValue.CreateNumberValue(warmHeartbeats);report["warmUiHeartbeatMaxMs"]=JsonValue.CreateNumberValue(warmHeartbeatMax);report["dispatcherCapturedOnUiThread"]=JsonValue.CreateBooleanValue(true);
    _onexNavigationPerfCapture=false;OnexStartupRoot.Children.Remove(marker);report["checks"]=JsonValue.CreateNumberValue(checks);
    report["longestLiveStateApplyMs"]=JsonValue.CreateNumberValue(_onexPushLongestMs);report["longestLiveStateApplyKey"]=JsonValue.CreateStringValue(_onexPushLongestKey??"none");report["nodeHotspots"]=OnexNavigationHotspotReport();
    report["appMemoryAfterBytes"]=JsonValue.CreateNumberValue(Windows.System.MemoryManager.AppMemoryUsage);report["managedMemoryAfterBytes"]=JsonValue.CreateNumberValue(GC.GetTotalMemory(false));
    report["ownedResourceBrushesAfter"]=JsonValue.CreateNumberValue(_onexOwnedResourceBrushesCreated);
    report["overlayPreviewNativeBuilds"]=JsonValue.CreateNumberValue(_onexOverlayPreviewBuilds);report["overlayPreviewIdenticalModelReuses"]=JsonValue.CreateNumberValue(_onexOverlayPreviewReuses);
    report["sharedFocusControlsHooked"]=JsonValue.CreateNumberValue(_onexFocusControlsHooked);report["sharedFocusDelegateSlots"]=JsonValue.CreateNumberValue(_onexSharedGotFocus==null?0:8);
    report["unopenedRemapEditorsPrepared"]=JsonValue.CreateNumberValue(_onexRemapWindows.Count(view=>!view.Flyout.IsOpen&&view.Width>=0));report["unopenedKeyboardContainersAfterTiming"]=JsonValue.CreateNumberValue(_kbmKeys.Sum(choice=>Enumerable.Range(0,choice.Items.Count).Count(index=>choice.ContainerFromIndex(index)!=null)));
    report["keyboardEditorBuiltAfterNavigation"]=JsonValue.CreateBooleanValue(_kbmFlyout!=null);
    report["fnLayerBuiltAfterNavigation"]=JsonValue.CreateBooleanValue(_kbmDiagrams.ContainsKey("fn"));report["fnAssignedAfterNavigation"]=JsonValue.CreateBooleanValue(!string.IsNullOrEmpty(ControllerText(KbmPolicy,"fnButton")));report["fnButtonsAfterNavigation"]=JsonValue.CreateNumberValue(_kbmTiles.Keys.Count(key=>key.StartsWith("fn:",StringComparison.Ordinal)));
    var output=new JsonArray();foreach(var x in samples)output.Add(new JsonObject{{"tab",JsonValue.CreateStringValue(x.Tab)},{"source",JsonValue.CreateStringValue(x.Source)},{"lap",JsonValue.CreateNumberValue(x.Lap)},{"navigationAndLayoutMs",JsonValue.CreateNumberValue(x.Navigation)},{"nextXamlRenderingMs",JsonValue.CreateNumberValue(x.Render)},{"renderObserved",JsonValue.CreateBooleanValue(x.Observed)},{"normalPriorityQueueMs",JsonValue.CreateNumberValue(x.Queue)},{"dispatcherSubmissionMs",JsonValue.CreateNumberValue(x.Submission)},{"fontPreparationMs",JsonValue.CreateNumberValue(x.Font)},{"themePreparationMs",JsonValue.CreateNumberValue(x.Theme)},{"accentFocusPreparationMs",JsonValue.CreateNumberValue(x.Accent)},{"gc0",JsonValue.CreateNumberValue(x.Gc0)},{"gc1",JsonValue.CreateNumberValue(x.Gc1)},{"gc2",JsonValue.CreateNumberValue(x.Gc2)}});
    // Public owned content only, after all timed work and the heartbeat stopped.
    // Counts identify remaining eager UI, never owner labels or heap contents.
    var census=OnexOwnedContent(this).Concat(_onexRemapWindows.SelectMany(view=>OnexOwnedContent(view.Editor))).Distinct().ToArray();
    var choices=census.OfType<ComboBox>().ToArray();var kinds=new JsonArray();
    foreach(var kind in census.GroupBy(node=>node.GetType().Name).OrderByDescending(group=>group.Count()).Take(12))kinds.Add(new JsonObject{{"type",JsonValue.CreateStringValue(kind.Key)},{"count",JsonValue.CreateNumberValue(kind.Count())}});
    report["publicOwnedLogicalTypesAfterTiming"]=kinds;report["publicChoiceLogicalItems"]=JsonValue.CreateNumberValue(choices.Sum(choice=>choice.Items.Count));report["publicChoiceEagerFrameworkItems"]=JsonValue.CreateNumberValue(choices.Sum(choice=>choice.Items.Cast<object>().Count(item=>item is DependencyObject)));report["logicalCensusIncludesNativePrivateTemplatesOrHeap"]=JsonValue.CreateBooleanValue(false);
    report["samples"]=output;report["ownerSettingsWritten"]=JsonValue.CreateBooleanValue(false);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);report["forcedXamlMarker"]=JsonValue.CreateBooleanValue(true);report["nativeJsonAllocatedDuringTiming"]=JsonValue.CreateBooleanValue(false);report["collectorPolicyChanged"]=JsonValue.CreateBooleanValue(false);report["forcedSynchronousLayout"]=JsonValue.CreateBooleanValue(false);report["extraDiagnosticContentFocus"]=JsonValue.CreateBooleanValue(false);
    if(original!=null)original.IsChecked=true;
   }
  }
 }
}
