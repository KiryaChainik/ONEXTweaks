// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private ThemeColors _onexSettingsPreview;
  private readonly HashSet<ToggleButton> _onexSettingsStyledHeaders=new HashSet<ToggleButton>();
  private bool _onexSettingsDesignUpdating;
  private ControlTemplate _onexSettingsButtonTemplate,_onexSettingsToggleTemplate,_onexSettingsCheckTemplate;
  private ControlTemplate OwnOnexSettingsButtonTemplate(bool toggle)
  {
   var current=toggle?_onexSettingsToggleTemplate:_onexSettingsButtonTemplate;if(current!=null)return current;
   string xml=@"<ControlTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" TargetType=""TARGET""><Grid Background=""Transparent""><VisualStateManager.VisualStateGroups><VisualStateGroup x:Name=""CommonStates""><VisualState x:Name=""Normal""/><VisualState x:Name=""PointerOver""><VisualState.Setters><Setter Target=""Frame.Opacity"" Value=""0.9""/></VisualState.Setters></VisualState><VisualState x:Name=""Pressed""><VisualState.Setters><Setter Target=""Frame.Opacity"" Value=""0.75""/></VisualState.Setters></VisualState><VisualState x:Name=""Disabled""><VisualState.Setters><Setter Target=""Frame.Opacity"" Value=""0.38""/></VisualState.Setters></VisualState></VisualStateGroup></VisualStateManager.VisualStateGroups><Border x:Name=""Frame"" Background=""{TemplateBinding Background}"" BorderBrush=""{TemplateBinding BorderBrush}"" BorderThickness=""{TemplateBinding BorderThickness}"" CornerRadius=""{TemplateBinding CornerRadius}"" Padding=""{TemplateBinding Padding}""><ContentPresenter Content=""{TemplateBinding Content}"" ContentTemplate=""{TemplateBinding ContentTemplate}"" Foreground=""{TemplateBinding Foreground}"" FontSize=""{TemplateBinding FontSize}"" FontFamily=""{TemplateBinding FontFamily}"" HorizontalAlignment=""{TemplateBinding HorizontalContentAlignment}"" VerticalAlignment=""{TemplateBinding VerticalContentAlignment}""/></Border></Grid></ControlTemplate>";
   current=(ControlTemplate)Windows.UI.Xaml.Markup.XamlReader.Load(xml.Replace("TARGET",toggle?"ToggleButton":"Button"));if(toggle)_onexSettingsToggleTemplate=current;else _onexSettingsButtonTemplate=current;return current;
  }
  private ControlTemplate OwnOnexSettingsCheckTemplate()
  {
   if(_onexSettingsCheckTemplate!=null)return _onexSettingsCheckTemplate;
   string xml=@"<ControlTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" TargetType=""CheckBox""><Grid x:Name=""Frame"" Background=""Transparent"" MinHeight=""36"" Padding=""{TemplateBinding Padding}""><VisualStateManager.VisualStateGroups><VisualStateGroup x:Name=""CommonStates""><VisualState x:Name=""Normal""/><VisualState x:Name=""PointerOver""/><VisualState x:Name=""Pressed""/><VisualState x:Name=""Disabled""><VisualState.Setters><Setter Target=""Frame.Opacity"" Value=""0.42""/></VisualState.Setters></VisualState></VisualStateGroup><VisualStateGroup x:Name=""CheckStates""><VisualState x:Name=""Unchecked""/><VisualState x:Name=""Checked""><VisualState.Setters><Setter Target=""Check.Visibility"" Value=""Visible""/></VisualState.Setters></VisualState><VisualState x:Name=""Indeterminate""><VisualState.Setters><Setter Target=""Check.Visibility"" Value=""Visible""/></VisualState.Setters></VisualState></VisualStateGroup></VisualStateManager.VisualStateGroups><Grid.ColumnDefinitions><ColumnDefinition Width=""24""/><ColumnDefinition Width=""*""/></Grid.ColumnDefinitions><Border Width=""22"" Height=""22"" CornerRadius=""11"" BorderThickness=""1"" BorderBrush=""{TemplateBinding BorderBrush}"" Background=""{TemplateBinding Background}"" VerticalAlignment=""Center""><FontIcon x:Name=""Check"" Visibility=""Collapsed"" Glyph=""&#xE73E;"" FontFamily=""Segoe MDL2 Assets"" FontSize=""12"" Foreground=""{TemplateBinding Foreground}""/></Border><ContentPresenter Grid.Column=""1"" Margin=""10,0,0,0"" Content=""{TemplateBinding Content}"" ContentTemplate=""{TemplateBinding ContentTemplate}"" Foreground=""{TemplateBinding Foreground}"" FontFamily=""{TemplateBinding FontFamily}"" FontSize=""{TemplateBinding FontSize}"" HorizontalAlignment=""{TemplateBinding HorizontalContentAlignment}"" VerticalAlignment=""Center""/></Grid></ControlTemplate>";
   return _onexSettingsCheckTemplate=(ControlTemplate)Windows.UI.Xaml.Markup.XamlReader.Load(xml);
  }
  private void UpdateOnexTabsHeader()
  {
   if(_onexTabsHeader==null)return;
   if(_onexTabsHeaderChevron==null)
   {
    var row=new Grid{ColumnSpacing=8};row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
    row.Children.Add(new FontIcon{Glyph="\uE700",FontSize=16,VerticalAlignment=VerticalAlignment.Center});
    var titles=new StackPanel();titles.Children.Add(new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("Tabs"),FontSize=18,FontWeight=Windows.UI.Text.FontWeights.SemiBold});titles.Children.Add(new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("Show, hide and reorder your tabs"),FontSize=12,TextWrapping=TextWrapping.Wrap});Grid.SetColumn(titles,1);row.Children.Add(titles);
    _onexTabsHeaderChevron=new FontIcon{FontSize=12,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(_onexTabsHeaderChevron,2);row.Children.Add(_onexTabsHeaderChevron);_onexTabsHeader.Content=row;
   }
   _onexTabsHeaderChevron.Glyph=_onexTabsHeader.IsChecked==true?"\uE70E":"\uE70D";
   Windows.UI.Xaml.Automation.AutomationProperties.SetName(_onexTabsHeader,_onexTabsHeader.IsChecked==true?global::XboxGamingBar.OnexStrings.Get("Hide Tabs"):global::XboxGamingBar.OnexStrings.Get("Show Tabs"));
   ApplyOnexSettingsDesign();
  }

  private void EnsureOnexSettingsSectionOrder()
  {
   if(!OnexFrontend.UsesOwnBackend||!(SystemScrollViewer?.Content is StackPanel body))return;
   var tail=new FrameworkElement[]{OnexNavigationGuideCard,_onexDriversCard,OnexDebugCard,OnexAboutCard};
   if(tail.Any(section=>section==null||!body.Children.Contains(section)))return;
   var expected=body.Children.Where(section=>!tail.Contains(section)).Concat(tail).ToArray();
   if(body.Children.SequenceEqual(expected))return;
   // Move the existing controls once; retain expansion, handlers and bindings.
   // Never reorder from a layout/scroll observer.
   foreach(var section in tail)body.Children.Remove(section);
   foreach(var section in tail)body.Children.Add(section);
   if(_onexNavigationCaches.TryGetValue(body,out var navigation))navigation.Dirty=true;
  }
  private void ApplyOnexSettingsDesign()
  {
   if(_onexSettingsDesignUpdating||SystemScrollViewer?.Visibility!=Visibility.Visible||!(SystemScrollViewer.Content is StackPanel body))return;_onexSettingsDesignUpdating=true;
   try
   {
    EnsureOnexSettingsSectionOrder();EnsureOnexSettingsHeaders();
    var theme=_onexSettingsPreview??_onexAppliedTheme??OnexPalette.CreateTheme();bool light=theme.CardBackground.R*.299+theme.CardBackground.G*.587+theme.CardBackground.B*.114>160;
    Color accent=OnexRoleAccent(1);
    var primary=OnexSolidBrush(theme.TextPrimary);var secondary=OnexSolidBrush(OnexSettingsCaptionColour(theme));var surface=OnexSurface(theme.CardBackground,theme);var stroke=OnexSolidBrush(theme.CardBorder);var controlSurface=OnexSurface(theme.ButtonBackground,theme);var focus=OnexSolidBrush(accent);
    NormalizeOnexProfilePageSpacing(body);
    foreach(var section in body.Children.OfType<FrameworkElement>())
    {
     if(section is Border card){card.Background=surface;card.BorderBrush=stroke;card.BorderThickness=new Thickness(1);card.CornerRadius=new CornerRadius(12);card.Padding=new Thickness(12);}
    }
    foreach(var item in OnexOwnedContent(body))
    {
     if(item is TextBlock text){text.Foreground=text.FontSize>=14||text.FontWeight.Weight>=600?primary:secondary;text.Opacity=1;}
     if(item is FontIcon icon){icon.Foreground=icon.FontSize>=16?focus:primary;}
     if(!(item is Control control)||control is ScrollViewer)continue;
     control.Foreground=primary;control.UseSystemFocusVisuals=false;ConfigureOnexFocus(control);
     if(control is ComboBox||control is CheckBox||control is ToggleSwitch)OnexAccentResources(control,accent);
     if(control is ComboBox choice){choice.MinHeight=38;choice.HorizontalAlignment=HorizontalAlignment.Stretch;}
     if(control is CheckBox check)UpdateOnexOwnedCheck(check,accent);
     if(control is Button button){if(!ReferenceEquals(button.Template,OnexInlineFocusTemplate(typeof(Button))))button.Template=OnexInlineFocusTemplate(typeof(Button));ConfigureOnexFocus(button);button.Background=controlSurface;button.BorderBrush=stroke;button.CornerRadius=new CornerRadius(8);}
     if(control is ToggleButton toggle && !(control is CheckBox))
     {
      if(IsOnexSettingsSectionHeader(toggle))
      {
       toggle.Template=OnexInlineFocusTemplate(typeof(ToggleButton));toggle.Background=OnexSolidBrush(Colors.Transparent);toggle.BorderBrush=OnexSolidBrush(Colors.Transparent);toggle.BorderThickness=new Thickness(0);toggle.CornerRadius=new CornerRadius(8);
       toggle.MinHeight=56;toggle.Padding=new Thickness(8,4,8,4);toggle.Margin=new Thickness(0);toggle.HorizontalAlignment=HorizontalAlignment.Stretch;toggle.HorizontalContentAlignment=HorizontalAlignment.Stretch;toggle.VerticalAlignment=VerticalAlignment.Stretch;
       var headerTexts=OnexOwnedContent(toggle.Content as DependencyObject).OfType<TextBlock>().ToArray();
       for(int index=0;index<headerTexts.Length;index++)
       {var headerText=headerTexts[index];headerText.FontSize=index==0?18:12;headerText.FontWeight=index==0?Windows.UI.Text.FontWeights.SemiBold:Windows.UI.Text.FontWeights.Normal;headerText.Foreground=index==0?primary:secondary;headerText.Margin=new Thickness(0);headerText.TextWrapping=TextWrapping.Wrap;}
       ConfigureOnexFocus(toggle);
      }
      else{toggle.Template=OwnOnexSettingsButtonTemplate(true);toggle.Background=controlSurface;toggle.BorderBrush=toggle.IsChecked==true?focus:stroke;toggle.BorderThickness=new Thickness(1);toggle.CornerRadius=new CornerRadius(8);}
      StoreOnexScopedResourceGroup(toggle,"checked-background",new[]{"ToggleButtonBackgroundChecked","ToggleButtonBackgroundCheckedPointerOver","ToggleButtonBackgroundCheckedPressed"},controlSurface);
      StoreOnexScopedResourceGroup(toggle,"checked-foreground",new[]{"ToggleButtonForegroundChecked","ToggleButtonForegroundCheckedPointerOver","ToggleButtonForegroundCheckedPressed"},primary);
      StoreOnexScopedResourceGroup(toggle,"checked-border",new[]{"ToggleButtonBorderBrushChecked","ToggleButtonBorderBrushCheckedPointerOver","ToggleButtonBorderBrushCheckedPressed"},focus);
      if(!IsOnexSettingsSectionHeader(toggle)&&_onexSettingsStyledHeaders.Add(toggle)){toggle.Checked+=(s,e)=>ApplyOnexSettingsDesign();toggle.Unchecked+=(s,e)=>ApplyOnexSettingsDesign();}
     }
    }
   }
   finally{_onexSettingsDesignUpdating=false;}
  }
  private static Color OnexSettingsCaptionColour(ThemeColors theme)
  {
   // Settings captions must remain readable, rather than resembling disabled
   // actions. Native disabled states still dim their own control template.
   Color start=theme.Name==OnexPalette.Name?Color.FromArgb(255,208,214,224):theme.TextSecondary;
   for(int step=0;step<=20;step++)
   {
    double amount=step/20d;byte Blend(byte a,byte b)=>(byte)Math.Round(a+(b-a)*amount);
    var candidate=Color.FromArgb(255,Blend(start.R,theme.TextPrimary.R),Blend(start.G,theme.TextPrimary.G),Blend(start.B,theme.TextPrimary.B));
    if(OnexContrast(candidate,theme.CardBackground)>=7&&OnexContrast(candidate,theme.ButtonBackground)>=7)return candidate;
   }
   var white=Color.FromArgb(255,255,255,255);var black=Color.FromArgb(255,0,0,0);
   double whiteContrast=Math.Min(OnexContrast(white,theme.CardBackground),OnexContrast(white,theme.ButtonBackground));
   double blackContrast=Math.Min(OnexContrast(black,theme.CardBackground),OnexContrast(black,theme.ButtonBackground));
   return whiteContrast>=blackContrast?white:black;
  }
  private async Task VerifyOnexSettingsDesignAsync(JsonObject report)
  {
   var values=ApplicationData.Current.LocalSettings.Values;string savedTheme=values.TryGetValue("WidgetTheme",out var stored)?stored as string:null;string tabs=ReadOnexTabValue(OnexTabsKey);var originalTab=GetVisibleNavigationItems().FirstOrDefault(item=>item.IsChecked==true);var requested=SystemScrollViewer.RequestedTheme;bool expanded=_onexTabsHeader.IsChecked==true;int checks=0;
   void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
   try
   {
    SystemNavItem.IsChecked=true;_onexTabsHeader.IsChecked=true;
    foreach(bool light in new[]{false,true})
    {
     _onexSettingsPreview=light?new ThemeColors{Name="QA light surface",CardBackground=Color.FromArgb(255,255,255,255),CardBorder=Color.FromArgb(255,207,215,224),TextPrimary=Color.FromArgb(255,24,31,40),TextSecondary=Color.FromArgb(255,79,91,107),ButtonBackground=Color.FromArgb(255,242,245,249),AccentColor=Color.FromArgb(255,0,112,122)}:OnexPalette.CreateTheme();SystemScrollViewer.RequestedTheme=light?ElementTheme.Light:ElementTheme.Dark;ApplyOnexSettingsDesign();UpdateLayout();await Task.Delay(100);
     var palette=_onexSettingsPreview;Check((_onexTabsHeader.Background as SolidColorBrush)?.Color==palette.ButtonBackground,"Expanded header has accent fill");Check((_onexTabsHeader.Resources["ToggleButtonBackgroundChecked"] as SolidColorBrush)?.Color==palette.ButtonBackground,"Native checked header fill not overridden");
     Check(ReferenceEquals(_onexTabsHeader.Template,OwnOnexSettingsButtonTemplate(true)),"Header is using native checked template");Check(ReferenceEquals(_onexTabRows["Performance"].Up.Template,OwnOnexSettingsButtonTemplate(false)),"Disabled move button is using native dark template");
     Check(_onexTabRows.Values.All(row=>ReferenceEquals(row.Visible.Template,OwnOnexSettingsCheckTemplate())),"Checkbox inherited the header toggle template");
     Check(_onexTabsHeader.Content is Grid,"Header still uses inline text chevron");Check((_onexTabsHeader.Foreground as SolidColorBrush)?.Color==palette.TextPrimary,"Header text contrast inconsistent");
     Check(!_onexTabRows["Performance"].Visible.IsEnabled&&!_onexTabRows["System"].Visible.IsEnabled,"Required tab state changed during styling");
     var focus=_onexTabRows.Values.SelectMany(row=>new[]{row.Up,row.Down}).First(button=>button.IsEnabled);Check(focus.Focus(FocusState.Keyboard),"Settings button cannot focus");Check(focus.FocusVisualMargin.Left>=1&&focus.FocusVisualPrimaryBrush!=null,"Settings focus lacks safe inset/accent");
     var sections=((StackPanel)SystemScrollViewer.Content).Children.OfType<FrameworkElement>().Where(OnexVisiblePageSection).ToArray();for(int n=0;n<sections.Length;n++)Check(sections[n].Margin.Top==(n==0?0:12),"Settings section spacing is inconsistent");
     _onexTabsCard.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(100);await SaveOnexImage(light?"onex-settings357-light.png":"onex-settings357-dark.png",_onexTabsCard,50);
    }
    Check((values.TryGetValue("WidgetTheme",out var current)?current as string:null)==savedTheme&&ReadOnexTabValue(OnexTabsKey)==tabs,"Visual check wrote owner theme/tab preferences");report["checks"]=JsonValue.CreateNumberValue(checks);report["actualOwnUwpLightDarkRendering"]=JsonValue.CreateBooleanValue(true);report["lightPaletteIsReadOnlyProjection"]=JsonValue.CreateBooleanValue(true);report["ownerPreferencesWrites"]=JsonValue.CreateNumberValue(0);report["ownerPhysicalInputTested"]=JsonValue.CreateBooleanValue(false);
   }
   finally{_onexSettingsPreview=null;SystemScrollViewer.RequestedTheme=requested;_onexTabsHeader.IsChecked=expanded;ApplyOnexSettingsDesign();if(originalTab!=null&&originalTab.Visibility==Visibility.Visible)originalTab.IsChecked=true;}
  }
 }
}
