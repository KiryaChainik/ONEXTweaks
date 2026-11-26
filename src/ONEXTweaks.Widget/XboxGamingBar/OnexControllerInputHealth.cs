// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private TextBlock _onexControllerHealthStatus;private bool _onexControllerHealthRefreshing;
  private static int OnexControllerGamePlayer(JsonObject runtime){double slot=runtime?.GetNamedNumber("observerVirtualSlot",-1)??-1;if(slot<0)slot=runtime?.GetNamedNumber("virtualSlot",-1)??-1;return slot>=0?(int)slot+1:0;}
  private async Task RefreshOnexControllerInputHealthAsync(){
   if(_onexControllerHealthRefreshing||_onexControllerReadOnlyProjection)return;_onexControllerHealthRefreshing=true;_onexControllerRefreshStatus.IsEnabled=false;
   try{var reply=await App.SendMessageAsync(new ValueSet{{"OnexControllerGet",true},{"OnexControllerRefreshInstallation",true}});if(reply?.ContainsKey("OnexControllerState")==true)ApplyOnexControllerState((string)reply["OnexControllerState"]);else _onexControllerHealthStatus.Text=global::XboxGamingBar.OnexStrings.Get("Input check unavailable. Try Refresh status again.");}
   catch(Exception error){_onexControllerHealthStatus.Text=global::XboxGamingBar.OnexStrings.Format("Input check unavailable: {0}",error.Message);}
   finally{_onexControllerHealthRefreshing=false;_onexControllerRefreshStatus.IsEnabled=App.IsConnected;}
  }
  private static string OnexInputHealthIssue(string code){
   switch(code){
    case "sourceMissing":return OnexStrings.Get("No supported built-in controller was found. Reconnect it before enabling capture.");
    case "extraPhysicalSource":return OnexStrings.Get("More than one Xbox-compatible input source was found. Check the extra device or virtual controller before starting capture.");
    case "inverseHideMode":return OnexStrings.Get("HidHide uses inverse mode. Existing configuration is protected; change it manually before enabling capture.");
    case "sourceNotHidden":return OnexStrings.Get("The physical controller is not fully hidden. Games may receive duplicate input; restore physical input, then enable capture again.");
    case "outputMissing":return OnexStrings.Get("The virtual Xbox controller is not visible to games. Restore physical input, then enable Virtual Controller again.");
    case "duplicateXboxInput":return OnexStrings.Get("Multiple Xbox-compatible gamepads are visible. Games may receive duplicate input. Check other input software or extra controllers.");
    case "sonyOutputMissing":return OnexStrings.Get("The owned DualShock 4 output is missing. Restore physical input, then enable Virtual Controller again.");
    case "xboxAlongsideSony":return OnexStrings.Get("An Xbox-compatible gamepad is visible alongside DualShock 4. Check for duplicate input or an extra controller.");
    case "hardwareHidden":return OnexStrings.Get("The built-in controller is not visible to games. Check other input software or reconnect the controller.");
    case "extraVisibleInput":return OnexStrings.Get("More than one Xbox-compatible gamepad is visible. Disconnect extra controllers before enabling capture.");
    default:return OnexStrings.Get("Input visibility could not be measured. Try Refresh status again.");
   }
  }
  private void RefreshOnexControllerInputHealth(){
   if(_onexControllerHealthStatus==null)return;if(_onexControllerState?.GetNamedBoolean("inputHealthSupported",false)!=true){_onexControllerHealthStatus.Text=global::XboxGamingBar.OnexStrings.Get("Input check requires an updated ONEXTweaks helper.");return;}
   var health=OnexMotionObject(_onexControllerState,"inputHealth");if(health==null){_onexControllerHealthStatus.Text=global::XboxGamingBar.OnexStrings.Get("Input visibility · Not checked\nRefresh status checks what games can see. It does not change drivers or input settings.");return;}
   bool stale=health.GetNamedBoolean("stale",false);var issues=health.GetNamedArray("issues",new JsonArray());string mode=ControllerText(health,"mode","Hardware");string stamp=ControllerText(health,"checkedUtc");if(DateTimeOffset.TryParse(stamp,out var date))stamp=date.ToLocalTime().ToString("HH:mm:ss");
   string result=OnexStrings.Format("Input visibility · {0} · {1}",OnexStrings.Get(stale?"Check again":issues.Count==0?"Ready":"Needs attention"),stamp);
   if(stale)result+=OnexStrings.Get("\nPrevious check; refresh to check the current input path.");
   else{
    double count=health.GetNamedNumber("visibleXboxCount",-1);result+=OnexStrings.Format("\nXbox-compatible gamepads visible to games: {0}",count<0?OnexStrings.Get("Unavailable"):count.ToString());
    if(health.GetNamedNumber("gamePlayer",0)>0)result+=OnexStrings.Format(" · Player {0}",health.GetNamedNumber("gamePlayer"));
    if(mode=="DualShock4")result+=OnexStrings.Format("\nOwned DualShock 4 HID: {0}",OnexStrings.Get(health.GetNamedBoolean("ownedSonyPresent",false)?"Present":"Missing"));
    result+="\n"+(mode=="Hardware"?OnexStrings.Get("Hardware mode"):OnexStrings.Format("{0} · Physical controller hidden: {1}",OnexControllerTargetName(mode),OnexStrings.Get(health.GetNamedBoolean("physicalHidden",false)?"Yes":"No")));
    foreach(var issue in issues)result+="\n"+OnexInputHealthIssue(issue.GetString());
   }
   _onexControllerHealthStatus.Text=result;
  }
  private async Task VerifyOnexControllerInputHealthAsync(JsonObject report){
   int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}await FlushOnexControllerPolicyAsync();for(int n=0;n<100&&_onexControllerBusy;n++)await Task.Delay(30);Check(!_onexControllerBusy&&_onexControllerPending==null,"Protect pending owner edits");await ReadOnexControllerAsync();
   var actual=await App.SendMessageAsync(new ValueSet{{"OnexControllerGet",true},{"OnexControllerRefreshInstallation",true}});Check(actual?.ContainsKey("OnexControllerState")==true,"Actual readonly status request failed");ApplyOnexControllerState((string)actual["OnexControllerState"]);var verified=OnexMotionObject(_onexControllerState,"inputHealth");Check(_onexControllerState.GetNamedBoolean("inputHealthSupported",false)&&verified!=null,"Production input health snapshot missing");string checkedAt=verified.GetNamedString("checkedUtc");await Task.Delay(1200);await ReadOnexControllerAsync();Check(_onexControllerState.GetNamedObject("inputHealth").GetNamedString("checkedUtc")==checkedAt,"Idle maintenance discarded the readonly health snapshot");
   string before=ControllerEditorPolicy.Stringify(),open=_onexControllerOpen;var tab=GetVisibleNavigationItems().FirstOrDefault(item=>item.IsChecked==true);bool guard=_onexControllerReadOnlyProjection;
   try{
    _onexControllerReadOnlyProjection=true;LegionNavItem.IsChecked=true;var state=JsonObject.Parse(_onexControllerState.Stringify());state["enabled"]=JsonValue.CreateBooleanValue(true);var runtime=state.GetNamedObject("runtime");runtime["running"]=JsonValue.CreateBooleanValue(true);runtime["outputTarget"]=JsonValue.CreateStringValue("Xbox360");runtime["virtualSlot"]=JsonValue.CreateNumberValue(1);runtime["observerVirtualSlot"]=JsonValue.CreateNumberValue(0);
    var health=new JsonObject{{"checkedUtc",JsonValue.CreateStringValue(DateTimeOffset.UtcNow.ToString("o"))},{"mode",JsonValue.CreateStringValue("Xbox360")},{"physicalSourceCount",JsonValue.CreateNumberValue(1)},{"visibleXboxCount",JsonValue.CreateNumberValue(1)},{"gamePlayer",JsonValue.CreateNumberValue(1)},{"physicalHidden",JsonValue.CreateBooleanValue(true)},{"ownedSonyPresent",JsonValue.CreateBooleanValue(false)},{"stale",JsonValue.CreateBooleanValue(false)},{"issues",new JsonArray()}};state["inputHealth"]=health;
    ApplyOnexControllerState(state.Stringify());SetOnexControllerCard("status");Check(_onexControllerStatus.Text.Contains("Player 1")&&!_onexControllerStatus.Text.Contains("Player 2"),"Status displays whitelisted slot instead of independently observed player");Check(_onexControllerHealthStatus.Text.Contains("Ready")&&_onexControllerHealthStatus.Text.Contains("Player 1"),"Healthy game-view result missing");
    health["visibleXboxCount"]=JsonValue.CreateNumberValue(2);health["issues"]=new JsonArray{JsonValue.CreateStringValue("duplicateXboxInput")};ApplyOnexControllerState(state.Stringify());Check(_onexControllerHealthStatus.Text.Contains("Needs attention")&&_onexControllerHealthStatus.Text.Contains("Multiple Xbox-compatible"),"Duplicate input advice missing");
    health["mode"]=JsonValue.CreateStringValue("DualShock4");health["visibleXboxCount"]=JsonValue.CreateNumberValue(0);health["gamePlayer"]=JsonValue.CreateNumberValue(0);health["ownedSonyPresent"]=JsonValue.CreateBooleanValue(true);health["issues"]=new JsonArray();runtime["outputTarget"]=JsonValue.CreateStringValue("DualShock4");runtime["virtualSlot"]=JsonValue.CreateNumberValue(-1);runtime["observerVirtualSlot"]=JsonValue.CreateNumberValue(-1);ApplyOnexControllerState(state.Stringify());Check(_onexControllerHealthStatus.Text.Contains("Owned DualShock 4 HID: Present")&&!_onexControllerHealthStatus.Text.Contains("Player 0"),"DS4 health lost HID identity or invented an Xbox player");
    _onexControllerHealthStatus.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(100);await SaveOnexImage("onex-controller347-health.png",_onexControllerHealthStatus,60);
    health["stale"]=JsonValue.CreateBooleanValue(true);ApplyOnexControllerState(state.Stringify());Check(_onexControllerHealthStatus.Text.Contains("Previous check")&&!_onexControllerHealthStatus.Text.Contains("Ready"),"Stale snapshot claims current readiness");
    state["inputHealthSupported"]=JsonValue.CreateBooleanValue(false);ApplyOnexControllerState(state.Stringify());Check(_onexControllerHealthStatus.Text.Contains("updated ONEXTweaks helper"),"Old helper exposes unsupported health feature");Check(_onexControllerPending==null,"Health projection queued owner profile mutation");
    report["checks"]=JsonValue.CreateNumberValue(checks);report["actualReadonlyRefresh"]=JsonValue.CreateBooleanValue(true);report["projectionOnlyAfterRefresh"]=JsonValue.CreateBooleanValue(true);report["profileWrites"]=JsonValue.CreateNumberValue(0);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);
   }finally{_onexControllerReadOnlyProjection=guard;await ReadOnexControllerAsync();Check(ControllerEditorPolicy.Stringify()==before,"Health verification changed owner policy");SetOnexControllerCard(open);if(tab!=null)tab.IsChecked=true;}
  }
 }
}
