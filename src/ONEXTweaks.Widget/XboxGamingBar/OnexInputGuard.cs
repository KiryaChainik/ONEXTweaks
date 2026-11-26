// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Runtime.CompilerServices;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace XboxGamingBar
{
    internal static class OnexInputGuard
    {
        private sealed class State { internal bool Pointer, Key, Picker, Percentage, EditVertically, ConfirmHeld, CancelHeld, Wired, Preview, ToggleInput; internal double Original; internal Action Capture,Commit; internal Action<bool> PreviewChanged; internal long LastInput,PickerUntil; }
        private static readonly ConditionalWeakTable<Control, State> states = new ConditionalWeakTable<Control, State>();
        internal static bool IsActive(Control control) =>
            (App.OnexExercisePower && System.IO.File.Exists(System.IO.Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path,"onex-layout-verification-armed.json"))) ||
            (control != null && states.TryGetValue(control,out var state) && !state.Preview && (state.Pointer || state.Key || (control is ComboBox && (state.Picker || DateTimeOffset.UtcNow.Ticks<state.PickerUntil))));
        internal static bool IsPreviewing(Slider slider)=>slider!=null&&states.TryGetValue(slider,out var state)&&state.Preview;
        internal static bool IsPointerActive(Control control)=>control!=null&&states.TryGetValue(control,out var state)&&state.Pointer;
        internal static void VerifyPointerHeld(Slider slider,bool held)
        {
            if(App.OnexTileRevisionStage!=215&&App.OnexTileRevisionStage!=219&&App.OnexTileRevisionStage!=220&&App.OnexTileRevisionStage!=221&&App.OnexTileRevisionStage!=222)throw new InvalidOperationException("Native215/219 diagnostic only");
            Wire(slider);states.GetValue(slider,_=>new State()).Pointer=held;
        }
        internal static bool ConsumeToggleInput(ToggleSwitch control)
        {
            if(control==null||!states.TryGetValue(control,out var state)||!state.ToggleInput)return false;
            state.ToggleInput=false;
            // Native ToggleSwitch commits on release, after our held-pointer/key flag
            // may already be cleared. Consume this gesture once; telemetry is never input.
            return state.Pointer||state.Key||DateTimeOffset.UtcNow.Ticks-state.LastInput<TimeSpan.FromSeconds(1).Ticks;
        }
        internal static bool IsEditing(Slider slider)=>slider!=null&&(IsPreviewing(slider)||slider.IsFocusEngaged||IsActive(slider));
        internal static void SetCommit(Slider slider,Action commit){Wire(slider);states.GetValue(slider,_=>new State()).Commit=commit;}
        internal static void Cancel(Slider slider){if(slider!=null&&states.TryGetValue(slider,out var state))Finish(slider,state,false);}
        internal static void CancelInteraction(Slider slider)
        {
            if(slider==null||!states.TryGetValue(slider,out var state))return;
            Finish(slider,state,false);
            bool release=state.Pointer;
            state.Pointer=state.Key=state.ConfirmHeld=state.CancelHeld=false;
            if(release)slider.ReleasePointerCaptures();
        }
        private static Slider FocusedSlider()
        {
            // Avoid private XAML template parents: native parent marshaling caused
            // the actual CoreCLR access violation captured in crash dump137.
            return FocusManager.GetFocusedElement() as Slider;
        }
        private static void Finish(Slider slider,State state,bool apply)
        {
            if(!state.Preview)return;
            bool changed=Math.Abs(slider.Value-state.Original)>.0001;
            if(!apply)slider.Value=state.Original; // Preview still suppresses every hardware write.
            state.Preview=false;SetEditVisual(slider,state,false);
            if(apply&&changed){state.Key=true;try{state.Commit?.Invoke();}finally{state.Key=false;}}
        }
        private static void SetEditVisual(Slider slider,State state,bool editing)
        {
            if(slider.IsLoaded)VisualStateManager.GoToState(slider,editing?(slider.Orientation==Orientation.Horizontal?"FocusEngagedHorizontal":"FocusEngagedVertical"):"FocusDisengaged",false);
            state.PreviewChanged?.Invoke(editing);
        }
        internal static VirtualKey NormalizeSliderKey(VirtualKey key)
        {
            switch(key)
            {
                case VirtualKey.GamepadA:case VirtualKey.NavigationAccept:case VirtualKey.Enter:return VirtualKey.Space;
                case VirtualKey.GamepadB:case VirtualKey.NavigationCancel:return VirtualKey.Escape;
                case VirtualKey.GamepadDPadUp:case VirtualKey.GamepadLeftThumbstickUp:case VirtualKey.NavigationUp:return VirtualKey.Up;
                case VirtualKey.GamepadDPadDown:case VirtualKey.GamepadLeftThumbstickDown:case VirtualKey.NavigationDown:return VirtualKey.Down;
                case VirtualKey.GamepadDPadLeft:case VirtualKey.GamepadLeftThumbstickLeft:case VirtualKey.NavigationLeft:return VirtualKey.Left;
                case VirtualKey.GamepadDPadRight:case VirtualKey.GamepadLeftThumbstickRight:case VirtualKey.NavigationRight:return VirtualKey.Right;
                default:return key;
            }
        }
        internal static VirtualKey SliderKey(KeyRoutedEventArgs args)
        {
            var original=NormalizeSliderKey(args.OriginalKey);
            switch(original)
            {
                case VirtualKey.Space:case VirtualKey.Escape:
                case VirtualKey.Up:case VirtualKey.Down:case VirtualKey.Left:case VirtualKey.Right:return original;
                default:return NormalizeSliderKey(args.Key);
            }
        }
        private static int Vertical(VirtualKey key)
        {
            return key==VirtualKey.Up?-1:key==VirtualKey.Down?1:0;
        }
        internal static void ConfigurePercentage(Slider slider, bool editVertically = false)
        {
            Wire(slider);slider.StepFrequency=1;slider.SmallChange=5;slider.LargeChange=5;
            var state = states.GetValue(slider, _ => new State());
            state.Percentage = true;
            state.EditVertically = editVertically;
        }
        internal static bool HandleFocusedSlider(KeyRoutedEventArgs args,Slider routedSlider=null)
        {
            if(args.Handled)return false;
            var slider=routedSlider??FocusedSlider();
            if(!HandleSliderKey(slider,SliderKey(args),args.KeyStatus.WasKeyDown))return false;
            args.Handled=true;
            return true;
        }
        internal static void SetPreviewVisual(Slider slider, Action<bool> update)
        { Wire(slider); states.GetValue(slider,_=>new State()).PreviewChanged=update; }
        internal static bool AcceptSliderKeyUp(Slider slider, VirtualKey key)
        {
            if(slider==null||!states.TryGetValue(slider,out var state))return false;
            switch(NormalizeSliderKey(key))
            {
                case VirtualKey.Space:bool confirm=state.ConfirmHeld;state.ConfirmHeld=false;return confirm;
                case VirtualKey.Escape:bool cancel=state.CancelHeld;state.CancelHeld=false;return cancel;
                default:return false;
            }
        }
        internal static bool HandleSliderKey(Slider slider,VirtualKey key,bool repeated)
        {
            if(slider==null||!slider.IsEnabled)return false;
            Wire(slider);var state=states.GetValue(slider,_=>new State());
            key=NormalizeSliderKey(key);
            if(key==VirtualKey.Space)
            {
                state.ConfirmHeld=true;
                if(!repeated)
                {
                    if(!ReferenceEquals(FocusManager.GetFocusedElement(),slider))slider.Focus(FocusState.Keyboard);
                    if(state.Preview)Finish(slider,state,true);
                    else{state.Original=slider.Value;state.Preview=true;SetEditVisual(slider,state,true);}
                }
                return true;
            }
            if(key==VirtualKey.Escape)
            {
                // Consume repeats after the first B cancels, until that physical press is released.
                if(!state.Preview)return repeated&&state.CancelHeld;
                state.CancelHeld=true;
                Finish(slider,state,false);
                return true;
            }
            int verticalDirection = Vertical(key);
            int horizontalDirection = key == VirtualKey.Left ? -1 : key == VirtualKey.Right ? 1 : 0;
            if (!state.Preview || (verticalDirection == 0 && horizontalDirection == 0))
                return false;

            int adjustment;
            if (state.EditVertically)
            {
                // Keep the current point selected until the user confirms or cancels.
                if (verticalDirection == 0)
                    return true;
                adjustment = -verticalDirection;
            }
            else
            {
                if (verticalDirection != 0)
                {
                    Finish(slider, state, true);
                    return false;
                }
                adjustment = horizontalDirection;
            }
            double step=state.Percentage?5:Math.Max(slider.SmallChange,slider.StepFrequency);
            // Consume at the bounds too: never navigate out while editing.
            slider.Value=Math.Max(slider.Minimum,Math.Min(slider.Maximum,slider.Value+adjustment*step));
            SetEditVisual(slider,state,true);
            return true;
        }
        internal static void Wire(Control control, Action capturePointerValue = null)
        {
            if(control == null)return;
            var state = states.GetValue(control,_=>new State());
            if(capturePointerValue!=null){state.Capture=capturePointerValue;state.Commit=capturePointerValue;}
            if(state.Wired)return;state.Wired=true;
            if(control is Slider slider)
            {
                // Game Bar delivers keyboard-mapped controller input. Native focus
                // disengagement restores Slider's own entry value and discards our
                // selected value before the commit callback. Own the edit session.
                slider.IsFocusEngagementEnabled=false;
                control.PreviewKeyDown+=(sender,args)=>
                {
                    if(HandleFocusedSlider(args,slider)||args.Handled)return;
                    var key=SliderKey(args);
                    int vertical=Vertical(key);
                    bool left=key==VirtualKey.Left;
                    bool right=key==VirtualKey.Right;
                    if(vertical==0&&!left&&!right)return;
                    // Page-specific navigation has already had its chance. A popup or
                    // boundary still must consume directions rather than alter the value.
                    args.Handled=true;
                    var target=(vertical<0?slider.XYFocusUp:vertical>0?slider.XYFocusDown:left?slider.XYFocusLeft:slider.XYFocusRight) as Control;
                    if(target?.IsEnabled==true)target.Focus(FocusState.Keyboard);
                    else FocusManager.TryMoveFocus(vertical<0?FocusNavigationDirection.Up:vertical>0?FocusNavigationDirection.Down:left?FocusNavigationDirection.Left:FocusNavigationDirection.Right);
                };
                control.PreviewKeyUp+=(sender,args)=>
                {
                    if(AcceptSliderKeyUp(slider,SliderKey(args)))args.Handled=true;
                };
            }
            control.AddHandler(UIElement.PointerPressedEvent,new PointerEventHandler((sender,args)=>{
                bool fromPreview=state.Preview;state.Preview=false;
                if(control is Slider touched){if(fromPreview)SetEditVisual(touched,state,false);if(touched.IsFocusEngaged)touched.IsFocusEngaged=false;}
                state.Pointer=true;state.LastInput=DateTimeOffset.UtcNow.Ticks;if(control is ToggleSwitch)state.ToggleInput=true;if(control is ComboBox opened&&opened.IsDropDownOpen)state.Picker=true;
                // Capture the first tap too: the native Slider class may update its Value
                // before the managed PointerPressed handler runs.
                if(state.Capture!=null)state.Capture();else if(fromPreview)state.Commit?.Invoke();
            }),true);
            PointerEventHandler release=(sender,args)=>{if(control is ToggleSwitch&&state.Pointer)state.LastInput=DateTimeOffset.UtcNow.Ticks;state.Pointer=false;};
            control.AddHandler(UIElement.PointerReleasedEvent,release,true);
            control.AddHandler(UIElement.PointerCanceledEvent,release,true);
            control.AddHandler(UIElement.PointerCaptureLostEvent,release,true);
            control.PreviewKeyDown+=(sender,args)=>{
                if(control is ToggleSwitch&&!args.KeyStatus.WasKeyDown&&(args.Key==VirtualKey.GamepadA||args.Key==VirtualKey.Space||args.Key==VirtualKey.Enter))state.ToggleInput=true;
                switch(args.Key){case VirtualKey.Left:case VirtualKey.Right:case VirtualKey.Up:case VirtualKey.Down:
                    case VirtualKey.Home:case VirtualKey.End:case VirtualKey.PageUp:case VirtualKey.PageDown:
                    case VirtualKey.Space:case VirtualKey.Enter:
                    case VirtualKey.GamepadDPadLeft:case VirtualKey.GamepadDPadRight:case VirtualKey.GamepadDPadUp:case VirtualKey.GamepadDPadDown:
                    case VirtualKey.GamepadLeftThumbstickLeft:case VirtualKey.GamepadLeftThumbstickRight:case VirtualKey.GamepadLeftThumbstickUp:case VirtualKey.GamepadLeftThumbstickDown:
                    case VirtualKey.GamepadA:state.Key=true;state.LastInput=DateTimeOffset.UtcNow.Ticks;break;}
            };
            control.AddHandler(UIElement.KeyUpEvent,new KeyEventHandler((sender,args)=>{if(control is ToggleSwitch&&state.Key)state.LastInput=DateTimeOffset.UtcNow.Ticks;state.Key=false;}),true);
            if(control is ComboBox picker)
            {
                picker.DropDownOpened+=(sender,args)=>state.Picker=state.Pointer||state.Key||DateTimeOffset.UtcNow.Ticks-state.LastInput<TimeSpan.FromSeconds(1).Ticks;
                picker.DropDownClosed+=(sender,args)=>{if(state.Picker)state.PickerUntil=DateTimeOffset.UtcNow.AddMilliseconds(250).Ticks;state.Picker=false;};
            }
            control.LostFocus+=(sender,args)=>{if(control is Slider lost&&state.Preview&&!ReferenceEquals(FocusedSlider(),lost))Finish(lost,state,false);state.Key=false;state.Pointer=false;state.ConfirmHeld=state.CancelHeld=false;};
            control.Unloaded+=(sender,args)=>{if(control is Slider lost&&state.Preview)Finish(lost,state,false);state.Key=false;state.Pointer=false;state.ConfirmHeld=state.CancelHeld=false;state.Picker=false;state.PickerUntil=0;};
        }
    }
}
