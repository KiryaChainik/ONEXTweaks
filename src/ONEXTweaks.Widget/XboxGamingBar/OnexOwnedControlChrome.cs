using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Linq;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Markup;
using Windows.UI.Xaml.Media;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private readonly Dictionary<string,Style> _onexOwnedStyles=new Dictionary<string,Style>();
  private readonly Dictionary<string,ControlTemplate> _onexOwnedCheckTemplates=new Dictionary<string,ControlTemplate>();
  private readonly ConditionalWeakTable<CheckBox,object> _onexOwnedChecks=new ConditionalWeakTable<CheckBox,object>();
  private sealed class OnexCheckAccent{internal int Epoch;internal Color Colour;}
  private readonly ConditionalWeakTable<CheckBox,OnexCheckAccent> _onexCheckAccents=new ConditionalWeakTable<CheckBox,OnexCheckAccent>();
  private bool _onexApplyingOwnedChrome;
  private const string OnexOwnedSliderMarkup="<Style xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"Slider\">\n<Setter Property=\"MinHeight\" Value=\"36\"/><Setter Property=\"CornerRadius\" Value=\"8\"/><Setter Property=\"IsFocusEngagementEnabled\" Value=\"True\"/><Setter Property=\"IsThumbToolTipEnabled\" Value=\"False\"/><Setter Property=\"ManipulationMode\" Value=\"None\"/><Setter Property=\"UseSystemFocusVisuals\" Value=\"False\"/>\n<Setter Property=\"Template\"><Setter.Value><ControlTemplate TargetType=\"Slider\">\n<Grid x:Name=\"OwnedSliderRoot\" Background=\"Transparent\"><Grid.Resources><Style x:Key=\"OwnedRoundThumb\" TargetType=\"Thumb\"><Setter Property=\"Template\"><Setter.Value><ControlTemplate TargetType=\"Thumb\"><Grid Width=\"24\" Height=\"24\"><Ellipse Fill=\"{TemplateBinding Foreground}\" Opacity=\"0.2\"/><Ellipse Width=\"18\" Height=\"18\" Fill=\"{TemplateBinding Foreground}\"/><Ellipse Width=\"10\" Height=\"10\" Fill=\"White\"/></Grid></ControlTemplate></Setter.Value></Setter></Style></Grid.Resources><Grid.RowDefinitions><RowDefinition Height=\"Auto\"/><RowDefinition Height=\"*\"/></Grid.RowDefinitions>\n<VisualStateManager.VisualStateGroups>\n<VisualStateGroup x:Name=\"CommonStates\"><VisualState x:Name=\"Normal\"/><VisualState x:Name=\"PointerOver\"/><VisualState x:Name=\"Pressed\"/><VisualState x:Name=\"Disabled\"><VisualState.Setters><Setter Target=\"OwnedSliderRoot.Opacity\" Value=\"0.4\"/></VisualState.Setters></VisualState></VisualStateGroup>\n<VisualStateGroup x:Name=\"OrientationStates\"><VisualState x:Name=\"Horizontal\"/><VisualState x:Name=\"Vertical\"><VisualState.Setters><Setter Target=\"HorizontalTemplate.Visibility\" Value=\"Collapsed\"/><Setter Target=\"VerticalTemplate.Visibility\" Value=\"Visible\"/></VisualState.Setters></VisualState></VisualStateGroup>\n</VisualStateManager.VisualStateGroups>\n<ContentPresenter x:Name=\"HeaderContentPresenter\" Content=\"{TemplateBinding Header}\" ContentTemplate=\"{TemplateBinding HeaderTemplate}\" Foreground=\"$PRIMARY\" FontSize=\"13\" Margin=\"6\" HorizontalAlignment=\"Left\" Visibility=\"Collapsed\"/>\n<Grid Grid.Row=\"1\" x:Name=\"HorizontalTemplate\" MinHeight=\"36\" Margin=\"12,0\"><Grid.ColumnDefinitions><ColumnDefinition Width=\"Auto\"/><ColumnDefinition Width=\"Auto\"/><ColumnDefinition Width=\"*\"/></Grid.ColumnDefinitions>\n<Rectangle x:Name=\"HorizontalTrackRect\" Height=\"8\" RadiusX=\"4\" RadiusY=\"4\" Fill=\"$TRACK\" VerticalAlignment=\"Center\" Grid.ColumnSpan=\"3\"/>\n<Rectangle x:Name=\"HorizontalDecreaseRect\" Height=\"8\" RadiusX=\"4\" RadiusY=\"4\" Fill=\"{TemplateBinding Foreground}\" VerticalAlignment=\"Center\"/>\n<Thumb x:Name=\"HorizontalThumb\" Grid.Column=\"1\" Width=\"24\" Height=\"24\" Margin=\"-12,0\" Foreground=\"{TemplateBinding Foreground}\" Style=\"{StaticResource OwnedRoundThumb}\" AutomationProperties.AccessibilityView=\"Raw\"/>\n</Grid>\n<Grid Grid.Row=\"1\" x:Name=\"VerticalTemplate\" Visibility=\"Collapsed\" MinWidth=\"36\" Margin=\"0,12\"><Grid.RowDefinitions><RowDefinition Height=\"*\"/><RowDefinition Height=\"Auto\"/><RowDefinition Height=\"Auto\"/></Grid.RowDefinitions>\n<Rectangle x:Name=\"VerticalTrackRect\" Width=\"8\" RadiusX=\"4\" RadiusY=\"4\" Fill=\"$TRACK\" HorizontalAlignment=\"Center\" Grid.RowSpan=\"3\"/>\n<Rectangle x:Name=\"VerticalDecreaseRect\" Grid.Row=\"2\" Width=\"8\" RadiusX=\"4\" RadiusY=\"4\" Fill=\"{TemplateBinding Foreground}\" HorizontalAlignment=\"Center\"/>\n<Thumb x:Name=\"VerticalThumb\" Grid.Row=\"1\" Width=\"24\" Height=\"24\" Margin=\"0,-12\" Foreground=\"{TemplateBinding Foreground}\" Style=\"{StaticResource OwnedRoundThumb}\" AutomationProperties.AccessibilityView=\"Raw\"/>\n</Grid>\n</Grid></ControlTemplate></Setter.Value></Setter></Style>";
  private const string OnexOwnedToggleMarkup="<Style xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"ToggleSwitch\">\n<Setter Property=\"MinWidth\" Value=\"0\"/><Setter Property=\"MinHeight\" Value=\"44\"/><Setter Property=\"Padding\" Value=\"8\"/><Setter Property=\"CornerRadius\" Value=\"8\"/><Setter Property=\"UseSystemFocusVisuals\" Value=\"False\"/>\n<Setter Property=\"Template\"><Setter.Value><ControlTemplate TargetType=\"ToggleSwitch\"><Grid x:Name=\"OwnedToggleRoot\" Background=\"Transparent\" Padding=\"{TemplateBinding Padding}\">\n<Grid.RowDefinitions><RowDefinition Height=\"Auto\"/><RowDefinition Height=\"Auto\"/></Grid.RowDefinitions>\n<VisualStateManager.VisualStateGroups>\n<VisualStateGroup x:Name=\"CommonStates\"><VisualState x:Name=\"Normal\"/><VisualState x:Name=\"PointerOver\"/><VisualState x:Name=\"Pressed\"/><VisualState x:Name=\"Disabled\"><VisualState.Setters><Setter Target=\"OwnedToggleRoot.Opacity\" Value=\"0.4\"/></VisualState.Setters></VisualState></VisualStateGroup>\n<VisualStateGroup x:Name=\"ToggleStates\"><VisualState x:Name=\"Dragging\"/><VisualState x:Name=\"Off\"/><VisualState x:Name=\"On\"><VisualState.Setters><Setter Target=\"KnobTranslateTransform.X\" Value=\"20\"/><Setter Target=\"OwnedTrackOn.Opacity\" Value=\"1\"/><Setter Target=\"OnContentPresenter.Visibility\" Value=\"Visible\"/><Setter Target=\"OffContentPresenter.Visibility\" Value=\"Collapsed\"/></VisualState.Setters></VisualState></VisualStateGroup>\n</VisualStateManager.VisualStateGroups>\n<ContentPresenter Content=\"{TemplateBinding Header}\" ContentTemplate=\"{TemplateBinding HeaderTemplate}\" Foreground=\"$PRIMARY\" FontSize=\"13\"/>\n<Grid x:Name=\"SwitchAreaGrid\" Grid.Row=\"1\"><Grid.ColumnDefinitions><ColumnDefinition Width=\"48\"/><ColumnDefinition Width=\"Auto\"/></Grid.ColumnDefinitions>\n<Canvas Width=\"48\" Height=\"28\" UseLayoutRounding=\"False\">\n<Border Width=\"48\" Height=\"28\" CornerRadius=\"14\" Background=\"$TRACK\" BorderBrush=\"$BORDER\" BorderThickness=\"1\"/>\n<Border x:Name=\"OwnedTrackOn\" Width=\"48\" Height=\"28\" CornerRadius=\"14\" Background=\"{TemplateBinding Background}\" Opacity=\"0\"/>\n<Rectangle x:Name=\"SwitchKnobBounds\" Width=\"40\" Height=\"20\" Canvas.Left=\"4\" Canvas.Top=\"4\" Fill=\"Transparent\" Opacity=\"0\" IsHitTestVisible=\"False\"/>\n<Grid x:Name=\"SwitchKnob\" Width=\"20\" Height=\"20\" Canvas.Left=\"4\" Canvas.Top=\"4\"><Grid.RenderTransform><TranslateTransform x:Name=\"KnobTranslateTransform\"/></Grid.RenderTransform><Ellipse Fill=\"White\" Width=\"20\" Height=\"20\"/></Grid>\n<Thumb x:Name=\"SwitchThumb\" Width=\"48\" Height=\"28\" AutomationProperties.AccessibilityView=\"Raw\"><Thumb.Template><ControlTemplate TargetType=\"Thumb\"><Rectangle Fill=\"Transparent\"/></ControlTemplate></Thumb.Template></Thumb>\n</Canvas>\n<Grid Grid.Column=\"1\" Margin=\"8,0,0,0\" VerticalAlignment=\"Center\"><ContentPresenter x:Name=\"OnContentPresenter\" Content=\"{TemplateBinding OnContent}\" ContentTemplate=\"{TemplateBinding OnContentTemplate}\" Foreground=\"$PRIMARY\" Visibility=\"Collapsed\"/><ContentPresenter x:Name=\"OffContentPresenter\" Content=\"{TemplateBinding OffContent}\" ContentTemplate=\"{TemplateBinding OffContentTemplate}\" Foreground=\"$PRIMARY\"/></Grid>\n</Grid></Grid></ControlTemplate></Setter.Value></Setter></Style>";
  private const string OnexOwnedCheckMarkup="<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"CheckBox\"><Grid x:Name=\"OwnedCheckRoot\" Background=\"Transparent\" Padding=\"{TemplateBinding Padding}\" MinHeight=\"34\">\n<VisualStateManager.VisualStateGroups><VisualStateGroup x:Name=\"CommonStates\"><VisualState x:Name=\"Normal\"/><VisualState x:Name=\"PointerOver\"/><VisualState x:Name=\"Pressed\"/><VisualState x:Name=\"Disabled\"><VisualState.Setters><Setter Target=\"OwnedCheckRoot.Opacity\" Value=\"0.4\"/></VisualState.Setters></VisualState></VisualStateGroup>\n<VisualStateGroup x:Name=\"CheckStates\"><VisualState x:Name=\"Unchecked\"/><VisualState x:Name=\"Checked\"><VisualState.Setters><Setter Target=\"OwnedCheckGlyph.Visibility\" Value=\"Visible\"/></VisualState.Setters></VisualState><VisualState x:Name=\"Indeterminate\"><VisualState.Setters><Setter Target=\"OwnedCheckGlyph.Visibility\" Value=\"Visible\"/></VisualState.Setters></VisualState></VisualStateGroup></VisualStateManager.VisualStateGroups>\n<Grid.ColumnDefinitions><ColumnDefinition Width=\"22\"/><ColumnDefinition Width=\"*\"/></Grid.ColumnDefinitions>\n<Border Width=\"22\" Height=\"22\" CornerRadius=\"4\" BorderThickness=\"1\" BorderBrush=\"{TemplateBinding BorderBrush}\" Background=\"{TemplateBinding Background}\" VerticalAlignment=\"Center\"><FontIcon x:Name=\"OwnedCheckGlyph\" Glyph=\"&#xE73E;\" FontFamily=\"Segoe MDL2 Assets\" FontSize=\"12\" Foreground=\"$GLYPH\" Visibility=\"Collapsed\"/></Border>\n<ContentPresenter Grid.Column=\"1\" Margin=\"10,0,0,0\" Content=\"{TemplateBinding Content}\" ContentTemplate=\"{TemplateBinding ContentTemplate}\" Foreground=\"{TemplateBinding Foreground}\" FontFamily=\"{TemplateBinding FontFamily}\" FontSize=\"{TemplateBinding FontSize}\" VerticalAlignment=\"Center\"/>\n</Grid></ControlTemplate>";
  private sealed class OnexTrackColour { internal Color Ink; }
  private static readonly ConditionalWeakTable<ThemeColors,OnexTrackColour> OnexTrackColours=
   new ConditionalWeakTable<ThemeColors,OnexTrackColour>();
  internal static Color OnexInactiveSliderTrack(ThemeColors theme)=>OnexTrackColours.GetValue(theme,palette=>
  {
   var surfaces=OnexThemeContrastSurfaces(palette);
   for(int step=0;step<=100;step++)
   {
    var ink=OnexOver(palette.TextPrimary,palette.CardBorder,step/100d);
    if(surfaces.All(surface=>OnexContrast(ink,surface)>=3))return new OnexTrackColour{Ink=ink};
   }
   return new OnexTrackColour{Ink=palette.TextPrimary};
  }).Ink;
  private Style OnexOwnedStyle(string kind)
  {
   var theme=_onexAppliedTheme??OnexPalette.CreateTheme();var track=kind=="slider"?OnexInactiveSliderTrack(theme):theme.ButtonBackground;
   string key=kind+theme.TextPrimary.ToString()+track.ToString()+theme.CardBorder.ToString();
   if(!_onexOwnedStyles.TryGetValue(key,out var style))
   {string xml=(kind=="slider"?OnexOwnedSliderMarkup:OnexOwnedToggleMarkup).Replace("$PRIMARY",theme.TextPrimary.ToString()).Replace("$TRACK",track.ToString()).Replace("$BORDER",theme.CardBorder.ToString());if(kind=="toggle-compact")
    {int start=xml.IndexOf("<Grid Grid.Column=\"1\"",StringComparison.Ordinal);int end=start>=0?xml.IndexOf("</Grid>",start,StringComparison.Ordinal):-1;if(start>=0&&end>=start)xml=xml.Remove(start,end-start+7);xml=xml.Replace("<Setter Target=\"OnContentPresenter.Visibility\" Value=\"Visible\"/>","").Replace("<Setter Target=\"OffContentPresenter.Visibility\" Value=\"Collapsed\"/>","");}
    style=(Style)XamlReader.Load(xml);_onexOwnedStyles[key]=style;}return style;
  }
  private Color OnexOwnedAccent(Control control)
  {
   if(OnexCatalogTheme&&_onexAppliedTheme!=null)return _onexAppliedTheme.EffectiveAccent();
   string key=control is CheckBox?"CheckBoxCheckBackgroundFillChecked":"ToggleSwitchFillOn";
   if(control.Resources.TryGetValue(key,out var resource)&&resource is SolidColorBrush solid)return solid.Color;
   return _onexAppliedTheme?.EffectiveAccent()??OnexPalette.Orange;
  }
  private void UpdateOnexOwnedCheck(CheckBox check,Color? requested=null)
  {
   var theme=_onexAppliedTheme??OnexPalette.CreateTheme();var scope=_onexCheckAccents.GetValue(check,_=>new OnexCheckAccent{Epoch=-1});
   if(requested.HasValue){scope.Colour=requested.Value;scope.Epoch=_onexThemeEpoch;}
   var accent=requested??(scope.Epoch==_onexThemeEpoch?scope.Colour:OnexOwnedAccent(check));
   string glyph=OnexContrast(Colors.Black,accent)>OnexContrast(Colors.White,accent)?"#FF000000":"#FFFFFFFF";
   if(!_onexOwnedCheckTemplates.TryGetValue(glyph,out var template)){template=(ControlTemplate)XamlReader.Load(OnexOwnedCheckMarkup.Replace("$GLYPH",glyph));_onexOwnedCheckTemplates[glyph]=template;}
   if(!ReferenceEquals(check.Template,template))check.Template=template;
   check.Padding=new Thickness(6);check.CornerRadius=new CornerRadius(8);
   SetOnexThemeBrush(check,Control.BackgroundProperty,check.IsChecked==true?accent:theme.ButtonBackground);
   SetOnexThemeBrush(check,Control.BorderBrushProperty,check.IsChecked==true?accent:theme.CardBorder);
   SetOnexThemeBrush(check,Control.ForegroundProperty,theme.TextPrimary);
  }
  private void ApplyOnexOwnedControlChrome(Control control)
  {
   if(control==null||_onexApplyingOwnedChrome)return;_onexApplyingOwnedChrome=true;
   try
   {
    if(ReferenceEquals(control,PerfSavedProfilesExpandButton))
    {
     control.Template=OwnOnexSettingsButtonTemplate(true);control.CornerRadius=new CornerRadius(8);
     control.Padding=new Thickness(12,10,12,10);control.HorizontalContentAlignment=HorizontalAlignment.Stretch;
    }
    else if(control is Button ownButton)
    {
     bool customTemplate=ownButton.ReadLocalValue(Control.TemplateProperty)!=DependencyProperty.UnsetValue&&!OnexHasInlineFocus(ownButton);
     bool explicitStyleTemplate=ownButton.Style?.Setters.OfType<Setter>().Any(setter=>setter.Property==Control.TemplateProperty)==true;
     if(!customTemplate&&!explicitStyleTemplate&&!OnexHasInlineFocus(ownButton))ownButton.Template=OnexInlineFocusTemplate(typeof(Button));
    }
    else if(control is ComboBox choice)
    {ConfigureOnexChoicePicker(choice);var items=OnexOwnedComboItemStyle();if(!ReferenceEquals(choice.ItemContainerStyle,items))choice.ItemContainerStyle=items;choice.Padding=new Thickness(12,8,12,8);choice.CornerRadius=new CornerRadius(8);}
    else if(control is CheckBox check)
    {
     if(!_onexOwnedChecks.TryGetValue(check,out var prior)){_onexOwnedChecks.Add(check,new object());check.Checked+=(s,e)=>UpdateOnexOwnedCheck(check);check.Unchecked+=(s,e)=>UpdateOnexOwnedCheck(check);check.Indeterminate+=(s,e)=>UpdateOnexOwnedCheck(check);}
     UpdateOnexOwnedCheck(check);
    }
    else if(control is ToggleSwitch toggle)
    {
     if(_releaseHome!=null&&ReferenceEquals(toggle,_releaseHome.FindName("LightPl2Toggle"))){toggle.CornerRadius=new CornerRadius(14);return;}
     if(_releaseHome!=null&&ReferenceEquals(toggle,_releaseHome.FindName("LightCpuBoostToggle"))){toggle.CornerRadius=new CornerRadius(14);return;}
     if(ReferenceEquals(toggle,CPUBoostToggle)){toggle.OnContent="";toggle.OffContent="";}
     bool compact=string.IsNullOrEmpty(toggle.OnContent as string)&&string.IsNullOrEmpty(toggle.OffContent as string);var style=OnexOwnedStyle(compact?"toggle-compact":"toggle");if(!ReferenceEquals(toggle.Style,style))toggle.Style=style;
     toggle.MaxWidth=double.PositiveInfinity;toggle.MaxHeight=double.PositiveInfinity;toggle.Width=double.NaN;toggle.Height=double.NaN;toggle.MinWidth=0;toggle.MinHeight=44;toggle.Padding=new Thickness(8);
     if(ReferenceEquals(toggle,CPUBoostToggle)){toggle.OnContent="";toggle.OffContent="";}
     SetOnexThemeBrush(toggle,Control.BackgroundProperty,OnexOwnedAccent(toggle));
    }
    else if(control is Slider slider)
    {
     if(ReferenceEquals(slider,RailBrightnessSlider)||ReferenceEquals(slider,RailVolumeSlider)||ReferenceEquals(slider,_releaseHome?.PowerInput)||(_releaseHome!=null&&ReferenceEquals(slider,_releaseHome.FindName("OnexPl2Slider"))))return;
     var style=OnexOwnedStyle("slider");if(!ReferenceEquals(slider.Style,style))slider.Style=style;slider.CornerRadius=new CornerRadius(8);
    }
   }
   finally{_onexApplyingOwnedChrome=false;}
  }
 }
}
