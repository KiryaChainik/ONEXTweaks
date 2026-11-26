// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private Border _onexControllerCaptureCard;private readonly CheckBox[] _onexControllerCaptureBoxes=new CheckBox[4];
  private static readonly string[] ControllerCaptureKeys={"mappings","nintendoLayout","vibration","gameLighting"};
  private Border CreateOnexControllerCaptureCard(){
   var body=new StackPanel{Spacing=10};body.Children.Add(new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("Settings stored in this profile"),FontSize=18,FontWeight=Windows.UI.Text.FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});
   body.Children.Add(ControllerHelp(global::XboxGamingBar.OnexStrings.Get("Checked: use this profile’s settings. Unchecked: follow Global Profile; editing that setting updates Global. Separate values are kept for when you check it again.")));
   string[] titles={OnexStrings.Get("Button mappings"),OnexStrings.Get("Nintendo layout"),"Vibration",OnexStrings.Get("Game lighting")};
   for(int n=0;n<4;n++){var box=new CheckBox{Name="OnexControllerCapture"+n,Content=OnexStrings.Get(titles[n]),IsChecked=true};OnexInputGuard.Wire(box);OnexAccentResources(box,OnexPalette.Violet);_onexControllerCaptureBoxes[n]=box;_onexControllerControls.Add(box);box.Click+=async(s,e)=>{if(_onexControllerSync||_onexControllerReadOnlyProjection||ControllerEditorId=="global")return;var groups=new JsonObject();for(int k=0;k<4;k++)groups[ControllerCaptureKeys[k]]=JsonValue.CreateBooleanValue(_onexControllerCaptureBoxes[k].IsChecked==true);await ControllerActionAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("captureGroups")},{"id",JsonValue.CreateStringValue(ControllerEditorId)},{"groups",groups}});};}
   body.Children.Add(OnexSettingsColumns(_onexControllerCaptureBoxes[0],_onexControllerCaptureBoxes[1]));body.Children.Add(OnexSettingsColumns(_onexControllerCaptureBoxes[2],_onexControllerCaptureBoxes[3]));body.Children.Add(ControllerHelp(global::XboxGamingBar.OnexStrings.Get("Controller type, stick/trigger tuning and gyro settings always stay in this profile. Game lighting requires Virtual Controller + DualShock 4.")));
   return _onexControllerCaptureCard=OnexSettingsCard(body);
  }
  private void RefreshOnexControllerCaptureGroups(){
   if(_onexControllerCaptureCard==null)return;bool available=_onexControllerState?.GetNamedBoolean("captureGroupsSupported",false)==true&&ControllerEditorId!="global";_onexControllerCaptureCard.Visibility=available?Visibility.Visible:Visibility.Collapsed;
   var groups=_onexControllerState?.GetNamedObject("editor",null)?.GetNamedObject("captureGroups",null);
   for(int n=0;n<4;n++){_onexControllerCaptureBoxes[n].IsChecked=groups?.GetNamedBoolean(ControllerCaptureKeys[n],true)??true;_onexControllerCaptureBoxes[n].IsEnabled=available&&App.IsConnected&&!_onexControllerBusy;}
  }
  private async Task VerifyOnexControllerCaptureGroupsAsync(JsonObject report){
   int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
   await FlushOnexControllerPolicyAsync();for(int n=0;n<100&&_onexControllerBusy;n++)await Task.Delay(30);Check(!_onexControllerBusy&&_onexControllerPending==null,"Protect pending owner settings");await ReadOnexControllerAsync();string before=ControllerEditorPolicy.Stringify(),open=_onexControllerOpen;var tab=GetVisibleNavigationItems().FirstOrDefault(item=>item.IsChecked==true);bool guard=_onexControllerReadOnlyProjection;
   try{
    _onexControllerReadOnlyProjection=true;LegionNavItem.IsChecked=true;SetOnexControllerCard(null);var state=JsonObject.Parse(_onexControllerState.Stringify());state["captureGroupsSupported"]=JsonValue.CreateBooleanValue(true);ApplyOnexControllerState(state.Stringify());Check(ControllerEditorId=="global"||_onexControllerCaptureCard.Visibility==Visibility.Visible,"Existing non-Global scope card is missing");
    var editor=state.GetNamedObject("editor");editor["id"]=JsonValue.CreateStringValue("qa-capture-projection");editor["name"]=JsonValue.CreateStringValue("QA capture scope");state["editorId"]=editor["id"];state["activeId"]=editor["id"];state["appliedId"]=editor["id"];state["activeName"]=editor["name"];
    var entry=JsonObject.Parse(state.GetNamedArray("entries").First().GetObject().Stringify());entry["id"]=editor["id"];entry["name"]=editor["name"];var raw=JsonObject.Parse(editor.GetNamedObject("policy").Stringify());raw["bindings"]=new JsonObject();entry["dc"]=raw;var effective=JsonObject.Parse(raw.Stringify());effective["bindings"]=new JsonObject{{"A",new JsonObject{{"kind",JsonValue.CreateStringValue("gamepad")},{"target",JsonValue.CreateStringValue("B")}}},{"B",new JsonObject{{"kind",JsonValue.CreateStringValue("gamepad")},{"target",JsonValue.CreateStringValue("A")}}}};entry["effectiveDc"]=effective;state.GetNamedArray("entries").Add(entry);
    var groups=new JsonObject{{"mappings",JsonValue.CreateBooleanValue(false)},{"nintendoLayout",JsonValue.CreateBooleanValue(true)},{"vibration",JsonValue.CreateBooleanValue(false)},{"gameLighting",JsonValue.CreateBooleanValue(true)}};editor["captureGroups"]=groups;ApplyOnexControllerState(state.Stringify());UpdateLayout();await Task.Delay(100);
    Check(_onexControllerCaptureCard.Visibility==Visibility.Visible,"Non-Global capture group card hidden");Check(_onexControllerSavedProfiles.Rows["qa-capture-projection"].Details.Text.Contains("2 remaps"),"Saved Profiles displays dormant values instead of effective inherited settings");for(int n=0;n<4;n++){var box=_onexControllerCaptureBoxes[n];Check(box.IsChecked==(n==1||n==3),"Scope checkbox projected wrong group");Check(box.IsEnabled&&OnexControllerRows().SelectMany(row=>row).Contains(box),"Scope checkbox unavailable or omitted from navigation");}
    WireOnexControllerNavigation();Check(ReferenceEquals(_onexControllerCaptureBoxes[0].XYFocusRight,_onexControllerCaptureBoxes[1])&&ReferenceEquals(_onexControllerCaptureBoxes[0].XYFocusDown,_onexControllerCaptureBoxes[2]),"Scope focus grid is inconsistent");Check(ReferenceEquals(_onexControllerCaptureBoxes[2].XYFocusDown,_onexControllerSavedProfiles.Header),"Scope does not lead to Saved Profiles");
    Check(_onexControllerCaptureBoxes[0].Focus(FocusState.Keyboard),"Capture group checkbox cannot focus");_onexControllerCaptureCard.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(100);await SaveOnexImage("onex-controller346-capture.png",_onexControllerCaptureCard,50);
    state["captureGroupsSupported"]=JsonValue.CreateBooleanValue(false);ApplyOnexControllerState(state.Stringify());Check(_onexControllerCaptureCard.Visibility==Visibility.Collapsed&&!OnexControllerRows().SelectMany(row=>row).Contains(_onexControllerCaptureBoxes[0]),"Older helper exposes unsupported capture controls");
    state["captureGroupsSupported"]=JsonValue.CreateBooleanValue(true);editor["id"]=JsonValue.CreateStringValue("global");state["editorId"]=editor["id"];ApplyOnexControllerState(state.Stringify());Check(_onexControllerCaptureCard.Visibility==Visibility.Collapsed,"Global can inherit itself");Check(_onexControllerPending==null,"Read-only scope projection queued an owner mutation");
    report["checks"]=JsonValue.CreateNumberValue(checks);report["projectionOnly"]=JsonValue.CreateBooleanValue(true);report["profileWrites"]=JsonValue.CreateNumberValue(0);report["ownerPhysicalInputTested"]=JsonValue.CreateBooleanValue(false);
   }finally{_onexControllerReadOnlyProjection=guard;await ReadOnexControllerAsync();Check(ControllerEditorPolicy.Stringify()==before,"Scope projection changed owner policy");SetOnexControllerCard(open);if(tab!=null)tab.IsChecked=true;}
  }
 }
}
