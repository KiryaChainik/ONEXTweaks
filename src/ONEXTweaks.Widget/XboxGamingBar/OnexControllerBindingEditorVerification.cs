using System;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexControllerBindingFirstUseAsync(JsonObject report)
  {
   int checks=0;void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
   async Task Wait(Func<bool> ready,string why){for(int n=0;n<160;n++){if(ready())return;await Task.Delay(25);}throw new TimeoutException(why);}
   Check(_onexControllerMappingFlyout==null&&_onexControllerKind==null&&_onexControllerKeyboard==null&&_onexControllerMacroPanel==null,"Closed controller binding UI eagerly constructed");
   Check(_onexControllerDiagramButtons.Count==30&&_onexControllerInputTiles.Count==30,"Permanent controller diagrams/binding summaries missing");
   var original=GetVisibleNavigationItems().FirstOrDefault(tab=>tab.IsChecked==true);bool previousProjection=_onexControllerReadOnlyProjection;bool previousSync=_onexControllerSync;
   await ReadOnexControllerAsync();string beforePolicy=ControllerEditorPolicy.Stringify();var projection=JsonObject.Parse(_onexControllerState.Stringify());
   int permanentControls=_onexControllerControls.Count;double creationMax=0;bool reopened=false;
   try
   {
    _onexControllerReadOnlyProjection=true;projection["enabled"]=JsonValue.CreateBooleanValue(true);projection["oemEnabled"]=JsonValue.CreateBooleanValue(true);projection.GetNamedObject("editor").GetNamedObject("policy")["useHardwareController"]=JsonValue.CreateBooleanValue(false);ApplyOnexControllerState(projection.Stringify());
    LegionNavItem.IsChecked=true;await Wait(()=>_onexControllerSections[0].Header.IsLoaded,"Controller editor anchor not loaded");
    for(int pass=0;pass<2;pass++)
    {
     var watch=Stopwatch.StartNew();EnsureOnexControllerBindingEditor();creationMax=Math.Max(creationMax,watch.Elapsed.TotalMilliseconds);
     var popup=_onexControllerMappingFlyout;var editor=_onexControllerMappingEditor;
     Check(popup!=null&&_onexControllerKind.Items.Count==11&&_onexControllerKeyboard.Items.Count>=50&&_onexControllerMacroTarget.Items.Count==26,"Incomplete controller first-use action/chord/macro catalog");
     EnsureOnexControllerBindingEditor();Check(ReferenceEquals(popup,_onexControllerMappingFlyout),"Open controller editor duplicated");
     popup.ShowAt(_onexControllerSections[0].Header);await Wait(()=>popup.IsOpen&&_onexControllerKind.IsLoaded,"Actual first-use controller editor did not open");
     Check(_onexControllerKind.Focus(FocusState.Keyboard),"First-use action list cannot focus");
     var view=_onexRemapWindows.Single(item=>ReferenceEquals(item.Flyout,popup));Check(view.Width>0&&view.NaturalHeight>0&&OnexRemapPresenter(editor)?.ActualWidth<=500.5,"First-use controller bounds not prepared");
     _onexControllerSync=true;_onexControllerKind.SelectedIndex=3;UpdateOnexControllerMappingVisibility();_onexControllerKeyboard.IsDropDownOpen=true;await Task.Delay(80);
     Check(_onexControllerKeyboard.Items.Count>=50&&Enumerable.Range(0,_onexControllerKeyboard.Items.Count).Any(index=>_onexControllerKeyboard.ContainerFromIndex(index)!=null),"Controller chord rows not realised");
     _onexControllerKeyboard.IsDropDownOpen=false;_onexControllerSync=previousSync;popup.Hide();await Wait(()=>_onexControllerMappingFlyout==null,"Closed controller editor still retained");
     Check(_onexControllerKind==null&&_onexControllerMacroPanel==null&&_onexControllerControls.Count==permanentControls&&!_onexRemapWindows.Any(item=>ReferenceEquals(item.Editor,editor)),"Closed binding editor retained controls/views");
     Check(_onexControllerDiagramButtons.Count==30&&_onexControllerInputTiles.Count==30&&_onexControllerPending==null,"Transient editor changed permanent controls or queued owner write");
     reopened=pass==1;
    }
    Check(creationMax<150,"Controller first-use construction exceeded150ms");
    report["controllerBindingFirstUseChecks"]=JsonValue.CreateNumberValue(checks);report["controllerBindingCreationMaxMs"]=JsonValue.CreateNumberValue(creationMax);report["controllerBindingReopenVerified"]=JsonValue.CreateBooleanValue(reopened);report["controllerBindingControlsReleasedAfterClose"]=JsonValue.CreateBooleanValue(true);report["controllerBindingProjectionOnly"]=JsonValue.CreateBooleanValue(true);
   }
   finally
   {
    _onexControllerMappingFlyout?.Hide();_onexControllerSync=previousSync;_onexControllerReadOnlyProjection=previousProjection;
    if(!previousProjection){await ReadOnexControllerAsync();Check(ControllerEditorPolicy.Stringify()==beforePolicy,"Actual controller policy changed during first-use UI verification");}
    if(original!=null)original.IsChecked=true;
   }
  }
 }
}
