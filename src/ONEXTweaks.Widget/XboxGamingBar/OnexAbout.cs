using Windows.ApplicationModel;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;

namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private const string OnexClawTweaksSourceUrl="https://github.com/enterTheVoidCode/ClawTweaks";
  private bool _onexAboutReady;
  private void EnsureOnexAbout()
  {
   if(_onexAboutReady||AboutContent==null||!OnexFrontend.UsesOwnBackend)return;
   _onexAboutReady=true;
   AboutExpandButton.Checked+=(s,e)=>SetOnexAboutExpanded(true);
   AboutExpandButton.Unchecked+=(s,e)=>SetOnexAboutExpanded(false);
   SetOnexAboutExpanded(AboutExpandButton.IsChecked==true);
  }
  private void SetOnexAboutExpanded(bool expanded)
  {
   isAboutExpanded=expanded;AboutContent.Visibility=expanded?Visibility.Visible:Visibility.Collapsed;
   AboutExpandIcon.Glyph=expanded?"\uE70E":"\uE70D";
   var v=Package.Current.Id.Version;AboutVersionText.Text=$"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
   Windows.UI.Xaml.Automation.AutomationProperties.SetName(AboutExpandButton,expanded?global::XboxGamingBar.OnexStrings.Get("Hide About"):global::XboxGamingBar.OnexStrings.Get("Show About"));
   Windows.UI.Xaml.Automation.AutomationProperties.SetName(OnexClawTweaksThanksLink,global::XboxGamingBar.OnexStrings.Get("ClawTweaks by enterTheVoidCode, also credited as Riff_Lash. Open its source project on GitHub."));
   ApplyOnexSettingsDesign();
  }
  private async void OnexClawTweaksThanks_Click(object sender,RoutedEventArgs args)
  {
   OnexClawTweaksThanksLink.IsEnabled=false;OnexAboutLinkStatus.Visibility=Visibility.Collapsed;
   try
   {
    // Use the existing full-trust web-tile route: direct UWP URI launch is unreliable in Game Bar.
    await SendOnexHomeRequestAsync(new ValueSet{["OnexDesktopAction"]="launch",["LaunchTarget"]=OnexClawTweaksSourceUrl,["InGameBar"]=widget!=null});
   }
   catch
   {OnexAboutLinkStatus.Text=global::XboxGamingBar.OnexStrings.Get("Could not open the source project. Check the ONEXTweaks Bridge connection and try again.");OnexAboutLinkStatus.Visibility=Visibility.Visible;}
   finally{OnexClawTweaksThanksLink.IsEnabled=true;}
  }
 }
}
