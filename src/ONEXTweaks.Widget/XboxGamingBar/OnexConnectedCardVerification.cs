// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Automation.Peers;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexCpuOutlineFirstFrameAsync(JsonObject report)
  {
   var original=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);
   try
   {
    if(OnexPerformancePendingDetails().Length>0)throw new InvalidOperationException("Owner pending edit protected");
    _releasePowerNav.IsChecked=true;await ReadOnexPerformanceProfilesAsync();
    _onexNavigationAuditHoldingState=true;_onexProfilesState=JsonObject.Parse(_onexProfilesState.Stringify());OnexActiveProfile["split"]=JsonValue.CreateBooleanValue(false);RefreshOnexSourceEditors();
    await SetOnexPerfCardAsync(null);var loading=SetOnexPerfCardAsync("cpu");
    bool joined=_onexPerfOpen=="cpu"&&_onexPerfConnectedOutline.Connected&&OnexPerfCpuCard.BorderThickness.Bottom==0&&OnexPerfCpuDetail.Visibility==Visibility.Visible;
    report["joinedBeforeAsyncCpuReadCompletes"]=JsonValue.CreateBooleanValue(joined);
    if(!joined)throw new InvalidOperationException("CPU's first visible state still has separate outlines");
    await loading;UpdateLayout();await SaveOnexImage("onex-cpu-first-frame253.png",OnexPerfCardGrid,60);
    report["joinedAfterCpuLoad"]=JsonValue.CreateBooleanValue(_onexPerfConnectedOutline.Connected);report["settingsWritesByFixture"]=JsonValue.CreateNumberValue(0);
   }
   finally{_onexNavigationAuditHoldingState=false;await SetOnexPerfCardAsync(null);await ReadOnexPerformanceProfilesAsync();if(original!=null)original.IsChecked=true;}
  }
  private async Task VerifyOnexConnectedCardsAsync(JsonObject report)
  {
   var folder=ApplicationData.Current.LocalFolder;string run=Guid.NewGuid().ToString("N");int step=0;var cases=new JsonArray();report["cases"]=cases;
   var original=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);var power=await ReadOnexStateAsync();
   void Check(bool ok,string text){if(!ok)throw new InvalidOperationException(text);}
   async Task Publish(string value){for(int attempt=0;;attempt++){try{File.WriteAllText(Path.Combine(folder.Path,"onex-gamepad-qa-state.json"),value);return;}catch(IOException){if(attempt>=40)throw;await Task.Delay(25);}}}
   async Task Input(string key,Func<bool> ready)
   {
    string id=run+":"+(++step);var rect=new RadioButtonAutomationPeer(_releasePowerNav).GetBoundingRectangle();
    await Publish(new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"button",JsonValue.CreateStringValue(key)},{"x",JsonValue.CreateNumberValue(rect.X+rect.Width/2)},{"y",JsonValue.CreateNumberValue(rect.Y+rect.Height/2)}}.Stringify());
    bool accepted=false;for(int n=0;n<80;n++){await Task.Delay(60);try{accepted=File.ReadAllText(Path.Combine(folder.Path,"onex-gamepad-qa-ack.txt"))==id&&ready();}catch{}if(accepted)break;}Check(accepted,"Joined-card input failed: "+key+" step="+step);await Task.Delay(80);
   }
   async Task Focus(Control control){control.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(70);Check(control.Focus(FocusState.Keyboard),"Cannot focus joined card control");await Task.Delay(40);}
   void CheckFrame(OnexConnectedCardOutline outline,Button header,Border detail,string kind)
   {
    Check(outline!=null&&outline.Connected&&ReferenceEquals(outline.Header,header),"Connected outline was not drawn: "+kind);
    Check(header.BorderThickness.Left>0&&header.BorderThickness.Top>0&&header.BorderThickness.Right>0&&header.BorderThickness.Bottom==0&&header.Margin.Bottom==0&&header.VerticalAlignment==VerticalAlignment.Stretch&&detail.BorderThickness.Left>0&&outline.Shape.Height==detail.BorderThickness.Top,"Header/editor seam was not removed: "+kind);
    Check(!outline.Shape.IsHitTestVisible,"Outline intercepts controls");cases.Add(new JsonObject{{"case",JsonValue.CreateStringValue(kind)},{"oneJoinedOutline",JsonValue.CreateBooleanValue(true)},{"separateCompleteFrames",JsonValue.CreateNumberValue(0)},{"inputTransparent",JsonValue.CreateBooleanValue(true)}});
   }
   try
   {
    Check(OnexPerformancePendingDetails().Length==0,"Owner pending edit protected; defer joined-card QA");
    _releasePowerNav.IsChecked=true;await ReadOnexPerformanceProfilesAsync();await Task.Delay(120);await Input("focus",()=>true);
    _onexNavigationAuditHoldingState=true;_onexProfilesState=JsonObject.Parse(_onexProfilesState.Stringify());OnexActiveProfile["split"]=JsonValue.CreateBooleanValue(false);RefreshOnexSourceEditors();await Task.Delay(100);
    foreach(string id in new[]{"tdp","fps","fan"})
    {
     var card=_onexPerfSections.First(value=>value.Id==id);await Focus(card.Header);await Input("a",()=>_onexPerfOpen==id);ApplyOnexTabPalette();UpdateLayout();await Task.Delay(60);CheckFrame(_onexPerfConnectedOutline,card.Header,card.Detail,"common-"+id);
     await SaveOnexImage("onex-joined-common-"+id+"252.png",OnexPerfCardGrid,80);await Focus(card.Close);await Input("a",()=>_onexPerfOpen==null);Check(card.Header.BorderThickness.Left>0&&!_onexPerfConnectedOutline.Connected,"Closed header outline did not return");
    }
    OnexActiveProfile["split"]=JsonValue.CreateBooleanValue(true);RefreshOnexSourceEditors();await Task.Delay(100);
    foreach(var form in new[]{_onexDcEditor,_onexAcEditor})foreach(int index in new[]{0,3,6})
    {
     var card=form.Cards[index];await Focus(card.Header);await Input("a",()=>ReferenceEquals(form.Open,card));ApplyOnexTabPalette();UpdateLayout();await Task.Delay(60);CheckFrame(form.Source=="dc"?_onexDcConnectedOutline:_onexAcConnectedOutline,card.Header,card.Detail,form.Source+"-"+card.Id);
     if(form.Source=="dc"&&index==3)await SaveOnexImage("onex-joined-dc-fps252.png",form.Grid,80);await Focus(card.Close);await Input("a",()=>form.Open==null);Check(card.Header.BorderThickness.Left>0,"Closed source header outline did not return");
    }
    Check((await ReadOnexStateAsync()).GetNamedString("rawLimits")==power.GetNamedString("rawLimits"),"Joined-card UI changed TDP");report["injectedControllerSteps"]=JsonValue.CreateNumberValue(step);report["allNineShapes"]=JsonValue.CreateBooleanValue(cases.Count==9);report["uiFixtureOnlyNoBackendMutation"]=JsonValue.CreateBooleanValue(true);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);report["settingsWritesByFixture"]=JsonValue.CreateNumberValue(0);
   }
   finally
   {
    _onexNavigationAuditHoldingState=false;await SetOnexPerfCardAsync(null);foreach(var form in new[]{_onexDcEditor,_onexAcEditor})if(form?.Open!=null){form.Open.Detail.Visibility=Visibility.Collapsed;form.Open=null;}
    await ReadOnexPerformanceProfilesAsync();ApplyOnexTabPalette();if(original!=null)original.IsChecked=true;await Publish("{\"finished\":true}");
   }
  }
 }
}
