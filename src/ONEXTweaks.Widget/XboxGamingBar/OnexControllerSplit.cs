// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private StackPanel _onexControllerExtraPanel;private Viewbox _onexControllerExtraDiagram;
  private TextBlock _onexControllerExtraName,_onexControllerExtraBinding,_onexControllerExtraHelp,_onexControllerBackendStatus;
  private Button _onexControllerRefreshStatus;
  private bool OnexControllerEditingVirtual=>_onexControllerState?.GetNamedBoolean("enabled",false)==true&&(_onexControllerPending??ControllerEditorPolicy)?.GetNamedBoolean("useHardwareController",false)!=true;
  private void ApplyOnexControllerActionAvailability()
  {
   bool virtualMode=OnexControllerEditingVirtual;
   if(_onexControllerExtraHelp!=null)_onexControllerExtraHelp.Text=virtualMode
    ?global::XboxGamingBar.OnexStrings.Get("Virtual Controller: all supported gamepad, macro, keyboard, mouse and system actions are available. Changes are saved in the selected profile.")
    :global::XboxGamingBar.OnexStrings.Get("Hardware Controller: only Open ONEXTweaks, Windows keyboard and Steam actions are available. Gamepad, macro, keyboard and mouse remaps require Virtual Controller. Saved virtual-only assignments are kept.");
   if(_onexControllerKind==null)return;
   foreach(var item in _onexControllerKind.Items.OfType<ComboBoxItem>().Select((value,index)=>new{value,index}))
   {bool allowed=item.index<2||item.index>=5&&item.index<=8&&ControllerIsOem||item.index>=2&&item.index<=4&&virtualMode;
    if(item.index==0&&ControllerEditingDouble)allowed=false;if(item.index==9)allowed=ControllerIsOem&&ControllerOemId=="KeyboardMouse"&&ControllerEditingDouble&&_onexControllerState.GetNamedBoolean("keyboardDoubleReserved",false);item.value.IsEnabled=allowed;}
   bool ready=App.IsConnected&&_onexControllerState?.ContainsKey("entries")==true;foreach(var control in new Control[]{_onexControllerGamepadMode,_onexControllerPadTarget,_onexControllerKeyboard,_onexControllerRecord,_onexControllerMouse})if(control!=null)control.IsEnabled=ready&&virtualMode;
   if(_onexControllerMacroPanel!=null){foreach(var control in OnexOwnedContent(_onexControllerMacroPanel).OfType<Control>())control.IsEnabled=ready&&virtualMode;UpdateOnexControllerMacroButtons();}

  }
  private void BuildOnexControllerExtraDiagram(StackPanel host)
  {
   var canvas=CreateOnexControllerDeviceArt();
   _onexControllerExtraName=AddOnexControllerSketchLabel(canvas,344,23,OnexDialogAccent);
   _onexControllerExtraBinding=AddOnexControllerSketchLabel(canvas,383,15,OnexSettingsBrush(210,217,226));
   foreach(string id in OnexControllerOemSources)
   {
    var button=AddOnexControllerSketchButton(canvas,id,"OnexExtraDiagram",anchor=>SelectOnexControllerMappingSource(id,anchor,true));
    _onexControllerDiagramButtons.Add(id,button);_onexControllerControls.Add(button);
   }
   _onexControllerExtraDiagram=new Viewbox{Name="OnexControllerExtraDiagram",Child=canvas,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Stretch};host.Children.Add(HostOnexSketch(_onexControllerExtraDiagram));
   _onexControllerExtraHelp=ControllerHelp("");host.Children.Add(_onexControllerExtraHelp);
  }
  private void AddOnexControllerBackendStatus(StackPanel parent)
  {
   _onexControllerBackendStatus=ControllerHelp(global::XboxGamingBar.OnexStrings.Get("Checking VIIPER installation…"));parent.Children.Insert(0,_onexControllerBackendStatus);
   _onexControllerRefreshStatus=new Button{Name="OnexControllerRefreshStatus",Content=global::XboxGamingBar.OnexStrings.Get("Refresh status"),Style=(Style)Resources["StandaloneButtonStyle"]};_onexControllerRefreshStatus.Click+=async(s,e)=>await RefreshOnexControllerInputHealthAsync();parent.Children.Insert(1,_onexControllerRefreshStatus);_onexControllerHealthStatus=ControllerHelp("");parent.Children.Insert(0,_onexControllerHealthStatus);
  }
  private void RefreshOnexControllerBackendStatus()
  {
   if(_onexControllerBackendStatus==null||_onexControllerState==null)return;RefreshOnexControllerInputHealth();var install=_onexControllerState.GetNamedObject("backendInstallation",null);var runtime=_onexControllerState.GetNamedObject("runtime",null);
   bool available=install?.GetNamedBoolean("canUseVirtual",false)==true;
   _onexControllerBackendStatus.Text=global::XboxGamingBar.OnexStrings.Format("VIIPER installation · {0}\nVIIPER library · {1}\nWindows driver · {2}\nDriver service · {3} · {4}{5}\nVirtual controller · {6}\nVIIPER runs inside the ONEXTweaks helper. The Windows service above belongs to its USB driver.",(available?global::XboxGamingBar.OnexStrings.Get("Ready"):global::XboxGamingBar.OnexStrings.Get("Not ready")),(install?.GetNamedBoolean("libraryVerified",false)==true?global::XboxGamingBar.OnexStrings.Get("Installed · verified"):install?.GetNamedBoolean("libraryInstalled",false)==true?global::XboxGamingBar.OnexStrings.Get("Installed · verification failed"):global::XboxGamingBar.OnexStrings.Get("Not installed")),(install?.GetNamedBoolean("driverInstalled",false)==true?global::XboxGamingBar.OnexStrings.Get("Installed"):global::XboxGamingBar.OnexStrings.Get("Not installed")),ControllerText(install,"driverService","usbip2_ude"),ControllerText(install,"driverState","Unavailable"),(install?.GetNamedBoolean("driverDisabled",false)==true?global::XboxGamingBar.OnexStrings.Get(" · Disabled"):""),(runtime?.GetNamedBoolean("running",false)==true?global::XboxGamingBar.OnexStrings.Format("Running · {0}",OnexControllerTargetName(ControllerText(runtime,"outputTarget","Xbox360"))):global::XboxGamingBar.OnexStrings.Get("Not running")));
   if(_onexControllerTransport.Items.Count>1)((ComboBoxItem)_onexControllerTransport.Items[1]).IsEnabled=available||_onexControllerState.GetNamedBoolean("enabled",false);
  }
 }
}
