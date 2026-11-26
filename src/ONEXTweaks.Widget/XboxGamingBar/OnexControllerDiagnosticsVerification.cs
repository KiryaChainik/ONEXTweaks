// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.Storage;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexControllerDiagnosticsAsync(JsonObject report)
  {
   int checks=0;void Check(bool v,string why){checks++;if(!v)throw new InvalidOperationException(why);}
   await FlushOnexControllerPolicyAsync();for(int n=0;n<100&&_onexControllerBusy;n++)await Task.Delay(30);Check(!_onexControllerBusy&&_onexControllerPending==null,"Protect pending owner settings");await ReadOnexControllerAsync();
   var tab=GetVisibleNavigationItems().FirstOrDefault(n=>n.IsChecked==true);string before=ControllerEditorPolicy.Stringify(),open=_onexControllerOpen;bool guard=_onexControllerReadOnlyProjection;
   JsonObject Object(string text)=>JsonObject.Parse(text);
   async Task VerifyReturn(string kind)
   {
    var anchor=kind=="gyro"?_onexControllerGyroTest:_onexControllerSticksTest;
    for(int n=0;n<100&&(!ReferenceEquals(FocusManager.GetFocusedElement(),anchor)||LegionNavItem.IsChecked!=true||_onexControllerOpen!=kind);n++)await Task.Delay(25);
    Check(LegionNavItem.IsChecked==true&&LegionScrollViewer.Visibility==Visibility.Visible,"Diagnostic return selected a different page: "+kind);
    Check(_onexControllerOpen==kind&&_onexControllerSections.Single(card=>card.Id==kind).Detail.Visibility==Visibility.Visible,"Diagnostic return did not expand its originating card: "+kind);
    Check(ReferenceEquals(FocusManager.GetFocusedElement(),anchor),"Diagnostic return lost its test button focus: "+kind);
    await Task.Delay(150);
    Check(ReferenceEquals(FocusManager.GetFocusedElement(),anchor),"Delayed tab-entry work replaced diagnostic focus: "+kind);
    await SaveOnexImage("onex-live1076-return-"+kind+".png",this,80);
   }
   try{
    _onexControllerReadOnlyProjection=true;LegionNavItem.IsChecked=true;Check(_onexControllerSticksTest!=null&&_onexControllerGyroTest!=null,"Live test entry buttons missing");
    foreach(string card in new[]{"sticks","gyro"}){SetOnexControllerCard(null);SetOnexControllerCard(card);var button=card=="sticks"?_onexControllerSticksTest:_onexControllerGyroTest;Check(OnexControllerRows().SelectMany(row=>row).Contains(button),"Test button missing in controller navigation");}
    foreach(string kind in new[]{"sticks"}){
     var panel=BuildOnexControllerDiagnosticPanel(kind);var dialog=CreateOnexControllerDiagnosticDialog(kind,panel);
     _onexDetailsDialog=dialog;_onexControllerDiagnosticKind=kind;var shown=dialog.ShowAsync();
     try{
      await Task.Delay(180);for(int n=0;n<80&&panel.ActualWidth<1;n++)await Task.Delay(25);UpdateLayout();
      if(kind=="sticks"){
       ApplyOnexControllerDiagnostic(Object(@"{""active"":true,""available"":true,""physical"":{""lx"":100,""ly"":-100,""rx"":-50,""ry"":25,""lt"":75,""rt"":25},""tuned"":{""lx"":80,""ly"":-80,""rx"":-40,""ry"":20,""lt"":100,""rt"":0}}"));
       var plot=_onexStickPlot;double radius=OnexControllerStickPlot.Radius,cy=OnexControllerStickPlot.CenterY;
       bool At(int side,bool physical,double x,double y)
       {
        var point=plot.Endpoint(side,physical);
        return Math.Abs(point.X-(OnexControllerStickPlot.CenterX(side)+x*radius))<.001&&Math.Abs(point.Y-(cy-y*radius))<.001;
       }
       Check(plot.HasPoint(0,false)&&At(0,false,.80,-.80),"Output stick position or Y orientation wrong");
       Check(At(0,true,1,-1)&&!plot.Outside(0,true),"Independent diagonal axes were radially clamped");
       Check(plot.TriggerFill(0)==OnexControllerStickPlot.TriggerHeight&&plot.TriggerFill(1)==0,"Trigger fill range wrong");
       Check(_onexControllerDiagnosticNumbers[2].Text=="100%"&&_onexControllerDiagnosticNumbers[3].Text=="0%","Trigger percentage labels wrong");
       // Projection fixture: exercise the actual XAML cells separately from the physical RPC phase.
       ApplyOnexControllerDiagnostic(Object(@"{""active"":true,""available"":true,""physical"":{""lx"":75,""ly"":40,""rx"":-100,""ry"":100,""lt"":9.8039215686,""rt"":50.1960784314},""tuned"":{""lx"":50,""ly"":0,""rx"":-100,""ry"":100,""lt"":0,""rt"":37.6470588235},""tuning"":{""leftDeadzone"":50,""rightDeadzone"":50,""leftTriggerStart"":20,""leftTriggerEnd"":100,""rightTriggerStart"":20,""rightTriggerEnd"":100,""hairTriggers"":false}}"));
       var grid=_onexAnalogReadouts;
       Check(grid.RawAt(0)=="0.75000"&&grid.OutputAt(0)=="0.50000","Midrange X projection lost the deadzone difference");
       Check(grid.RawAt(1)=="0.40000"&&grid.OutputAt(1)=="0.00000","Positive Y inside the centre deadzone is incorrect");
       Check(grid.RawAt(2)=="-1.00000"&&grid.OutputAt(2)=="-1.00000"&&grid.RawAt(3)=="1.00000"&&grid.OutputAt(3)=="1.00000","Axis endpoints or axis order is incorrect");
       Check(grid.RawAt(4)=="0.09804"&&grid.OutputAt(4)=="0.00000"&&grid.RawAt(5)=="0.50196"&&grid.OutputAt(5)=="0.37647","Independent raw and tuned trigger projection is incorrect");
       Check(grid.TravelDescription==OnexStrings.Format("Deadzone / full press · LT {0}% / {1}% · RT {2}% / {3}%",20,100,20,100),"Live trigger ranges are missing");
       Check(At(0,true,.75,.4)&&At(0,false,.5,0)&&Math.Abs(plot.DeadzoneWidth(0)-radius)<.001,"Midrange axes or square deadzone geometry wrong");
       plot.SetZoom(2);
       Check(plot.Outside(0,true)&&!plot.HasPoint(0,true)&&At(0,true,1,.4/.75)&&plot.HasPoint(0,false)&&At(0,false,1,0),"Zoom failed to distinguish a clipped direction and a measured point on the boundary");
       Check(grid.RawAt(0)=="0.75000"&&grid.OutputAt(0)=="0.50000","Zoom changed measured coordinates");
       await SaveOnexImage("onex-live1074-synthetic-zoom-2x.png",panel,40);
       plot.SetZoom(5);Check(plot.Outside(0,true)&&plot.Outside(0,false)&&plot.DeadzoneWidth(0)==2*radius,"Zoomed deadzone or outside arrows wrong");
       plot.SetZoom(10);Check(At(1,true,-1,1)&&plot.Outside(1,true),"Clipped diagonal direction changed at 10×");
       await SaveOnexImage("onex-live1074-synthetic-zoom-10x.png",panel,40);
       plot.SetZoom(1);var midrangeExample=_onexControllerDiagnosticLast;
       ApplyOnexControllerDiagnostic(Object(@"{""active"":true,""available"":true,""physical"":{""lx"":100,""ly"":0,""rx"":0,""ry"":0,""lt"":0,""rt"":0},""tuned"":{""lx"":100,""ly"":0,""rx"":0,""ry"":0,""lt"":0,""rt"":0}}"));
       Check(At(0,true,1,0)&&At(0,false,1,0)&&At(1,true,0,0)&&At(1,false,0,0),"Full cardinal position must land exactly on the unit circle and neutral at the origin");
       Check(OnexControllerDiagnosticHost.IsAnalogNavigation(VirtualKey.GamepadLeftThumbstickUp)&&OnexControllerDiagnosticHost.IsAnalogNavigation(VirtualKey.GamepadRightTrigger)&&!OnexControllerDiagnosticHost.IsAnalogNavigation(VirtualKey.GamepadDPadUp)&&!OnexControllerDiagnosticHost.IsAnalogNavigation(VirtualKey.GamepadA),"Host zoom navigation swallowed a digital control or accepted analog navigation");
       ApplyOnexControllerDiagnostic(midrangeExample);
       report["syntheticZoomAndVectorGeometry"]=JsonValue.CreateBooleanValue(true);
       report["syntheticAxisAndTriggerReadouts"]=JsonValue.CreateBooleanValue(true);
      }else{
       ApplyOnexControllerDiagnostic(Object(@"{""active"":true,""available"":true,""motion"":{""angular"":{""x"":90,""y"":-45,""z"":180},""acceleration"":{""x"":0.2,""y"":-0.3,""z"":0.9}}}"));
       Check(_onexControllerDiagnosticAxes.Count==6&&_onexControllerDiagnosticNumbers.Count==3&&_onexControllerDiagnosticBars.Count==6,"Six gyro channels/three rows missing");
       Check(_onexControllerDiagnosticNumbers[0].Text.Contains("+90.0")&&_onexControllerDiagnosticNumbers[0].Text.Contains("+0.2"),"Gyro/accel units andvalues missing");
       Check(_onexControllerDiagnosticBars[0].Width==125&&_onexControllerDiagnosticBars[4].Width==250,"Signed gyro bars wrong");
       Check(_onexControllerDiagnosticAxes[1].Opacity==1&&_onexControllerDiagnosticAxes[2].Opacity==1&&_onexControllerDiagnosticAxes[5].Opacity==1,"Signed motion arrows wrong");
       ApplyOnexControllerDiagnostic(Object(@"{""active"":true,""available"":true,""motion"":{""angular"":{""x"":0,""y"":0,""z"":0},""acceleration"":{""x"":0.6,""y"":-0.5,""z"":0.6}}}"));
       Check(Math.Abs(_onexControllerDiagnosticTilt.RotationX)>1&&Math.Abs(_onexControllerDiagnosticTilt.RotationY)>1,"Console relative tilt not drawn");
      }
      int navigations=_onexTriggerNavigationCount;HandleOnexTriggerPress(VirtualKey.GamepadLeftTrigger,false);HandleOnexTriggerPress(VirtualKey.GamepadRightTrigger,false);Check(_onexTriggerNavigationCount==navigations,"Live test triggers switched tabs");
      report[kind+"Dimensions"]=new JsonObject{{"panelWidth",JsonValue.CreateNumberValue(panel.ActualWidth)},{"panelHeight",JsonValue.CreateNumberValue(panel.ActualHeight)},{"dialogWidth",JsonValue.CreateNumberValue(dialog.ActualWidth)},{"widgetWidth",JsonValue.CreateNumberValue(ActualWidth)}};
      await SaveOnexImage("onex-live320-"+kind+".png",panel,80);
      Check(panel.ActualWidth>=Math.Min(232,ActualWidth-80)&&panel.ActualWidth<=660,"Live diagram width clipped: "+panel.ActualWidth);Check(panel.ActualHeight>260,"Live diagram not rendered");
      ApplyOnexControllerDiagnostic(Object(@"{""active"":true,""available"":false,""status"":""No data""}"));Check(_onexControllerDiagnosticNumbers.All(n=>n.Text=="—"),"Disconnected input retained as live");
      if(kind=="sticks")Check(Enumerable.Range(0,6).All(i=>_onexAnalogReadouts.RawAt(i)=="—"&&_onexAnalogReadouts.OutputAt(i)=="—")&&_onexAnalogReadouts.TravelDescription=="—","Unavailable frame left stale readouts");
      foreach(VirtualKey key in new[]{VirtualKey.GamepadA,VirtualKey.GamepadB,VirtualKey.GamepadLeftTrigger,VirtualKey.GamepadRightTrigger,VirtualKey.GamepadLeftThumbstickUp,VirtualKey.GamepadRightThumbstickLeft,VirtualKey.Up,VirtualKey.Enter})Check(IsOnexControllerDiagnosticInput(key),"Test input can escape to page");
     }finally{dialog.Hide();await shown;_onexDetailsDialog=null;_onexControllerDiagnosticKind="";}
    }
    Check(string.IsNullOrEmpty(_onexControllerDiagnosticToken)&&!_onexControllerDiagnosticReadBusy,"Read-only UI projection started real session");
    _onexControllerReadOnlyProjection=false;
    var triggerProjection=new JsonObject();await VerifyOnexControllerTriggersAsync(triggerProjection);
    report["triggerEditorReadOnlyProjection"]=triggerProjection;
    var gyroLocks=new JsonObject();VerifyOnexGyroAvailabilityReasons(gyroLocks);
    report["gyroAvailabilityReadOnlyProjection"]=gyroLocks;
    var dashboard=new JsonObject();await VerifyOnexGyroDashboardAsync(dashboard);report["gyroDashboardProjection"]=dashboard;
    var gyroOff=new JsonObject();await VerifyOnexGyroEditableOffUiAsync(gyroOff);report["gyroEditableOffUiProjection"]=gyroOff;
    SetOnexControllerCard(null);SetOnexControllerCard("sticks");
    await VerifyOnexDiagnosticPollingRecoveryAsync(Check);
    report["injectedTransportPollingRecovery"]=JsonValue.CreateBooleanValue(true);
    Check(Windows.Gaming.UI.GameBar.IsInputRedirected,"Open Game Bar before the sticks host verification");
    await ShowOnexControllerDiagnosticAsync("sticks",_onexControllerSticksTest,verifyHost:true);
    var host=_onexSticksHostLast;
    Check(host!=null&&!host.GetNamedBoolean("active"),"Controller host did not finish");
    Check(host.GetNamedBoolean("ready")&&host.GetNamedBoolean("released")&&host.GetNamedBoolean("foreground"),"Controller host did not own released input");
    Check(host.GetNamedNumber("hostPid")>0&&host.GetNamedNumber("hostPid")!=Windows.System.Diagnostics.ProcessDiagnosticInfo.GetForCurrentProcess().ProcessId,"Controller test did not run in an independent process");
    Check(host.GetNamedNumber("renderedSequence")>=3&&host.GetNamedBoolean("captured"),"Controller host did not draw and capture three actual reads");
    var displayed=host.GetNamedObject("displayed");
    Check(displayed.GetNamedBoolean("available")&&displayed.GetNamedNumber("sequence")==host.GetNamedNumber("renderedSequence"),"Controller host acknowledged a different sample");
    foreach(string channel in new[]{"lx","ly","rx","ry","lt","rt"})
     Check(displayed.GetNamedObject("physical").GetNamedValue(channel).ValueType==JsonValueType.Number&&displayed.GetNamedObject("tuned").GetNamedValue(channel).ValueType==JsonValueType.Number,"Missing physical/tuned "+channel);
    report["sticksIndependentHost"]=host;
    report["desktopPhysicalSample"]=JsonObject.Parse(displayed.GetNamedObject("physical").Stringify());
    for(int n=0;n<100&&!Windows.Gaming.UI.GameBar.IsInputRedirected;n++)await Task.Delay(50);
    Check(host.GetNamedBoolean("returnRequested")&&Windows.Gaming.UI.GameBar.IsInputRedirected,"Closing the controller host did not return to the widget");
    await VerifyReturn("sticks");report["actualSticksReturnContextAndFocus"]=JsonValue.CreateBooleanValue(true);
    var stopped=await ReadOnexControllerAsync();
    Check(OnexMotionObject(stopped,"diagnostics")?.GetNamedBoolean("active",true)==false,"Host left a live controller lease");
    SetOnexControllerCard(null);SetOnexControllerCard("gyro");
    await ShowOnexControllerDiagnosticAsync("gyro",_onexControllerGyroTest,verifyHost:true);
    var gyroHost=_onexGyroHostLast;
    Check(gyroHost!=null&&!gyroHost.GetNamedBoolean("active")&&gyroHost.GetNamedBoolean("ready")&&gyroHost.GetNamedBoolean("released")&&gyroHost.GetNamedBoolean("foreground"),"Gyro host did not finish with released input");
    Check(gyroHost.GetNamedNumber("hostPid")>0&&gyroHost.GetNamedNumber("hostPid")!=Windows.System.Diagnostics.ProcessDiagnosticInfo.GetForCurrentProcess().ProcessId,"Gyro test did not run independently");
    Check(gyroHost.GetNamedNumber("renderedSequence")>=3&&gyroHost.GetNamedBoolean("captured")&&gyroHost.GetNamedBoolean("returnRequested"),"Gyro host did not capture its actual sample and return");
    var gyroDisplayed=gyroHost.GetNamedObject("displayed");
    Check(gyroDisplayed.GetNamedBoolean("available")&&gyroDisplayed.GetNamedString("kind")=="gyro"&&gyroDisplayed.GetNamedNumber("sequence")==gyroHost.GetNamedNumber("renderedSequence"),"Gyro host acknowledged another sample");
    var preview=gyroDisplayed.GetNamedObject("gyroPreview");
    Check(preview.GetNamedNumber("tick")>0&&preview.GetNamedNumber("sensorSequence")==gyroDisplayed.GetNamedObject("motion").GetNamedNumber("sequence"),"Gyro raw and processed preview are not paired");
    foreach(string axis in new[]{"x","y","z"})Check(preview.GetNamedObject("raw").GetNamedValue(axis).ValueType==JsonValueType.Number&&preview.GetNamedObject("calibrated").GetNamedValue(axis).ValueType==JsonValueType.Number,"Gyro host is missing a real raw/corrected body axis");
    report["gyroIndependentHost"]=gyroHost;
    for(int n=0;n<100&&!Windows.Gaming.UI.GameBar.IsInputRedirected;n++)await Task.Delay(50);
    await VerifyReturn("gyro");report["actualGyroReturnContextAndFocus"]=JsonValue.CreateBooleanValue(true);
    var gyroClosed=await ReadOnexControllerAsync();
    Check(OnexMotionObject(gyroClosed,"diagnostics")?.GetNamedBoolean("active",true)==false,"Gyro left a live controller lease");
    string tabsBefore=ApplicationData.Current.LocalSettings.Values[OnexTabsActiveKey] as string;
    OnexControllerDiagnosticReturnHidden();
    Check(_onexControllerDiagnosticReturnKind==null,"Completed diagnostic return leaked into the next widget opening");
    var preference=LoadOnexTabs();ApplyDefaultTabOnOpen("DiagnosticReturnOrdinaryReopen");
    if(!string.IsNullOrEmpty(preference.Open))Check(GetVisibleNavigationItems().Single(item=>item.Tag as string==preference.Open).IsChecked==true,"Ordinary opening no longer uses the saved startup tab");
    Check((ApplicationData.Current.LocalSettings.Values[OnexTabsActiveKey] as string)==tabsBefore,"Diagnostic return rewrote startup preferences");
    report["ordinaryStartupPreferencePreserved"]=JsonValue.CreateBooleanValue(true);
    report["actualReadOnlyAppServicePolling"]=JsonValue.CreateBooleanValue(true);
    report["actualSticksAndGyroWindowsOpenedAndClosedProgrammatically"]=JsonValue.CreateBooleanValue(true);
    report["checks"]=JsonValue.CreateNumberValue(checks);report["diagramProjectionUsedForRender"]=JsonValue.CreateBooleanValue(true);report["realSensorInvokedInSeparateLiveWindowPhase"]=JsonValue.CreateBooleanValue(true);report["profileWrites"]=JsonValue.CreateNumberValue(0);
   }finally{ClearOnexControllerDiagnosticReturn();_onexControllerReadOnlyProjection=guard;await ReadOnexControllerAsync();Check(ControllerEditorPolicy.Stringify()==before,"Diagnostics UI changed owner policy");SetOnexControllerCard(open);if(tab!=null)tab.IsChecked=true;}
  }

  private async Task VerifyOnexDiagnosticPollingRecoveryAsync(Action<bool,string> check)
  {
   async Task Until(Func<bool> ready,string message)
   {
    for(int n=0;n<160&&!ready();n++)await Task.Delay(50);
    check(ready(),message);
   }
   ValueSet Session(string token)
   {
    var session=new JsonObject{{"active",JsonValue.CreateBooleanValue(token.Length>0)},{"kind",JsonValue.CreateStringValue("sticks")},{"token",JsonValue.CreateStringValue(token)}};
    return new ValueSet{{"OnexControllerState",new JsonObject{{"diagnostics",session}}.Stringify()}};
   }
   ValueSet Sample(double x,double trigger,int sequence)
   {
    var axes=new JsonObject();
    foreach(string key in new[]{"lx","ly","rx","ry","lt","rt"})axes[key]=JsonValue.CreateNumberValue(key=="lx"?x:key=="lt"?trigger:0);
    var state=new JsonObject{{"active",JsonValue.CreateBooleanValue(true)},{"available",JsonValue.CreateBooleanValue(true)},{"kind",JsonValue.CreateStringValue("sticks")},{"sequence",JsonValue.CreateNumberValue(sequence)},{"physical",axes},{"tuned",axes}};
    return new ValueSet{{"OnexControllerDiagnosticState",state.Stringify()}};
   }
   var firstToken=Guid.NewGuid().ToString("N");var nextToken=Guid.NewGuid().ToString("N");
   var stopped=new List<string>();int starts=0,firstReads=0,unexpected=0;bool expire=false;
   var failure=new TaskCompletionSource<ValueSet>();var movement=new TaskCompletionSource<ValueSet>();
   Task<ValueSet> Transport(ValueSet request)
   {
    if(request.TryGetValue("OnexControllerSet",out var mutation))
    {
     var change=JsonObject.Parse((string)mutation);var operation=change.GetNamedString("operation");
     if(operation=="diagnosticsStart")return Task.FromResult(Session(++starts==1?firstToken:nextToken));
     if(operation=="diagnosticsStop"){stopped.Add(change.GetNamedString("token"));return Task.FromResult(Session(""));}
    }
    if(request.TryGetValue("OnexControllerDiagnosticGet",out var token))
    {
     if((string)token==firstToken)
     {
      firstReads++;
      if(firstReads==1)return Task.FromResult(Sample(0,0,1));
      if(firstReads==2)return failure.Task;
      if(expire)return Task.FromResult(new ValueSet{{"OnexControllerDiagnosticState","{\"active\":false,\"available\":false}"}});
      return movement.Task;
     }
     if((string)token==nextToken&&starts==2)return Task.FromResult(Sample(-50,25,1));
    }
    unexpected++;return Task.FromResult(new ValueSet{{"Error","Unexpected diagnostic request"}});
   }
   var showing=ShowOnexControllerDiagnosticAsync("sticks",_onexControllerSticksTest,Transport);
   try
   {
    await Until(()=>firstReads>=2,"Production timer did not poll after its initial neutral sample");
    check(_onexControllerDiagnosticNumbers[2].Text=="0%","Initial valid zero was not displayed");
    failure.SetResult(null);
    await Until(()=>firstReads>=3,"Transient read failure stopped the timer or abandoned its live lease");
    check(_onexControllerDiagnosticNumbers.All(n=>n.Text=="—")&&!_onexStickPlot.HasPoint(0,true)&&!_onexStickPlot.HasPoint(0,false),"Transient failure still displayed stale live input");
    movement.SetResult(Sample(100,75,2));
    await Until(()=>_onexControllerDiagnosticNumbers[2].Text=="75%","Recovered input did not repaint the trigger");
    check(starts==1&&Math.Abs(_onexStickPlot.Endpoint(0,true).X-OnexControllerStickPlot.CenterX(0)-OnexControllerStickPlot.Radius)<.01&&_onexStickPlot.HasPoint(0,false),"Same-session recovery lost the stick movement");
    expire=true;
    await Until(()=>starts==2&&_onexControllerDiagnosticNumbers[2].Text=="25%","Expired lease did not restart in the open dialog");
    check(Math.Abs(_onexStickPlot.Endpoint(0,true).X-OnexControllerStickPlot.CenterX(0)+OnexControllerStickPlot.Radius/2)<.01,"Replacement lease did not repaint the opposite stick movement");
   }
   finally
   {
    failure.TrySetResult(null);movement.TrySetResult(Sample(100,75,2));
    _onexDetailsDialog?.Hide();await showing;
   }
   check(starts==2&&unexpected==0&&stopped.Count(t=>t==nextToken)==1&&stopped.All(t=>t==firstToken||t==nextToken),"Polling recovery lost ownership of the replacement lease");

   var lateToken=Guid.NewGuid().ToString("N");var pendingStart=new TaskCompletionSource<ValueSet>();
   var closed=new TaskCompletionSource<bool>();int lateStarts=0,lateReads=0;stopped.Clear();
   Task<ValueSet> LateTransport(ValueSet request)
   {
    if(request.TryGetValue("OnexControllerSet",out var mutation))
    {
     var change=JsonObject.Parse((string)mutation);
     if(change.GetNamedString("operation")=="diagnosticsStart"){lateStarts++;return pendingStart.Task;}
     if(change.GetNamedString("operation")=="diagnosticsStop"){stopped.Add(change.GetNamedString("token"));return Task.FromResult(Session(""));}
    }
    lateReads++;return Task.FromResult(Sample(0,0,1));
   }
   var closing=ShowOnexControllerDiagnosticAsync("sticks",_onexControllerSticksTest,LateTransport);
   try
   {
    await Until(()=>lateStarts>0,"Late-start test did not enter its pending request");
    var dialog=_onexDetailsDialog;dialog.Closed+=(s,e)=>closed.TrySetResult(true);dialog.Hide();
    await Until(()=>closed.Task.IsCompleted,"Diagnostic dialog did not close during its pending start");
    pendingStart.SetResult(Session(lateToken));
    await Until(()=>stopped.Count>0,"Late start left its acquired lease running");
   }
   finally{pendingStart.TrySetResult(Session(lateToken));_onexDetailsDialog?.Hide();await closing;}
   check(lateStarts==1&&lateReads==0&&stopped.SequenceEqual(new[]{lateToken}),"Closed dialog polled, restarted, or released the late lease more than once");
  }
 }
}
