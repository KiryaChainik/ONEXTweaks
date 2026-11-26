// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexHomeProfilePersistenceAsync(JsonObject report)
  {
   void Check(bool ok,string text){if(!ok)throw new InvalidOperationException(text);}
   await ReadOnexPerformanceProfilesAsync();await ReadOnexFanProfilesAsync();await ReadOnexScreenProfilesAsync();
   if(_onexProfilesState.GetNamedString("selection")!="auto"||_onexProfilesState.GetNamedBoolean("manualOverride",false))
   {
    var previous=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);
    try
    {
     QuickNavItem.IsChecked=true;var request=new Windows.Foundation.Collections.ValueSet{{"OnexUserInput",true},{"OnexFpsSet","{}"}};MarkOnexHomeProfileOverride(request);Check(request.ContainsKey("OnexPreferActiveProfile")&&(bool)request["OnexPreferActiveProfile"],"Home input origin is not marked for active profile persistence");OnexFrontend.PrepareMessage(request);Check((string)request["OnexProfileId"]==OnexFrontend.ProfileId,"Home write scope is stale");
     var screenRequest=new Windows.Foundation.Collections.ValueSet{{"OnexDisplayProfilesSet","{}"}};OnexFrontend.PrepareMessage(screenRequest);Check(!screenRequest.ContainsKey("OnexProfileId"),"Independent Screen request is incorrectly scoped to Performance");var fanRequest=new Windows.Foundation.Collections.ValueSet{{"OnexFanProfilesSet","{}"}};OnexFrontend.PrepareMessage(fanRequest);Check((string)fanRequest["OnexProfileId"]==OnexFrontend.ProfileId,"Fan binding lacks active Performance scope guard");
     var currentPower=await ReadOnexStateAsync();ApplyOnexReleasePowerState(currentPower.Stringify());ApplyOnexTileAvailability();Check(_releaseHome.PowerInput.Value==currentPower.GetNamedNumber("pl1Watts")&&TDPSlider.Value==currentPower.GetNamedNumber("pl1Watts"),"Home and Performance actual TDP differ");Check(qsTileMap["Profile"].StateText.Text==OnexProfileCaption(OnexActiveProfile)&&qsTileMap["OnexFanMode"].StateText.Text==OnexFanProfileCaption&&qsTileMap["ReShade"].StateText.Text==OnexScreenProfileCaption,"Current profile tile captions are stale");
     report["manualOwnerSelectionPreserved"]=JsonValue.CreateBooleanValue(true);report["homeRequestOriginAndScopeGuards"]=JsonValue.CreateBooleanValue(true);report["currentProfileAndPowerViewsSynchronized"]=JsonValue.CreateBooleanValue(true);report["screenProfilesIndependent"]=JsonValue.CreateBooleanValue(_onexScreenProfiles.GetNamedBoolean("sourceIndependent",false));report["realHomeTdpCommitIntoQaProfile"]=JsonValue.CreateBooleanValue(false);report["hardwareQaSkippedBecauseManualOwnerSelection"]=JsonValue.CreateBooleanValue(true);report["hardwareSettingsWritesByFixture"]=JsonValue.CreateNumberValue(0);report["ownerPhysicalInputTested"]=JsonValue.CreateBooleanValue(false);
    }
    finally{if(previous!=null)previous.IsChecked=true;}
    return;
   }
   Check(OnexPerformancePendingDetails().Length==0,"Pending owner edits are protected");
   var originalTab=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);var power=await ReadOnexStateAsync();string originalId=OnexFrontend.ProfileId;string ownerEntries=_onexProfilesState.GetNamedArray("entries").Stringify();string screen=_onexScreenProfiles.GetNamedArray("entries").Stringify();int before=(int)power.GetNamedNumber("pl1Watts"),before2=(int)power.GetNamedNumber("pl2Watts");
   string run=Guid.NewGuid().ToString("N"),name="ONEXQA HomeProfile265 "+run.Substring(0,10),own=null;string manifest=Path.Combine(ApplicationData.Current.LocalFolder.Path,"onex-home-profile-qa-owned.json");
   Check(!File.Exists(manifest),"Unfinished owned QA is protected; recovery must run first");var beforeIds=new JsonArray();foreach(var entry in OnexProfiles)beforeIds.Add(entry["id"]);var evidence=new JsonObject{{"run",JsonValue.CreateStringValue(run)},{"name",JsonValue.CreateStringValue(name)},{"beforeIds",beforeIds},{"selectionBefore",JsonValue.CreateStringValue("auto")}};File.WriteAllText(manifest,evidence.Stringify());
   try
   {
    await ChangeOnexPerformanceProfileAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("create")},{"name",JsonValue.CreateStringValue(name)}});own=OnexFrontend.ProfileId;evidence["id"]=JsonValue.CreateStringValue(own);File.WriteAllText(manifest,evidence.Stringify());Check(!beforeIds.Any(id=>id.GetString()==own),"QA identity is not new");
    QuickNavItem.IsChecked=true;int requested=before<40?before+1:before-1;await SetOnexReleasePowerAsync(requested,Math.Max(requested,before2));await ReadOnexPerformanceProfilesAsync();var actual=await ReadOnexStateAsync();var selected=OnexActiveProfile;bool onAc=_onexProfilesState.GetNamedBoolean("onAc",false);var values=selected.GetNamedObject(onAc?"effectiveAc":"effectiveDc");
    Check(OnexFrontend.ProfileId==own&&actual.GetNamedNumber("pl1Watts")==requested&&values.GetNamedNumber("pl1")==requested,"Home TDP did not apply and persist in active QA profile");Check(_releaseHome.PowerInput.Value==requested&&TDPSlider.Value==requested,"Home and Performance power controls are not synchronized");
    var untouched=new JsonArray();foreach(var entry in OnexProfiles.Where(entry=>entry.GetNamedString("id")!=own))untouched.Add(entry);Check(untouched.Stringify()==ownerEntries,"Home edit changed an existing owner profile");
    await ChangeOnexPerformanceProfileAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("select")},{"id",JsonValue.CreateStringValue(originalId)}});await OnexPerformanceTileCatalog().Change(new JsonObject{{"operation",JsonValue.CreateStringValue("select")},{"id",JsonValue.CreateStringValue(own)}});actual=await ReadOnexStateAsync();Check(actual.GetNamedNumber("pl1Watts")==requested&&_releaseHome.PowerInput.Value==requested&&TDPSlider.Value==requested,"Reselecting QA through Home selection callback did not reload saved TDP into both views");
    ApplyOnexTileAvailability();Check(qsTileMap["Profile"].StateText.Text==OnexProfileCaption(OnexActiveProfile),"Performance profile tile caption is stale");Check(qsTileMap["CPUBoost"].StateText.Text==(_onexBoostOn?"On":"Off")&&qsTileMap["OnexFanMode"].StateText.Text==OnexFanProfileCaption&&qsTileMap["ReShade"].StateText.Text==OnexScreenProfileCaption,"Loaded profile/tile states disagree");
    Check(_onexScreenProfiles.GetNamedBoolean("sourceIndependent",false)&&_onexScreenProfiles.GetNamedArray("entries").Stringify()==screen,"Performance changes modified independent Screen profiles");
    report["realHomeTdpCommitIntoQaProfile"]=JsonValue.CreateBooleanValue(true);report["actualPowerDeltaWatts"]=JsonValue.CreateNumberValue(requested-before);report["profileReselectionReappliesStoredValue"]=JsonValue.CreateBooleanValue(true);report["homePerformanceControlsAndTileCaptionsSynchronized"]=JsonValue.CreateBooleanValue(true);report["ownerProfileDataUntouched"]=JsonValue.CreateBooleanValue(true);report["screenProfilesIndependent"]=JsonValue.CreateBooleanValue(true);report["ownerPhysicalInputTested"]=JsonValue.CreateBooleanValue(false);report["injectedControllerInput"]=JsonValue.CreateBooleanValue(false);
   }
   finally
   {
    await ReadOnexPerformanceProfilesAsync();bool hasQa=own!=null&&OnexProfiles.Any(entry=>entry.GetNamedString("id")==own&&entry.GetNamedString("name")==name);
    if(hasQa)
    {
     if(OnexFrontend.ProfileId==own)await ChangeOnexPerformanceProfileAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("automatic")}});
     await ChangeOnexPerformanceProfileAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("delete")},{"id",JsonValue.CreateStringValue(own)}});
     if(_onexProfilesState.GetNamedString("selection")=="manual"&&OnexFrontend.ProfileId==originalId)await ChangeOnexPerformanceProfileAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("automatic")}});
    }
    await ReadOnexPerformanceProfilesAsync();var final=await ReadOnexStateAsync();report["rawPowerRestored"]=JsonValue.CreateBooleanValue(final.GetNamedNumber("pl1Watts")==before&&final.GetNamedNumber("pl2Watts")==before2);if(File.Exists(manifest)&&!OnexProfiles.Any(entry=>entry.GetNamedString("name")==name))File.Delete(manifest);if(originalTab!=null)originalTab.IsChecked=true;
   }
   Check(report.GetNamedBoolean("rawPowerRestored",false),"Original raw power was not restored by original profile selection");report["ownedQaDeleted"]=JsonValue.CreateBooleanValue(true);
  }
 }
}
