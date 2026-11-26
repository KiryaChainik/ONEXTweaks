using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexSettingsSpacingAsync(JsonObject report)
  {
   int checks=0,samples=0;void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
   var values=ApplicationData.Current.LocalSettings.Values;string saved=values.TryGetValue("WidgetTheme",out var old)?old as string:null;
   bool initialized=isThemeInitialized,tabs=_onexTabsHeader.IsChecked==true,appearance=ThemeExpandButton.IsChecked==true;
   var themeVisibility=ThemeContent.Visibility;var original=GetVisibleNavigationItems().FirstOrDefault(x=>x.IsChecked==true);double scroll=SystemScrollViewer.VerticalOffset;
   double minimum=double.MaxValue,maximum=double.MinValue;var metrics=new JsonArray();report["themes"]=metrics;
   try
   {
    Check(_onexDetailsDialog==null,"Protect owner dialog");isThemeInitialized=false;SystemNavItem.IsChecked=true;
    var body=(StackPanel)SystemScrollViewer.Content;
    var appearanceCard=body.Children.OfType<Border>().Single(x=>OnexOwnedContent(x).Contains(ThemeExpandButton));
    foreach(string name in OnexAllowedThemeNames)
    {
     ApplyTheme(name);ApplyOnexSettingsDesign();UpdateLayout();await Task.Delay(60);
     var theme=_onexAppliedTheme;double caption=Math.Min(OnexContrast(OnexSettingsCaptionColour(theme),theme.CardBackground),OnexContrast(OnexSettingsCaptionColour(theme),theme.ButtonBackground));
     Check(caption>=6.99,"Settings caption contrast too low: "+name+" "+caption);
     Check(_onexDriversIcon!=null&&_onexDriversIcon.ActualWidth>0&&_onexDriversIcon.ActualHeight>0&&!string.IsNullOrEmpty(_onexDriversIcon.Glyph),"Drivers header icon not rendered");
     foreach(var text in OnexOwnedContent(body).OfType<TextBlock>().Where(x=>!string.IsNullOrEmpty(x.Text)))
     {
      Check(text.Opacity==1,"Settings text still has a faded opacity");
      var colour=(text.Foreground as SolidColorBrush)?.Color;
      Check(colour.HasValue&&colour.Value.A==255,"Settings text has a transparent foreground");
      if(text.FontSize>=14||text.FontWeight.Weight>=600)Check(colour.Value==theme.TextPrimary,"Available Settings title/action is not primary text");
     }
     if(name==OnexPalette.Name||name=="Dracula"||name=="Windows 11 Light")
     {
      for(int state=0;state<4;state++)
      {
       _onexTabsHeader.IsChecked=(state&1)!=0;ThemeExpandButton.IsChecked=(state&2)!=0;ThemeContent.Visibility=ThemeExpandButton.IsChecked==true?Visibility.Visible:Visibility.Collapsed;ApplyOnexSettingsDesign();UpdateLayout();
       for(int n=0;n<20;n++)
       {
        // Production page updates used to put StackPanel.Spacing back to12,
        // while Settings separately retained its12px section margins.
        ApplyOnexPageSpacing();if(n%3==0)ApplyOnexSettingsDesign();
        var target=n%2==0?Math.Min(SystemScrollViewer.ScrollableHeight,100+n*24):0;SystemScrollViewer.ChangeView(null,target,null,true);
        UpdateLayout();await Task.Delay(12);
        Check(body.Spacing==0,"A page update reintroduced Settings StackPanel spacing");
        Rect first=_onexTabsCard.TransformToVisual(body).TransformBounds(new Rect(0,0,_onexTabsCard.ActualWidth,_onexTabsCard.ActualHeight));
        Rect next=appearanceCard.TransformToVisual(body).TransformBounds(new Rect(0,0,appearanceCard.ActualWidth,appearanceCard.ActualHeight));double gap=next.Top-first.Bottom;
        minimum=Math.Min(minimum,gap);maximum=Math.Max(maximum,gap);samples++;
        Check(Math.Abs(gap-12)<.75,"Tabs/Appearance actual gap drifted: "+name+" "+gap);
       }
      }
      _onexTabsHeader.IsChecked=false;ThemeExpandButton.IsChecked=true;ThemeContent.Visibility=Visibility.Visible;ApplyOnexSettingsDesign();UpdateLayout();
      var position=appearanceCard.TransformToVisual(body).TransformPoint(new Point()).Y;SystemScrollViewer.ChangeView(null,position-100,null,true);await Task.Delay(80);
      await SaveOnexImage("onex-settings387-"+(name==OnexPalette.Name?"base":name=="Dracula"?"dark":"light")+".png",SystemScrollViewer,50);
      await SaveOnexImage("onex-drivers387-header-"+(name==OnexPalette.Name?"base":name=="Dracula"?"dark":"light")+".png",_onexDriversCard,50);
     }
     metrics.Add(new JsonObject{{"name",JsonValue.CreateStringValue(name)},{"captionContrast",JsonValue.CreateNumberValue(caption)}});
    }
    Check((values.TryGetValue("WidgetTheme",out var fresh)?fresh as string:null)==saved,"Settings audit wrote owner theme");
    report["checks"]=JsonValue.CreateNumberValue(checks);report["actualGapSamples"]=JsonValue.CreateNumberValue(samples);report["minimumGap"]=JsonValue.CreateNumberValue(minimum);report["maximumGap"]=JsonValue.CreateNumberValue(maximum);
    report["realUwpLayout"]=JsonValue.CreateBooleanValue(true);report["scrollProgrammatic"]=JsonValue.CreateBooleanValue(true);report["ownerPhysicalInputTested"]=JsonValue.CreateBooleanValue(false);report["hardwareWritesRequested"]=JsonValue.CreateNumberValue(0);
   }
   finally
   {
    report["checks"]=JsonValue.CreateNumberValue(checks);report["actualGapSamples"]=JsonValue.CreateNumberValue(samples);
    if(samples>0){report["minimumGap"]=JsonValue.CreateNumberValue(minimum);report["maximumGap"]=JsonValue.CreateNumberValue(maximum);}
    _onexTabsHeader.IsChecked=tabs;ThemeExpandButton.IsChecked=appearance;ThemeContent.Visibility=themeVisibility;
    isThemeInitialized=false;ApplyTheme(values.TryGetValue("WidgetTheme",out var fresh)?fresh as string:OnexPalette.Name);isThemeInitialized=initialized;ApplyOnexSettingsDesign();
    if(original!=null&&original.Visibility==Visibility.Visible)original.IsChecked=true;SystemScrollViewer.ChangeView(null,scroll,null,true);
   }
  }
 }
}
