using System;
using System.Linq;
using Windows.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Markup;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private ComboBox _onexOpenChoice;
  private int _onexChoicePageGuardsSkipped,_onexChoiceCursor=-1,_onexChoiceMoved,_onexChoiceCommits,_onexChoiceCancels,_onexChoiceNativeCommits;
  private VirtualKey _onexChoiceRelease;
  private ItemsPanelTemplate _onexChoicePanel;
  private ComboBoxItem _onexChoiceCursorRow;
  private Border _onexChoiceCursorBorder;
  private Border FindOnexChoiceCursorBorder(ComboBoxItem row)
  {
   row.ApplyTemplate();if(VisualTreeHelper.GetChildrenCount(row)==0)return null;
   var root=VisualTreeHelper.GetChild(row,0) as Grid;
   return root?.Name=="OnexChoiceRoot"?root.Children.OfType<Border>().FirstOrDefault(child=>child.Name=="OwnKeyboardCursor"):null;
  }
  private void ClearOnexChoiceCursor()
  {
   if(_onexChoiceCursorBorder!=null)_onexChoiceCursorBorder.Visibility=Visibility.Collapsed;
   _onexChoiceCursorRow=null;
   _onexChoiceCursorBorder=null;
  }
  private void OnexChoiceFocusChanged(object sender,FocusManagerGotFocusEventArgs args)=>UpdateOnexChoiceFocus();
  private void UpdateOnexChoiceFocus()
  {
   // The popup has its own visual tree. Follow the real focus independently
   // of selection and of the platform's ComboBoxItem visual states.
   var choice=_onexOpenChoice;
   var row=FocusManager.GetFocusedElement() as ComboBoxItem;
   if(choice==null||!choice.IsDropDownOpen||row==null||
      (choice.IndexFromContainer(row)<0&&!choice.Items.Contains(row)))
   {
    ClearOnexChoiceCursor();
    return;
   }
   var border=FindOnexChoiceCursorBorder(row);
   if(ReferenceEquals(border,_onexChoiceCursorBorder))return;
   ClearOnexChoiceCursor();
   if(border==null)return;
   _onexChoiceCursorRow=row;
   _onexChoiceCursorBorder=border;
   border.Visibility=Visibility.Visible;
  }
  private void ResetOnexOpenChoice()
  {
   if(_onexOpenChoice==null)return;
   FocusManager.GotFocus-=OnexChoiceFocusChanged;
   ClearOnexChoiceCursor();
   _onexOpenChoice=null;
   _onexChoiceOriginal=null;
   _onexChoiceCursor=-1;
  }
  private object _onexChoiceOriginal;
  private bool OnexAppearanceChoicePreviewing(ComboBox choice)=>OnexFrontend.UsesOwnBackend&&OnexOwnsAppearanceChoice(choice)&&choice.IsDropDownOpen;
  private bool OnexOwnsAppearanceChoice(ComboBox choice)=>choice!=null&&(choice.Name=="ThemeComboBox"||choice.Name=="AppFontComboBox"||choice.Name=="OnexLanguageChoice");
  private void ConfigureOnexChoiceNavigation(ComboBox choice)
  {
   if(!OnexOwnsAppearanceChoice(choice)||!OnexFrontend.UsesOwnBackend)return;
   // These small fixed catalogs need concrete focusable rows. Do not change the
   // panel of large action catalogs or recreate any popup while it is open.
   if(_onexChoicePanel==null)_onexChoicePanel=(ItemsPanelTemplate)XamlReader.Load("<ItemsPanelTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><StackPanel/></ItemsPanelTemplate>");
   if(!choice.IsDropDownOpen&&!ReferenceEquals(choice.ItemsPanel,_onexChoicePanel))choice.ItemsPanel=_onexChoicePanel;
  }
  private void OpenOnexChoice(ComboBox choice)
  {
   ResetOnexOpenChoice();
   _onexOpenChoice=choice;
   _onexChoiceOriginal=OnexOwnsAppearanceChoice(choice)?choice.SelectedItem:null;
   _onexChoiceCursor=Math.Max(0,choice.SelectedIndex);
   FocusManager.GotFocus+=OnexChoiceFocusChanged;
   _=Dispatcher.RunAsync(CoreDispatcherPriority.Low,()=>
   {
    if(!ReferenceEquals(_onexOpenChoice,choice)||!choice.IsDropDownOpen)return;
    if(OnexOwnsAppearanceChoice(choice))FocusOnexChoiceCursor(choice);
    else UpdateOnexChoiceFocus();
   });
  }
  private void CloseOnexChoice(ComboBox choice)
  {
   object original=_onexChoiceOriginal,selection=choice.SelectedItem;
   if(ReferenceEquals(_onexOpenChoice,choice))ResetOnexOpenChoice();
   // Native mouse/touch selection also changes SelectedItem before the popup
   // closes. Preserve that input path when deferring appearance restyling.
   if(OnexFrontend.UsesOwnBackend&&OnexOwnsAppearanceChoice(choice)&&!ReferenceEquals(original,selection))
    _=Dispatcher.RunAsync(CoreDispatcherPriority.Low,()=>
    {
     if(!choice.IsLoaded||choice.IsDropDownOpen||!ReferenceEquals(choice.SelectedItem,selection))return;
     _onexChoiceNativeCommits++;
     if(ReferenceEquals(choice,ThemeComboBox))ThemeComboBox_SelectionChanged(choice,null);
     else if(ReferenceEquals(choice,AppFontComboBox))AppFontComboBox_SelectionChanged(choice,null);
    });
  }
  private void FocusOnexChoiceCursor(ComboBox choice)
  {
   if(_onexChoiceCursor<0||_onexChoiceCursor>=choice.Items.Count)return;
   var row=choice.ContainerFromIndex(_onexChoiceCursor) as ComboBoxItem??choice.Items[_onexChoiceCursor] as ComboBoxItem;
   if(row==null||!row.IsEnabled)return;
   ConfigureOnexFocus(row);row.IsTabStop=true;row.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});
   if(row.Focus(FocusState.Keyboard))UpdateOnexChoiceFocus();
  }
  private bool HandleOnexChoiceNavigation(VirtualKey key,bool repeated)
  {
   var choice=_onexOpenChoice;if(!OnexOwnsAppearanceChoice(choice)||!choice.IsDropDownOpen||!choice.IsEnabled)return false;
   int delta=0;
   switch(key){case VirtualKey.Up:case VirtualKey.Left:case VirtualKey.GamepadDPadUp:case VirtualKey.GamepadDPadLeft:case VirtualKey.GamepadLeftThumbstickUp:case VirtualKey.GamepadLeftThumbstickLeft:delta=-1;break;
    case VirtualKey.Down:case VirtualKey.Right:case VirtualKey.GamepadDPadDown:case VirtualKey.GamepadDPadRight:case VirtualKey.GamepadLeftThumbstickDown:case VirtualKey.GamepadLeftThumbstickRight:delta=1;break;}
   bool confirm=key==VirtualKey.Space||key==VirtualKey.Enter||key==VirtualKey.GamepadA,cancel=key==VirtualKey.Escape||key==VirtualKey.GamepadB;
   if(delta==0&&!confirm&&!cancel&&key!=VirtualKey.Home&&key!=VirtualKey.End&&key!=VirtualKey.PageUp&&key!=VirtualKey.PageDown)return false;
   _onexChoiceRelease=key;
   if(confirm||cancel)
   {
    if(repeated)return true;
    object selection=_onexChoiceCursor>=0&&_onexChoiceCursor<choice.Items.Count?choice.Items[_onexChoiceCursor]:null;
    object original=_onexChoiceOriginal;choice.SelectedItem=original;choice.IsDropDownOpen=false;choice.Focus(FocusState.Keyboard);
    if(cancel){_onexChoiceCancels++;return true;}
    _onexChoiceCommits++;
    // Commit after native popup teardown, so theme/font restyling cannot replace
    // a live item template inside its own input/selection callback.
    if(selection!=null)_=Dispatcher.RunAsync(CoreDispatcherPriority.Low,()=>{if(choice.IsLoaded&&!choice.IsDropDownOpen&&ReferenceEquals(choice.SelectedItem,original))choice.SelectedItem=selection;});
    return true;
   }
   var focused=FocusManager.GetFocusedElement();
   for(int n=0;n<choice.Items.Count;n++)if(ReferenceEquals(choice.ContainerFromIndex(n),focused)){_onexChoiceCursor=n;break;}
   int target=key==VirtualKey.Home?0:key==VirtualKey.End?choice.Items.Count-1:_onexChoiceCursor+(key==VirtualKey.PageUp?-6:key==VirtualKey.PageDown?6:delta);
   target=Math.Max(0,Math.Min(choice.Items.Count-1,target));
   while(target>=0&&target<choice.Items.Count){var item=choice.Items[target] as ComboBoxItem;if(item==null||item.IsEnabled)break;target+=delta==0?1:delta;}
   if(target>=0&&target<choice.Items.Count){_onexChoiceCursor=target;FocusOnexChoiceCursor(choice);_onexChoiceMoved++;}return true;
  }
  private bool HandleOnexChoiceAccelerator(AcceleratorKeyEventArgs args)
  {
   bool up=args.EventType==CoreAcceleratorKeyEventType.KeyUp||args.EventType==CoreAcceleratorKeyEventType.SystemKeyUp;
   if(up&&_onexChoiceRelease!=VirtualKey.None&&args.VirtualKey==_onexChoiceRelease){_onexChoiceRelease=VirtualKey.None;args.Handled=true;return true;}
   if(args.EventType!=CoreAcceleratorKeyEventType.KeyDown&&args.EventType!=CoreAcceleratorKeyEventType.SystemKeyDown)return false;
   if(!HandleOnexChoiceNavigation(args.VirtualKey,args.KeyStatus.WasKeyDown))return false;args.Handled=true;return true;
  }
  private bool OnexChoiceOwnsInput(VirtualKey key)
  {
   if(!OnexFrontend.UsesOwnBackend)return false;
   var choice=_onexOpenChoice??FocusManager.GetFocusedElement() as ComboBox;
   if(choice==null||!choice.IsLoaded||!choice.IsEnabled||!choice.IsDropDownOpen)return false;
   switch(key)
   {
    case VirtualKey.Up:case VirtualKey.Down:case VirtualKey.Left:case VirtualKey.Right:
    case VirtualKey.GamepadDPadUp:case VirtualKey.GamepadDPadDown:case VirtualKey.GamepadDPadLeft:case VirtualKey.GamepadDPadRight:
    case VirtualKey.GamepadLeftThumbstickUp:case VirtualKey.GamepadLeftThumbstickDown:case VirtualKey.GamepadLeftThumbstickLeft:case VirtualKey.GamepadLeftThumbstickRight:
    case VirtualKey.Home:case VirtualKey.End:case VirtualKey.PageUp:case VirtualKey.PageDown:
    case VirtualKey.GamepadA:case VirtualKey.GamepadB:case VirtualKey.Space:case VirtualKey.Enter:case VirtualKey.Escape:return true;
    default:return false;
   }
  }
 }
}
