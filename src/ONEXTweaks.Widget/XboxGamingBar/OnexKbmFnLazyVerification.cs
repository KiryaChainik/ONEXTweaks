// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexKbmFnDeferredAsync(JsonObject report)
  {
   int checks=0;
   void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
   Check(!LoadImportedDeviceUi&&HotkeysCard==null&&MsiLedColorCard==null&&TcmSticksCard==null&&TcmGyroCard==null,
    "Unused imported editors constructed");
   await ReadOnexKeyboardMouseAsync();
   Check(_kbmState!=null&&KbmPolicy!=null,"Current keyboard/mouse state unavailable");
   var baseline=_kbmState;
   var priorPending=_onexControllerPending;
   var original=GetVisibleNavigationItems().FirstOrDefault(tab=>tab.IsChecked==true);
   var originalSection=_kbmOpenSection;
   var originalSource=_kbmSource;
   var originalLayer=_kbmLayer;
   bool priorProjection=_kbmReadOnlyProjection,priorSync=_kbmSync;
   try
   {
    _kbmReadOnlyProjection=true;
    _onexKeyboardMouseNav.IsChecked=true;
    SetKbmSettingsCard(_kbmSections[2]);
    var model=JsonObject.Parse(baseline.Stringify());
    model["generation"]=JsonValue.CreateStringValue("readonly-shared-remapping-layers");
    var policy=model.GetNamedObject("policy");
    var main=new JsonObject{{"A",KbmBinding("keyboard","",162,67)},{"B",KbmBinding("keyboard","",27)}};
    var fn=new JsonObject{{"A",KbmBinding("mouse","right")}};
    policy["bindings"]=main;
    policy["fnBindings"]=fn;
    policy["fnStartDouble"]=JsonValue.CreateNullValue();
    policy["startDouble"]=KbmBinding("keyboard","",91,9);
    model["baseBindings"]=main;
    model["fnBindings"]=new JsonObject{{"A",fn.GetNamedObject("A")},{"B",main.GetNamedObject("B")}};
    void Project(string source)
    {
     policy["fnButton"]=JsonValue.CreateStringValue(source);
     model["revision"]=JsonValue.CreateNumberValue(model.GetNamedNumber("revision",0)+1);
     ApplyOnexKeyboardMouseState(model.Stringify());
    }
    Project("OEM:M1");
    int inputs=ControllerInputs.Length+OnexControllerOemSources.Length;
    Check(_kbmDiagrams.Count==1&&_kbmTiles.Count==inputs&&_kbmDiagramKeys.Count==inputs&&
     _kbmFnSummaries.Count==inputs,"Remapping must have one diagram and one tile per source");
    var anchor=_kbmTiles["base:A"];
    var mainValue=_kbmSummaries["base:A"];
    var fnValue=_kbmFnSummaries["base:A"];
    Check(mainValue.Text==OnexKeyboardCaption(new[]{162,67})&&fnValue.Text==OnexStrings.Get("Right click"),
     "Tile does not show both effective actions");
    Check(((SolidColorBrush)mainValue.Foreground).Color!=((SolidColorBrush)fnValue.Foreground).Color,
     "Main and Fn overrides share one accent");
    Check(!KbmHasOverride("B","fn")&&_kbmFnSummaries["base:B"].Text==OnexVirtualKeyDisplayName(27),
     "Inherited Fn action is absent or marked as custom");
    Check(!KbmHasOverride("Start","base",true),"Built-in Start double action incorrectly marked as custom");
    Check(new[]{8,9,13,17,18,27,32,38,48,65,91,106,160,162,164,190,269}
     .Select(OnexVirtualKeyDisplayName).All(label=>!label.StartsWith("0x",StringComparison.Ordinal)),
     "A Windows key label uses hexadecimal");
    Check(OnexVirtualKeyDisplayName(65)=="A"&&OnexVirtualKeyDisplayName(17)=="Ctrl"&&
     OnexVirtualKeyDisplayName(91)=="Win"&&OnexVirtualKeyDisplayName(160)==OnexStrings.Get("Left Shift")&&
     OnexVirtualKeyDisplayName(162)==OnexStrings.Get("Left Ctrl"),"Windows key labels decode the wrong namespace");
    _onexControllerPending=new JsonObject{{"bindings",new JsonObject{{"A",KbmBinding("keyboard","",91,162,65)}}}};
    Check(OnexControllerBindingSummary("A")==OnexKeyboardCaption(new[]{91,162,65}),
     "Gamepad tab does not use the shared keyboard label");
    _onexControllerPending=priorPending;

    OpenKbmEditor("A","base",anchor,true);
    await Task.Delay(120);
    var popup=_kbmFlyout;
    Check(IsOnexRemapOpen(popup)&&_kbmLayer=="base"&&_kbmMainLayerValue.Text==mainValue.Text,"Main editor did not open");
    InvokeLightingButton(_kbmFnLayerButton);
    await Task.Delay(80);
    Check(ReferenceEquals(_kbmFlyout,popup)&&IsOnexRemapOpen(popup)&&_kbmLayer=="fn"&&
     KbmEditorKind=="mouse"&&_kbmFnLayerValue.Text==fnValue.Text,"Fn editor does not switch in place");
    Check((string)_kbmBindingReset.Content==OnexStrings.Get("Use Main action"),"Fn reset does not explain inheritance");
    InvokeLightingButton(_kbmMainLayerButton);
    await Task.Delay(80);
    Check(_kbmLayer=="base"&&KbmEditorKind=="keyboard","Switching back to Main lost the binding");
    Project("");
    InvokeLightingButton(_kbmFnLayerButton);
    await Task.Delay(80);
    Check(_kbmLayer=="fn"&&_kbmFnUnavailableNote.Visibility==Visibility.Visible&&
     ReferenceEquals(anchor,_kbmTiles["base:A"]),"Unassigned Fn prevents editing or adds a second diagram");
    Check(_kbmTuning.Count==0,"Read-only projection queued settings");
    report["sharedFnChecks"]=JsonValue.CreateNumberValue(checks);
   }
   finally
   {
    if(IsOnexRemapOpen(_kbmFlyout))CloseOnexRemapEditor(_kbmFlyout);
    _onexControllerPending=priorPending;
    _kbmSource=originalSource;_kbmLayer=originalLayer;
    _kbmState=null;
    ApplyOnexKeyboardMouseState(baseline.Stringify());
    _kbmSync=priorSync;_kbmReadOnlyProjection=priorProjection;
    SetKbmSettingsCard(originalSection);
    if(original!=null)original.IsChecked=true;
   }
  }
 }
}
