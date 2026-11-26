using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.ApplicationModel.Resources.Core;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Automation.Provider;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexAboutAsync(JsonObject report)
  {
   int checks=0;void Check(bool ok,string reason){checks++;if(!ok)throw new InvalidOperationException(reason);}
   var values=ApplicationData.Current.LocalSettings.Values;
   string stored=values.TryGetValue("WidgetTheme",out var value)?value as string:null;
   string tabs=ReadOnexTabValue(OnexTabsKey);
   bool expanded=AboutExpandButton.IsChecked==true,initialized=isThemeInitialized;
   string caption=OnexSupportAuthorCaption.Text,alt=AutomationProperties.GetName(OnexSupportAuthorQr);
   double oldScroll=SystemScrollViewer.VerticalOffset;
   string originalTheme=OnexPalette.Name;
   var original=GetVisibleNavigationItems().FirstOrDefault(x=>x.IsChecked==true);
   var toggle=(IToggleProvider)new ToggleButtonAutomationPeer(AboutExpandButton).GetPattern(PatternInterface.Toggle);
   try
   {
    EnsureOnexAbout();SystemNavItem.IsChecked=true;
    if(AboutExpandButton.IsChecked==true)toggle.Toggle();
    Check(AboutContent.Visibility==Visibility.Collapsed,"About does not collapse");
    Check(!OnexPageNavigationRows().SelectMany(x=>x).Any(x=>ReferenceEquals(x.Control,OnexClawTweaksThanksLink)),"Collapsed About link remains in focus graph");
    toggle.Toggle();Check(AboutContent.Visibility==Visibility.Visible,"Native toggle provider did not expand About");
    Check(OnexAboutAuthorText.Text=="KiryaChainik","Wrong ONEXTweaks author");
    var v=Windows.ApplicationModel.Package.Current.Id.Version;
    Check(AboutVersionText.Text==$"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}","About shows a stale version");
    Check(OnexAboutPurposeText.Text.StartsWith("ONEXTweaks")&&OnexAboutPurposeText.Text.Contains("ONEXPLAYER"),"Purpose is missing");
    Check(OnexAboutComponentsText.Text.Contains("Bridge")&&OnexAboutComponentsText.Text.Contains("Power Agent")&&OnexAboutComponentsText.Text.Contains("VIIPER")&&OnexAboutComponentsText.Text.Contains("RTSS"),"Component roles are missing");
    Check(OnexClawTweaksThanksLink.NavigateUri==null&&OnexClawTweaksSourceUrl=="https://github.com/enterTheVoidCode/ClawTweaks","Wrong credit source URL");
    Check((OnexClawTweaksThanksLink.Content as TextBlock)?.Text=="ClawTweaks — enterTheVoidCode (Riff_Lash)","ClawTweaks credit is missing");
    Check(AutoUpdateCheckToggle.Visibility==Visibility.Collapsed&&!AutoUpdateCheckToggle.IsEnabled&&!AutoUpdateCheckToggle.IsTabStop,"Dormant upstream updater still offered");
    // Enumerate only the owned logical XAML: no framework template descendants.
    var direct=AboutContent.Children.ToArray();
    var owned=direct.Concat(direct.OfType<StackPanel>().SelectMany(x=>x.Children)).ToArray();
    Check(owned.OfType<Image>().Count()==1&&owned.OfType<Image>().Single()==OnexSupportAuthorQr,"Expected one supplied QR image");
    Check(OnexSupportAuthorCaption.Text==OnexStrings.Get("Support author"),"Selected-language caption differs from runtime");
    Check(AutomationProperties.GetName(OnexSupportAuthorQr)==OnexStrings.Get("Support author QR code"),"QR accessibility does not follow selected language");
    var qrFile=await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/support-author-qr.png"));
    var qrBytes=await FileIO.ReadBufferAsync(qrFile);
    string qrHash=CryptographicBuffer.EncodeToHexString(HashAlgorithmProvider.OpenAlgorithm(HashAlgorithmNames.Sha256).HashData(qrBytes));
    Check(string.Equals(qrHash,"46b6dc8808e8ef4c683eca12c8e2c0f48e8b5c1de612745bc0a0e58c61d1022f",StringComparison.OrdinalIgnoreCase),"Supplied original QR was altered");
    Check(OnexSupportAuthorQr.Stretch==Windows.UI.Xaml.Media.Stretch.Uniform&&OnexSupportAuthorQr.Width==OnexSupportAuthorQr.Height,"QR aspect ratio changed");
    for(int wait=0;wait<100&&(OnexSupportAuthorQr.Source as BitmapImage)?.PixelWidth!=1440;wait++)await Task.Delay(40);
    Check((OnexSupportAuthorQr.Source as BitmapImage)?.PixelWidth==1440&&(OnexSupportAuthorQr.Source as BitmapImage)?.PixelHeight==1440,"Packaged QR failed to decode as image");
    report["originalQrSha256"]=JsonValue.CreateStringValue(qrHash);
    Check(direct.OfType<StackPanel>().SelectMany(x=>x.Children).OfType<HyperlinkButton>().Count()==1,"Unexpected action in About");
    foreach(string theme in new[]{originalTheme,"Windows 11 Light"})
    foreach(string language in new[]{"en-US","ru-RU"})
    {
     isThemeInitialized=false;ApplyTheme(theme);
     var context=new ResourceContext{Languages=new[]{language}};
     var resources=ResourceManager.Current.MainResourceMap.GetSubtree("Resources");
     string localizedCaption=resources.GetValue("OnexSupportAuthorCaption/Text",context).ValueAsString;
     var altResources=resources.Where(entry=>entry.Key.StartsWith("OnexSupportAuthorQr/",StringComparison.Ordinal)).ToArray();
     Check(altResources.Length==1,"Expected one qualified MRT accessibility resource");
     string localizedAlt=altResources[0].Value.Resolve(context).ValueAsString;
     Check(localizedCaption==(language=="ru-RU"?"Поддержать автора":"Support author"),"MRT caption resource mismatch");
     Check(localizedAlt==OnexStrings.GetForLanguage("Support author QR code",language=="ru-RU"?"ru":"en"),"MRT accessibility resource mismatch");
     // Read-only presentation projection: no owner language preference or PrimaryLanguageOverride writes.
     OnexSupportAuthorCaption.Text=localizedCaption;AutomationProperties.SetName(OnexSupportAuthorQr,localizedAlt);
     UpdateLayout();await Task.Delay(140);
     Check(!OnexSupportAuthorCaption.IsTextTrimmed&&OnexSupportAuthorCaption.ActualHeight>0,"Support caption clips");
     foreach(var text in new[]{OnexAboutPurposeText,OnexAboutComponentsText,OnexAboutCreditsText})
      Check(text.TextWrapping==TextWrapping.Wrap&&!text.IsTextTrimmed&&text.ActualWidth>350&&text.ActualHeight>0,"About copy clips or does not wrap");
     Check(OnexClawTweaksThanksLink.Focus(FocusState.Keyboard),"Credit source link cannot receive focus");
     Check(OnexPageNavigationRows().SelectMany(x=>x).Any(x=>ReferenceEquals(x.Control,OnexClawTweaksThanksLink)),"Credit source missing from page focus graph");
     Check(AboutExpandButton.Focus(FocusState.Keyboard),"About header cannot regain focus");
     var box=OnexAboutCard.TransformToVisual(SystemScrollViewer.Content as UIElement).TransformBounds(new Windows.Foundation.Rect(0,0,OnexAboutCard.ActualWidth,OnexAboutCard.ActualHeight));
     SystemScrollViewer.ChangeView(null,box.Y,null,true);await Task.Delay(160);
     string prefix="onex-about467-"+language+"-"+(theme=="Windows 11 Light"?"light":"dark");
     await SaveOnexImage(prefix+"-full.png",OnexAboutCard,60);
     OnexSupportAuthorQr.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(100);UpdateLayout();
     var bounds=OnexSupportAuthorQr.TransformToVisual(SystemScrollViewer).TransformBounds(new Windows.Foundation.Rect(0,0,OnexSupportAuthorQr.ActualWidth,OnexSupportAuthorQr.ActualHeight));
     Check(bounds.Y>=-1&&bounds.Bottom<=SystemScrollViewer.ActualHeight+1&&bounds.X>=0&&bounds.Right<=SystemScrollViewer.ActualWidth,"QR clips outside viewport after scrolling");
     Check(OnexSupportAuthorQr.ActualWidth==240&&OnexSupportAuthorQr.ActualHeight==240,"QR changed rendered size");
     await SaveOnexImage(prefix+"-viewport.png",SystemScrollViewer,60);
     await SaveOnexImage(prefix+"-qr.png",OnexSupportAuthorQr,60);
    }
    Check((values.TryGetValue("WidgetTheme",out var current)?current as string:null)==stored&&ReadOnexTabValue(OnexTabsKey)==tabs,"About inspection changed owner settings");
    report["checks"]=JsonValue.CreateNumberValue(checks);report["actualOwnUwpRendering"]=JsonValue.CreateBooleanValue(true);
    report["mrtLanguagesValidated"]=new JsonArray{JsonValue.CreateStringValue("en-US"),JsonValue.CreateStringValue("ru-RU")};
    report["alternateLocalePresentationProjection"]=JsonValue.CreateBooleanValue(true);
    report["ownerLanguagePreferenceWritten"]=JsonValue.CreateBooleanValue(false);
    report["nativeToggleProvider"]=JsonValue.CreateBooleanValue(true);report["externalLinkLaunched"]=JsonValue.CreateBooleanValue(false);
    report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);report["hardwareWritesRequested"]=JsonValue.CreateNumberValue(0);
   }
   finally
   {
    OnexSupportAuthorCaption.Text=caption;AutomationProperties.SetName(OnexSupportAuthorQr,alt);
    if(AboutExpandButton.IsChecked!=expanded)toggle.Toggle();
    isThemeInitialized=false;string fresh=values.TryGetValue("WidgetTheme",out var current)?current as string:OnexPalette.Name;
    ApplyTheme(fresh??OnexPalette.Name);isThemeInitialized=initialized;
    SystemScrollViewer.ChangeView(null,oldScroll,null,true);
    if(original!=null&&original.Visibility==Visibility.Visible)original.IsChecked=true;
   }
  }
 }
}
