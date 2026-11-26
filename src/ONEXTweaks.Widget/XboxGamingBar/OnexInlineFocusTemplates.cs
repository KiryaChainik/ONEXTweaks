using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Markup;
namespace XboxGamingBar
{
 public sealed class OnexSelectionVisibilityConverter : Windows.UI.Xaml.Data.IValueConverter
 {
  public object Convert(object value,Type targetType,object parameter,string language)=>value is bool selected&&selected?Visibility.Visible:Visibility.Collapsed;
  public object ConvertBack(object value,Type targetType,object parameter,string language)=>throw new NotSupportedException();
 }
 public sealed partial class GamingWidget
 {
  private const string OnexComboItemMarkup=@"<ControlTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" xmlns:local=""using:XboxGamingBar"" TargetType=""ComboBoxItem"">
<Grid x:Name=""OnexChoiceRoot"" Background=""Transparent"" MinHeight=""36"">
<Grid.Resources><local:OnexSelectionVisibilityConverter x:Key=""OnexSelectionVisible""/></Grid.Resources>
<VisualStateManager.VisualStateGroups>
<VisualStateGroup x:Name=""CommonStates"">
<VisualState x:Name=""Normal""/><VisualState x:Name=""Selected""/><VisualState x:Name=""SelectedUnfocused""/>
<VisualState x:Name=""PointerOver""><VisualState.Setters><Setter Target=""OwnSurface.Background"" Value=""{ThemeResource SystemControlHighlightListLowBrush}""/><Setter Target=""OwnChoiceContent.Foreground"" Value=""{ThemeResource SystemControlForegroundBaseHighBrush}""/><Setter Target=""OwnSelectedCheck.Foreground"" Value=""{ThemeResource SystemControlForegroundBaseHighBrush}""/></VisualState.Setters></VisualState>
<VisualState x:Name=""SelectedPointerOver""><VisualState.Setters><Setter Target=""OwnSurface.Background"" Value=""{ThemeResource SystemControlHighlightListLowBrush}""/><Setter Target=""OwnChoiceContent.Foreground"" Value=""{ThemeResource SystemControlForegroundBaseHighBrush}""/><Setter Target=""OwnSelectedCheck.Foreground"" Value=""{ThemeResource SystemControlForegroundBaseHighBrush}""/></VisualState.Setters></VisualState>
<VisualState x:Name=""Pressed""><VisualState.Setters><Setter Target=""OwnSurface.Background"" Value=""{ThemeResource SystemControlHighlightListMediumBrush}""/><Setter Target=""OwnChoiceContent.Foreground"" Value=""{ThemeResource SystemControlForegroundBaseHighBrush}""/><Setter Target=""OwnSelectedCheck.Foreground"" Value=""{ThemeResource SystemControlForegroundBaseHighBrush}""/></VisualState.Setters></VisualState>
<VisualState x:Name=""SelectedPressed""><VisualState.Setters><Setter Target=""OwnSurface.Background"" Value=""{ThemeResource SystemControlHighlightListMediumBrush}""/><Setter Target=""OwnChoiceContent.Foreground"" Value=""{ThemeResource SystemControlForegroundBaseHighBrush}""/><Setter Target=""OwnSelectedCheck.Foreground"" Value=""{ThemeResource SystemControlForegroundBaseHighBrush}""/></VisualState.Setters></VisualState>
<VisualState x:Name=""Disabled""><VisualState.Setters><Setter Target=""OwnSurface.Opacity"" Value=""0.4""/></VisualState.Setters></VisualState>
</VisualStateGroup>
</VisualStateManager.VisualStateGroups>
<Border x:Name=""OwnSurface"" Background=""{TemplateBinding Background}"" BorderBrush=""{TemplateBinding BorderBrush}"" BorderThickness=""{TemplateBinding BorderThickness}"" CornerRadius=""{TemplateBinding CornerRadius}"" Padding=""{TemplateBinding Padding}""><Grid>
<Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""20""/></Grid.ColumnDefinitions>
<ContentPresenter x:Name=""OwnChoiceContent"" Margin=""4,0,8,0"" Content=""{TemplateBinding Content}"" ContentTemplate=""{TemplateBinding ContentTemplate}"" Foreground=""{TemplateBinding Foreground}"" FontFamily=""{TemplateBinding FontFamily}"" FontSize=""{TemplateBinding FontSize}"" HorizontalAlignment=""{TemplateBinding HorizontalContentAlignment}"" VerticalAlignment=""Center""/>
<FontIcon x:Name=""OwnSelectedCheck"" Grid.Column=""1"" Glyph=""&#xE73E;"" FontFamily=""Segoe MDL2 Assets"" FontSize=""12"" Foreground=""{TemplateBinding Foreground}"" Visibility=""{Binding IsSelected,RelativeSource={RelativeSource TemplatedParent},Converter={StaticResource OnexSelectionVisible}}""/>
</Grid></Border>
<Border x:Name=""OwnSelected"" Width=""3"" Height=""18"" CornerRadius=""1.5"" Background=""{TemplateBinding FocusVisualPrimaryBrush}"" HorizontalAlignment=""Left"" Margin=""4,0,0,0"" Visibility=""{Binding IsSelected,RelativeSource={RelativeSource TemplatedParent},Converter={StaticResource OnexSelectionVisible}}""/>
<!-- Keep the stroke inside the popup viewport at the first and last rows. -->
<Border x:Name=""OwnKeyboardCursor"" IsHitTestVisible=""False"" Margin=""1,3,1,3"" CornerRadius=""6"" BorderBrush=""{TemplateBinding FocusVisualPrimaryBrush}"" BorderThickness=""2"" Visibility=""Collapsed""/>
</Grid></ControlTemplate>";
  private const string OnexInlineFocusMarkup="<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"$TYPE\"><Grid Background=\"Transparent\" MinHeight=\"36\">\n<VisualStateManager.VisualStateGroups>\n<VisualStateGroup x:Name=\"CommonStates\"><VisualState x:Name=\"Normal\"/><VisualState x:Name=\"PointerOver\"><VisualState.Setters><Setter Target=\"OwnSurface.Background\" Value=\"{ThemeResource SystemControlHighlightListLowBrush}\"/></VisualState.Setters></VisualState><VisualState x:Name=\"Pressed\"><VisualState.Setters><Setter Target=\"OwnSurface.Background\" Value=\"{ThemeResource SystemControlHighlightListMediumBrush}\"/></VisualState.Setters></VisualState><VisualState x:Name=\"Disabled\"><VisualState.Setters><Setter Target=\"OwnSurface.Opacity\" Value=\"0.4\"/></VisualState.Setters></VisualState></VisualStateGroup>\n<VisualStateGroup x:Name=\"FocusStates\"><VisualState x:Name=\"Unfocused\"/><VisualState x:Name=\"PointerFocused\"/><VisualState x:Name=\"Focused\"><VisualState.Setters><Setter Target=\"OwnFocus.Visibility\" Value=\"Visible\"/></VisualState.Setters></VisualState></VisualStateGroup>\n<VisualStateGroup x:Name=\"SelectionStates\"><VisualState x:Name=\"Unselected\"/><VisualState x:Name=\"Selected\"><VisualState.Setters><Setter Target=\"OwnSelected.Visibility\" Value=\"Visible\"/></VisualState.Setters></VisualState></VisualStateGroup>\n</VisualStateManager.VisualStateGroups>\n<Border x:Name=\"OwnSurface\" Background=\"{TemplateBinding Background}\" BorderBrush=\"{TemplateBinding BorderBrush}\" BorderThickness=\"{TemplateBinding BorderThickness}\" CornerRadius=\"8\" Padding=\"{TemplateBinding Padding}\">$CONTENT</Border>\n<Border x:Name=\"OwnSelected\" Width=\"3\" Height=\"18\" CornerRadius=\"1.5\" Background=\"{TemplateBinding FocusVisualPrimaryBrush}\" HorizontalAlignment=\"Left\" Margin=\"4,0,0,0\" Visibility=\"Collapsed\"/>\n<Border x:Name=\"OwnFocus\" IsHitTestVisible=\"False\" CornerRadius=\"8\" BorderBrush=\"{TemplateBinding FocusVisualPrimaryBrush}\" BorderThickness=\"{TemplateBinding FocusVisualPrimaryThickness}\" Visibility=\"Collapsed\"/>\n</Grid></ControlTemplate>";
  private readonly Dictionary<string,Style> _onexComboContainerStyles=new Dictionary<string,Style>();
  private readonly Dictionary<Type,ControlTemplate> _onexInlineFocusTemplates=new Dictionary<Type,ControlTemplate>();
  private ControlTemplate OnexInlineFocusTemplate(Type type)
  {
   if(!_onexInlineFocusTemplates.TryGetValue(type,out var template))
   {
    // Selection is an actual SelectorItem property. Keeping its marker in an
    // independent VSM group left old markers visible when native ComboBoxItem
    // returned to Normal/PointerOver in its CommonStates group.
    if(type==typeof(ComboBoxItem)){template=(ControlTemplate)XamlReader.Load(OnexComboItemMarkup);_onexInlineFocusTemplates[type]=template;return template;}
    bool menu=type==typeof(MenuFlyoutItem)||type==typeof(MenuFlyoutSubItem);
    string body=menu?"<Grid ColumnSpacing=\"10\"><Grid.ColumnDefinitions><ColumnDefinition Width=\"24\"/><ColumnDefinition Width=\"*\"/><ColumnDefinition Width=\"16\"/></Grid.ColumnDefinitions><ContentPresenter Content=\"{TemplateBinding Icon}\" VerticalAlignment=\"Center\"/><TextBlock Grid.Column=\"1\" Text=\"{TemplateBinding Text}\" Foreground=\"{TemplateBinding Foreground}\" FontFamily=\"{TemplateBinding FontFamily}\" FontSize=\"{TemplateBinding FontSize}\" TextWrapping=\"Wrap\" MaxLines=\"2\" VerticalAlignment=\"Center\"/>$ARROW</Grid>":"<ContentPresenter Content=\"{TemplateBinding Content}\" ContentTemplate=\"{TemplateBinding ContentTemplate}\" Foreground=\"{TemplateBinding Foreground}\" FontFamily=\"{TemplateBinding FontFamily}\" FontSize=\"{TemplateBinding FontSize}\" HorizontalAlignment=\"{TemplateBinding HorizontalContentAlignment}\" VerticalAlignment=\"Center\"/>";
    body=body.Replace("$ARROW",type==typeof(MenuFlyoutSubItem)?"<FontIcon Grid.Column=\"2\" Glyph=\"&#xE76C;\" FontFamily=\"Segoe MDL2 Assets\" FontSize=\"10\"/>":"");
    string markup=OnexInlineFocusMarkup.Replace("$TYPE",type.Name).Replace("$CONTENT",body);if(type==typeof(Button))markup=markup.Replace("MinHeight=\"36\"","MinHeight=\"0\"");markup=markup.Replace("CornerRadius=\"8\"","CornerRadius=\"{TemplateBinding CornerRadius}\"");if(type==typeof(ToggleButton))
    {
     string state(string name,string brush)=>"<VisualState x:Name=\""+name+"\"><VisualState.Setters><Setter Target=\"OwnSurface.Background\" Value=\"{ThemeResource "+brush+"}\"/></VisualState.Setters></VisualState>";
     markup=markup.Replace("<VisualState x:Name=\"Normal\"/>","<VisualState x:Name=\"Normal\"/><VisualState x:Name=\"Checked\"/>"+state("CheckedPointerOver","SystemControlHighlightListLowBrush")+state("CheckedPressed","SystemControlHighlightListMediumBrush"));
    }
    template=(ControlTemplate)XamlReader.Load(markup);_onexInlineFocusTemplates[type]=template;
   }
   return template;
  }
  private bool OnexHasInlineFocus(Control control)=>control!=null&&_onexInlineFocusTemplates.Values.Any(template=>ReferenceEquals(control.Template,template));
  private void ApplyOnexInlineFocus(Control control)
  {
   if(!(control is ComboBoxItem)&&!(control is MenuFlyoutItem)&&!(control is MenuFlyoutSubItem))return;
   var template=OnexInlineFocusTemplate(control.GetType()==typeof(MenuFlyoutSubItem)?typeof(MenuFlyoutSubItem):control is MenuFlyoutItem?typeof(MenuFlyoutItem):typeof(ComboBoxItem));
   if(!ReferenceEquals(control.Template,template))control.Template=template;
   control.CornerRadius=new CornerRadius(8);control.Padding=new Thickness(12,8,12,8);control.MinHeight=38;control.UseSystemFocusVisuals=false;
   if(control is ComboBoxItem item)ConfigureOnexChoiceColours(item);
  }
  private Style OnexOwnedComboItemStyle()
  {
   var theme=_onexAppliedTheme??OnexPalette.CreateTheme();string key=theme.TextPrimary.ToString()+theme.ButtonBackground.ToString()+OnexFocusColour.ToString()+FontFamily.Source;if(_onexComboContainerStyles.TryGetValue(key,out var cached))return cached;var style=new Style(typeof(ComboBoxItem));
   style.Setters.Add(new Setter(Control.FontFamilyProperty,FontFamily));
   style.Setters.Add(new Setter(Control.TemplateProperty,OnexInlineFocusTemplate(typeof(ComboBoxItem))));
   style.Setters.Add(new Setter(Control.UseSystemFocusVisualsProperty,false));style.Setters.Add(new Setter(Control.CornerRadiusProperty,new CornerRadius(8)));style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(12,8,12,8)));
   style.Setters.Add(new Setter(Control.ForegroundProperty,OnexSolidBrush(theme.TextPrimary)));style.Setters.Add(new Setter(Control.BackgroundProperty,OnexSolidBrush(theme.ButtonBackground)));style.Setters.Add(new Setter(Control.FocusVisualPrimaryBrushProperty,OnexSolidBrush(OnexFocusColour)));style.Setters.Add(new Setter(Control.FocusVisualPrimaryThicknessProperty,new Thickness(2)));style.Setters.Add(new Setter(Control.FocusVisualSecondaryThicknessProperty,new Thickness(0)));_onexComboContainerStyles[key]=style;return style;
  }
  private void ConfigureOnexOwnedMenuItem(MenuFlyoutItemBase item)
  {
   ConfigureOnexFocus(item);if(item is MenuFlyoutSubItem parent)foreach(var child in parent.Items)ConfigureOnexOwnedMenuItem(child);
  }
  private Style OnexDialogFocusButtonStyle(bool primary)
  {
   var style=new Style(typeof(Button)){BasedOn=OnexSettingsButtonStyle(primary)};
   style.Setters.Add(new Setter(Control.TemplateProperty,OnexInlineFocusTemplate(typeof(Button))));
   style.Setters.Add(new Setter(Control.UseSystemFocusVisualsProperty,false));return style;
  }
 }
}
