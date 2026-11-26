// SPDX-License-Identifier: AGPL-3.0-only
using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private RoutedEventHandler _onexSharedGotFocus,_onexSharedLostFocus,_onexSharedUnloaded,_onexSharedLoaded;
  private SizeChangedEventHandler _onexSharedSizeChanged;
  private EventHandler<object> _onexSharedDropOpened,_onexSharedDropClosed;
  private DependencyPropertyChangedCallback _onexSharedSystemFocus;
  private int _onexFocusControlsHooked;
  private void WireOnexFocusEvents(Control control)
  {
   if(_onexFocusControls.TryGetValue(control,out var known))return;
   // One native delegate identity per callback/widget. The routed sender is
   // the control that owns the subscription; no per-control closure is needed.
   if(_onexSharedGotFocus==null)
   {
    _onexSharedGotFocus=(sender,args)=>{var current=(Control)sender;if(current is ComboBox focusedChoice)ConfigureOnexChoiceNavigation(focusedChoice);ConfigureOnexFocus(current,false);FocusOnexOwnedControl(current);};
    _onexSharedLostFocus=(sender,args)=>HideOnexOwnedFocus((Control)sender);
    _onexSharedSizeChanged=(sender,args)=>{if(_onexControlLayers.TryGetValue((Control)sender,out var layer))RefreshOnexFocusLayer(layer);};
    _onexSharedUnloaded=(sender,args)=>{if(ReferenceEquals(sender,_onexOpenChoice))ResetOnexOpenChoice();HideOnexOwnedFocus((Control)sender);};
    _onexSharedLoaded=(sender,args)=>{var current=(Control)sender;if(ReferenceEquals(FocusManager.GetFocusedElement(),current))FocusOnexOwnedControl(current);};
    _onexSharedDropOpened=(sender,args)=>{OpenOnexChoice((ComboBox)sender);HideOnexAllFocusLayers();};
    _onexSharedDropClosed=(sender,args)=>{CloseOnexChoice((ComboBox)sender);FocusOnexOwnedControl((Control)sender);};
    _onexSharedSystemFocus=(sender,property)=>{var current=(Control)sender;if(current.UseSystemFocusVisuals)current.UseSystemFocusVisuals=false;};
   }
   _onexFocusControls.Add(control,new object());_onexFocusControlsHooked++;
   control.GotFocus+=_onexSharedGotFocus;control.LostFocus+=_onexSharedLostFocus;control.SizeChanged+=_onexSharedSizeChanged;
   control.Unloaded+=_onexSharedUnloaded;control.Loaded+=_onexSharedLoaded;
   if(control is ComboBox choice){choice.DropDownOpened+=_onexSharedDropOpened;choice.DropDownClosed+=_onexSharedDropClosed;}
   control.RegisterPropertyChangedCallback(Control.UseSystemFocusVisualsProperty,_onexSharedSystemFocus);
  }
 }
}
