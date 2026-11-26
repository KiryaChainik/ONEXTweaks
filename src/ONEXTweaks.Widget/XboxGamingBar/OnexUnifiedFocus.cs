using System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Input;
using System.Linq;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private bool _onexUnifiedFocusHooked;
  private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control,object> _onexFocusControls=new System.Runtime.CompilerServices.ConditionalWeakTable<Control,object>();
  private sealed class OnexFocusStamp
  {internal ThemeColors Theme;internal int FontEpoch;internal object Scope;internal Style Style;internal ControlTemplate Template;internal Color? Foreground,Background;internal bool Ready;}
  private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control,OnexFocusStamp> _onexFocusStamps=new System.Runtime.CompilerServices.ConditionalWeakTable<Control,OnexFocusStamp>();
  private Color OnexFocusColour=>OnexCurrentPalette.Focus;
  private void ConfigureOnexFocus(Control control,bool restyleChrome=true)
  {
   if(control==null||OnexApprovedTheme.OwnsTarget(control))return;
   var stamp=_onexFocusStamps.GetValue(control,_=>new OnexFocusStamp());_onexControlLayers.TryGetValue(control,out var scope);
   Color? foreground=(control.Foreground as SolidColorBrush)?.Color,background=(control.Background as SolidColorBrush)?.Color;
   if(!_onexReplayPreviousThemeWork&&stamp.Ready&&ReferenceEquals(stamp.Theme,_onexAppliedTheme)&&stamp.FontEpoch==_onexUiFontEpoch&&ReferenceEquals(stamp.Scope,scope)&&ReferenceEquals(stamp.Style,control.Style)&&ReferenceEquals(stamp.Template,control.Template)&&stamp.Foreground==foreground&&stamp.Background==background)return;
   long focusStarted=_onexNavigationPerfCapture?System.Diagnostics.Stopwatch.GetTimestamp():0;
   try{
   if(restyleChrome){ApplyOnexOwnedControlChrome(control);ApplyOnexInlineFocus(control);}
   ApplyOnexUiFontNode(control);
   WireOnexFocusEvents(control);
   if((control is Button||control is Windows.UI.Xaml.Controls.Primitives.ToggleButton||control is ComboBox||control is TextBox||control is MenuFlyoutItemBase)&&control.CornerRadius.Equals(new CornerRadius(0)))control.CornerRadius=new CornerRadius(8);
   control.UseSystemFocusVisuals=false;
   if(control is Button button&&qsTileDefinitions.Any(tile=>ReferenceEquals(tile.TileButton,button)))button.CornerRadius=new CornerRadius(12);
   if(control is CheckBox check){check.Padding=new Thickness(6);check.CornerRadius=new CornerRadius(8);}
   // Only native text-field templates consume these keys. Adding the group
   // to every button/toggle/slider creates unused XAML resource references,
   // increasing native/managed reference tracking during Gen2 collections.
   if(control is ComboBox)
    SetOnexThemeResourceGroup(control,"focus-chrome-border",new[]{"ComboBoxBorderBrushFocused","ComboBoxBackgroundBorderBrushFocused"},_onexAppliedTheme?.CardBorder??OnexPalette.Border);
   else if(control is TextBox||control is PasswordBox||control is RichEditBox||control is AutoSuggestBox||control is Microsoft.UI.Xaml.Controls.NumberBox)
    SetOnexThemeResourceGroup(control,"focus-chrome-border",new[]{"TextControlBorderBrushFocused"},_onexAppliedTheme?.CardBorder??OnexPalette.Border);
   if(control is ComboBox)
   {
    SetOnexThemeResourceGroup(control,"focus-chrome-text",new[]{"ComboBoxForegroundFocused","ComboBoxDropDownGlyphForegroundFocused"},_onexAppliedTheme?.TextPrimary??Colors.White);
    var headerMargin=new Thickness(6,6,6,6);
    if(_onexReplayPreviousThemeWork||!control.Resources.TryGetValue("ComboBoxTopHeaderMargin",out var currentMargin)||!(currentMargin is Thickness priorMargin)||!priorMargin.Equals(headerMargin))control.Resources["ComboBoxTopHeaderMargin"]=headerMargin;
   }
   SetOnexThemeBrush(control,Control.FocusVisualPrimaryBrushProperty,OnexFocusColour);
   SetOnexThemeBrush(control,Control.FocusVisualSecondaryBrushProperty,Colors.Transparent);
   control.FocusVisualPrimaryThickness=OnexHasInlineFocus(control)&&_onexControlLayers.TryGetValue(control,out var ownScope)?new Thickness(0):new Thickness(2);
   control.FocusVisualSecondaryThickness=new Thickness(0);
   control.FocusVisualMargin=new Thickness(2);
   ConfigureOnexInteractionSurfaces(control);
   if(restyleChrome){stamp.Theme=_onexAppliedTheme;stamp.FontEpoch=_onexUiFontEpoch;stamp.Scope=scope;stamp.Style=control.Style;stamp.Template=control.Template;stamp.Foreground=(control.Foreground as SolidColorBrush)?.Color;stamp.Background=(control.Background as SolidColorBrush)?.Color;stamp.Ready=true;}
   }finally{RecordOnexNavigationWork("focus-node",control,focusStarted);}
  }
  private void EnsureOnexUnifiedFocus()
  {
   if(_onexUnifiedFocusHooked)return;_onexUnifiedFocusHooked=true;
   EnsureOnexFocusLayer(OnexStartupRoot,true);
   GotFocus+=(s,e)=>{var focused=e.OriginalSource as Control??FocusManager.GetFocusedElement() as Control;if(focused!=null)ConfigureOnexFocus(focused,!_onexFocusControls.TryGetValue(focused,out var known));};
  }
  private void ApplyOnexPageFocus()
  {
   var root=OnexNavigationRoot();if(_onexMainFocusLayer!=null)RegisterOnexFocusLayerContent(root,_onexMainFocusLayer);
   if(root!=null&&_onexFocusLayoutRoots.Add(root))root.SizeChanged+=(s,e)=>RefreshOnexFocusLayer(_onexMainFocusLayer);
   if(OnexOverlayScrollViewer?.Visibility==Visibility.Visible)PaintOnexOverlayMetricRows();
   RefreshOnexOwnedFocus();
  }
 }
}
