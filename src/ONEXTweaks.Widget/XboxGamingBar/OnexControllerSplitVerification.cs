// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexControllerSplitAsync(JsonObject report)
  {
   void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
   var folder=ApplicationData.Current.LocalFolder;string run=Guid.NewGuid().ToString("N");int step=0;var originalTab=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);string originalSource=ControllerSourceId;bool? originalOem=null;bool preparedByQa=false;
   async Task Write(string file,string value){for(int n=0;;n++){try{File.WriteAllText(Path.Combine(folder.Path,file),value);return;}catch(IOException)when(n<25){await Task.Delay(20);}}}
   async Task Input(string button,FrameworkElement target=null)
   {target=target??FocusManager.GetFocusedElement() as FrameworkElement;Check(target!=null,"Split input target missing");var peer=FrameworkElementAutomationPeer.FromElement(target)??FrameworkElementAutomationPeer.CreatePeerForElement(target);var r=peer.GetBoundingRectangle();double scale=Windows.Graphics.Display.DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel;string id=run+":"+(++step);await Write("onex-gamepad-qa-state.json",new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"button",JsonValue.CreateStringValue(button)},{"x",JsonValue.CreateNumberValue(r.X+r.Width/2+widget.WindowBounds.X*scale)},{"y",JsonValue.CreateNumberValue(r.Y+r.Height/2+widget.WindowBounds.Y*scale)}}.Stringify());bool ack=false;for(int n=0;n<120;n++){await Task.Delay(60);try{ack=await FileIO.ReadTextAsync(await folder.GetFileAsync("onex-gamepad-qa-ack.txt"))==id;}catch{}if(ack)break;}Check(ack,"Split injected input timeout");await Task.Delay(130);}
   async Task Focus(Control control){control.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(120);Check(control.Focus(FocusState.Keyboard),"Split focus failed");await Task.Delay(40);}
   try
   {
    LegionNavItem.IsChecked=true;await ReadOnexControllerAsync();Check(!_onexControllerState.GetNamedBoolean("enabled",false)&&ControllerText(_onexControllerState,"inputMode")=="gamepad","Protect owner active Virtual/keyboard mode during injected-pad test");originalOem=_onexControllerState.GetNamedBoolean("oemEnabled",false);
    Check(_onexControllerSections.Length==6,"Expected six controller cards");var extra=_onexControllerSections.Single(card=>card.Id=="extra");var full=_onexControllerSections.Single(card=>card.Id=="mapping");
    Check(Grid.GetRow(extra.Header)==0&&Grid.GetColumn(extra.Header)==0&&Grid.GetColumn(full.Header)==1&&Grid.GetRow(full.Header)==0,"Two remapping cards are not first");
    Check(((Canvas)_onexControllerExtraDiagram.Child).Children.OfType<Button>().Count()==6&&((Canvas)_onexControllerExtraDiagram.Child).Children.OfType<Button>().All(button=>((string)button.Tag).StartsWith("OEM:")),"Mini diagram must contain exactly six OEM hotspots");
    Check(((Canvas)_onexControllerDiagram.Child).Children.OfType<Button>().Count()==24&&((Canvas)_onexControllerDiagram.Child).Children.OfType<Button>().All(button=>!((string)button.Tag).StartsWith("OEM:")),"Full diagram still contains OEM hotspots");
    Check(!full.Header.IsEnabled&&full.Header.Opacity<.5&&!OnexControllerRows().SelectMany(row=>row).Contains(full.Header),"Hardware full-remap card remains interactive");SetOnexControllerCard("mapping");Check(_onexControllerOpen!="mapping","Hardware card opened programmatically");
    await Focus(extra.Header);await Input("a");Check(_onexControllerOpen=="extra","Extra card did not open");if(!originalOem.Value){await Focus(_onexControllerOemEnabled);await Input("a");for(int n=0;n<120&&_onexControllerBusy;n++)await Task.Delay(40);await ReadOnexControllerAsync();preparedByQa=true;}Check(_onexControllerState.GetNamedBoolean("oemEnabled",false),"Extra listener did not enable");
    var key=_onexControllerDiagramButtons["OEM:M1"];key.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(120);await Input("touch",key);Check(ControllerSourceId=="OEM:M1"&&IsOnexRemapOpen(_onexControllerMappingFlyout),"Mini hotspot did not open its editor");
    Check(new[]{2,3,4}.All(index=>!((ComboBoxItem)_onexControllerKind.Items[index]).IsEnabled)&&new[]{5,6,7,8}.All(index=>((ComboBoxItem)_onexControllerKind.Items[index]).IsEnabled),"Hardware action permissions wrong");Check(_onexControllerExtraHelp.Text.Contains("Hardware Controller")&&_onexControllerExtraHelp.Text.Contains("require Virtual Controller"),"English Hardware explanation missing");
    await Input("b");Check(!IsOnexRemapOpen(_onexControllerMappingFlyout),"Extra editor B close failed");
    // Project only UI DTOs synchronously; no mode/profile commands or capture.
    string real=_onexControllerState.Stringify();
    try{
     var projected=JsonObject.Parse(real);projected["enabled"]=JsonValue.CreateBooleanValue(true);var policy=projected.GetNamedObject("editor").GetNamedObject("policy");policy["useHardwareController"]=JsonValue.CreateBooleanValue(false);policy["gamepadRemappingDisabled"]=JsonValue.CreateBooleanValue(false);ApplyOnexControllerState(projected.Stringify());
     Check(full.Header.IsEnabled&&OnexControllerSourceGroupOn("A"),"Virtual projection did not unlock Full Remapping");Check(new[]{2,3,4,5,6,7,8}.All(index=>((ComboBoxItem)_onexControllerKind.Items[index]).IsEnabled),"Virtual projection did not unlock extra actions");SetOnexControllerCard("mapping");Check(_onexControllerOpen=="mapping"&&OnexControllerRows().SelectMany(row=>row).OfType<Button>().Where(button=>button.Tag is string id&&ControllerInputs.Contains(id)).Count()==24,"Full card lacks24 standard focus targets");Check(!OnexControllerRows().SelectMany(row=>row).OfType<Button>().Any(button=>button.Tag is string id&&id.StartsWith("OEM:")),"OEM focus targets leaked into Full card");policy["useHardwareController"]=JsonValue.CreateBooleanValue(true);ApplyOnexControllerState(projected.Stringify());Check(!full.Header.IsEnabled&&_onexControllerOpen!="mapping","Per-game Hardware exception did not lock Full Remapping");
    }finally{ApplyOnexControllerState(real);}
    await ReadOnexControllerAsync();SetOnexControllerCard("extra");_onexControllerExtraDiagram.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await SaveOnexImage("onex-controller-extra300-diagram.png",_onexControllerExtraDiagram,140);
    SetOnexControllerCard("status");Check(_onexControllerBackendStatus.Text.Contains("usbip2_ude")&&_onexControllerBackendStatus.Text.Contains("VIIPER library")&&_onexControllerBackendStatus.Text.Contains("Windows driver"),"VIIPER installation/service status missing");Check(OnexControllerRows().SelectMany(row=>row).Contains(_onexControllerRefreshStatus),"Refresh status missing from focus graph");
    foreach(string keyName in new[]{"twoSeparateCards","sixOemMiniHotspots","standard24OnlyDiagram","hardwareFullCardDisabled","hardwareNavigationSkipsFull","miniTouchEditor","hardwareSystemActionsOnly","englishHardwareHint","virtualUiProjectionUnlocksAll","perGameHardwareProjectionLocksFull","viiperServiceInstallationStatus","statusRefreshInFocusGraph"})report[keyName]=JsonValue.CreateBooleanValue(true);report["injectedInputSteps"]=JsonValue.CreateNumberValue(step);report["modeChecksEvidence"]=JsonValue.CreateStringValue("Hardware actual; Virtual/per-game exception UI DTO projections only, no production capture");report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);report["ownerProfileEdits"]=JsonValue.CreateNumberValue(0);
   }
   finally
   {
    _onexControllerMappingFlyout?.Hide();if(preparedByQa&&originalOem.HasValue){await ReadOnexControllerAsync();if(_onexControllerState.GetNamedBoolean("oemEnabled",false))await ChangeOnexControllerAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("oemEnable")},{"enabled",JsonValue.CreateBooleanValue(false)}});}
    await ReadOnexControllerAsync();if(originalSource!=null)_onexControllerSource.SelectedItem=_onexControllerSource.Items.OfType<ComboBoxItem>().FirstOrDefault(item=>(string)item.Tag==originalSource);if(_onexControllerOpen!=null)SetOnexControllerCard(null);if(originalTab!=null)originalTab.IsChecked=true;await Write("onex-gamepad-qa-state.json","{\"finished\":true}");
   }
  }
 }
}
