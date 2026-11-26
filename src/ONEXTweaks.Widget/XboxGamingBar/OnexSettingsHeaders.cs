// SPDX-License-Identifier: AGPL-3.0-only
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private FontIcon _onexTabsHeaderChevron;
  private bool _onexSettingsHeadersWired,_onexSettingsHeaderQaProjection;
  private bool IsOnexSettingsSectionHeader(ToggleButton header)=>ReferenceEquals(header,_onexTabsHeader)||ReferenceEquals(header,ThemeExpandButton)||ReferenceEquals(header,ProTipsExpandButton)||ReferenceEquals(header,_onexDriversExpand)||ReferenceEquals(header,DebugExpandButton)||ReferenceEquals(header,AboutExpandButton);
  private void EnsureOnexSettingsHeaders()
  {
   if(_onexSettingsHeadersWired||!OnexFrontend.UsesOwnBackend)return;_onexSettingsHeadersWired=true;
   // Checked/Unchecked is the single state for touch, keyboard and UI Automation.
   // The retained Click handlers read that state instead of toggling it again.
   ThemeExpandButton.Checked+=(s,e)=>SetOnexAppearanceExpanded(true);ThemeExpandButton.Unchecked+=(s,e)=>SetOnexAppearanceExpanded(false);
   DebugExpandButton.Checked+=(s,e)=>SetOnexDebugExpanded(true);DebugExpandButton.Unchecked+=(s,e)=>SetOnexDebugExpanded(false);
   SetOnexAppearanceExpanded(ThemeExpandButton.IsChecked==true);SetOnexDebugExpanded(DebugExpandButton.IsChecked==true);
  }
  private void SetOnexAppearanceExpanded(bool expanded)
  {
   bool changed=_themeExpanded!=expanded||ThemeContent.Visibility!=(expanded?Visibility.Visible:Visibility.Collapsed);
   _themeExpanded=expanded;ThemeContent.Visibility=expanded?Visibility.Visible:Visibility.Collapsed;ThemeExpandIcon.Glyph=expanded?"\uE70E":"\uE70D";
   AutomationProperties.SetName(ThemeExpandButton,expanded?global::XboxGamingBar.OnexStrings.Get("Hide Appearance"):global::XboxGamingBar.OnexStrings.Get("Show Appearance"));if(changed)ApplyOnexSettingsDesign();
  }
  private void SetOnexDebugExpanded(bool expanded)
  {
   bool changed=isDebugExpanded!=expanded||DebugContent.Visibility!=(expanded?Visibility.Visible:Visibility.Collapsed);
   isDebugExpanded=expanded;DebugContent.Visibility=expanded?Visibility.Visible:Visibility.Collapsed;DebugExpandIcon.Glyph=expanded?"\uE70E":"\uE70D";
   AutomationProperties.SetName(DebugExpandButton,expanded?global::XboxGamingBar.OnexStrings.Get("Hide Debug"):global::XboxGamingBar.OnexStrings.Get("Show Debug"));if(changed)ApplyOnexSettingsDesign();
  }
 }
}
