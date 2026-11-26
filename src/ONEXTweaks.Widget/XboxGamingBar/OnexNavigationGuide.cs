// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private sealed class OnexGuideEntry
  {
   internal string Id,Title,Detail;internal string[] Symbols;
   internal OnexGuideEntry(string id,string title,string detail,params string[] symbols)
   {Id=id;Title=OnexStrings.Get(title);Detail=OnexStrings.Get(detail);Symbols=symbols;}
  }
  private static readonly OnexGuideEntry[] OnexGuideEntries={
   new OnexGuideEntry("move","Move between controls","Use the D-pad or left stick. Directional focus stays in the page content; use LT / RT or touch to change tabs.","D-Pad Up","Left Stick"),
   new OnexGuideEntry("tabs","Previous / next tab","LT and RT cycle through visible tabs, skipping hidden ones. Close the current popup or menu before switching.","LT","RT"),
   new OnexGuideEntry("activate","Select or start editing","A activates the focused control. On a slider, press A to start editing and A again to apply; focus stays on that slider.","A"),
   new OnexGuideEntry("cancel","Cancel or go back","While editing a slider, B restores its starting value. In a popup, B closes it; in an open Controller or Performance section, B can collapse that section.","B"),
   new OnexGuideEntry("details","More options on Home","Focus a tile with a menu and press X for the same options as touch and hold. On the volume or microphone icon, X opens audio-device selection.","X"),
   new OnexGuideEntry("adjust","Adjust a slider","During editing, left / right changes the value. Most percentage sliders use 5% gamepad steps and 1% touch steps. TDP uses watts; Charge Limit keeps its own increments.","D-Pad Left","D-Pad Right"),
   new OnexGuideEntry("applymove","Apply and move","While editing a slider, up / down saves the value and moves focus to the control above / below. At a page boundary, focus stays in the content.","D-Pad Up","D-Pad Down"),
   new OnexGuideEntry("first","First control on this tab","Y returns focus to the first control of the current tab. It does not put the top tab buttons into directional navigation.","Y"),
   new OnexGuideEntry("touch","Touch controls","Tap to activate or choose. Drag a slider for direct adjustment. Touch and hold a Home tile with a menu to open its details; you can also tap a tab directly."),
   new OnexGuideEntry("desktop","Keyboard / Mouse mode","Desktop bindings are separate from widget navigation and can be remapped. See the Keyboard / Mouse tab for the current button and stick assignments."),
   new OnexGuideEntry("keyboard","Keyboard shortcuts","Use Tab / Shift+Tab to focus controls, Enter / Space to activate, and Esc to cancel a slider edit or close a popup. On Home tiles with options, use the Menu key or Shift+F10.")
  };
  private StackPanel _onexNavigationGuide;
  private readonly Dictionary<string,ContentControl> _onexNavigationGuideRows=new Dictionary<string,ContentControl>();
  private void EnsureOnexNavigationGuide()
  {
   if(_onexNavigationGuide!=null||ProTipsContent==null||!OnexFrontend.UsesOwnBackend)return;
   // One outer Settings ScrollViewer owns scrolling; rows have no nested selector/scroll host.
   _onexNavigationGuide=new StackPanel{Name="OnexNavigationGuide",Spacing=4};
   var template=(ControlTemplate)Windows.UI.Xaml.Markup.XamlReader.Load(@"<ControlTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"" TargetType=""ContentControl""><ContentPresenter Content=""{TemplateBinding Content}"" Foreground=""{TemplateBinding Foreground}"" HorizontalAlignment=""Stretch"" /></ControlTemplate>");
   foreach(var entry in OnexGuideEntries)
   {
    var line=new Grid{ColumnSpacing=12,Padding=new Thickness(4,8,4,8)};
    line.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(88)});line.ColumnDefinitions.Add(new ColumnDefinition());
    var icons=new StackPanel{Orientation=Orientation.Horizontal,Spacing=6,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,2,0,0)};
    foreach(string symbol in entry.Symbols)
    {
     if(!GamepadButtonIconConverter.Map.TryGetValue(symbol,out var asset))continue;
     var image=new Image{Name="OnexGuideIcon"+entry.Id+icons.Children.Count,Source=new BitmapImage(new Uri("ms-appx:///Assets/ButtonIcons/"+asset+".png")),Width=26,Height=26,Stretch=Stretch.Uniform};
     Windows.UI.Xaml.Automation.AutomationProperties.SetName(image,OnexStrings.Get(symbol));
     icons.Children.Add(new Border{Tag="OnexInputGlyphPlate",Width=34,Height=34,Padding=new Thickness(4),CornerRadius=new CornerRadius(7),Background=OnexSettingsBrush(31,42,55),Child=image});
    }
    if(entry.Symbols.Length==0)icons.Children.Add(new TextBlock{Text=entry.Id=="touch"?global::XboxGamingBar.OnexStrings.Get("Touch"):entry.Id=="desktop"?global::XboxGamingBar.OnexStrings.Get("Mode"):global::XboxGamingBar.OnexStrings.Get("Keys"),FontSize=12,TextWrapping=TextWrapping.Wrap});
    line.Children.Add(icons);var copy=new StackPanel{Spacing=3};copy.Children.Add(new TextBlock{Text=entry.Title,FontSize=14,FontWeight=Windows.UI.Text.FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});copy.Children.Add(new TextBlock{Text=entry.Detail,FontSize=12,TextWrapping=TextWrapping.Wrap});Grid.SetColumn(copy,1);line.Children.Add(copy);
    var item=new ContentControl{Template=template,UseSystemFocusVisuals=true,Name="OnexGuide"+entry.Id,Tag=entry.Id,Content=line,IsTabStop=true,MinHeight=64,Padding=new Thickness(2),Margin=new Thickness(0,2,0,2),HorizontalContentAlignment=HorizontalAlignment.Stretch,FocusVisualMargin=new Thickness(2)};
    Windows.UI.Xaml.Automation.AutomationProperties.SetName(item,entry.Title+". "+entry.Detail);_onexNavigationGuideRows.Add(entry.Id,item);_onexNavigationGuide.Children.Add(item);
   }
   ProTipsContent.Children.Clear();ProTipsContent.Children.Add(_onexNavigationGuide);
   ProTipsExpandButton.Checked+=(s,e)=>SetOnexNavigationGuideExpanded(true);ProTipsExpandButton.Unchecked+=(s,e)=>SetOnexNavigationGuideExpanded(false);
   SetOnexNavigationGuideExpanded(ProTipsExpandButton.IsChecked==true);
  }
  private void SetOnexNavigationGuideExpanded(bool expanded)
  {
   isProTipsExpanded=expanded;ProTipsContent.Visibility=expanded?Visibility.Visible:Visibility.Collapsed;ProTipsExpandIcon.Glyph=expanded?"\uE70E":"\uE70D";
   Windows.UI.Xaml.Automation.AutomationProperties.SetName(ProTipsExpandButton,expanded?global::XboxGamingBar.OnexStrings.Get("Hide navigation guide"):global::XboxGamingBar.OnexStrings.Get("Show navigation guide"));ApplyOnexSettingsDesign();
  }
 }
}
