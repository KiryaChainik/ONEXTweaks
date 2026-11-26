// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexSettingsOrderAsync(JsonObject report)
  {
   var body=(StackPanel)SystemScrollViewer.Content;var values=ApplicationData.Current.LocalSettings.Values;
   string savedTheme=values.TryGetValue("WidgetTheme",out var themeRaw)?themeRaw as string:null;
   bool themeReady=isThemeInitialized,debugFlag=isDebugExpanded;
   bool guide=ProTipsExpandButton.IsChecked==true,debug=DebugExpandButton.IsChecked==true,about=AboutExpandButton.IsChecked==true;
   var guideVisible=ProTipsContent.Visibility;var driverVisible=_onexDriversBody.Visibility;var debugVisible=DebugContent.Visibility;var aboutVisible=AboutContent.Visibility;
   var original=GetVisibleNavigationItems().FirstOrDefault(x=>x.IsChecked==true);double scroll=SystemScrollViewer.VerticalOffset;
   var cards=new FrameworkElement[]{OnexNavigationGuideCard,_onexDriversCard,OnexDebugCard,OnexAboutCard};
   var headers=new Control[]{ProTipsExpandButton,_onexDriversExpand,DebugExpandButton,AboutExpandButton};
   int checks=0,geometrySamples=0,focusMoves=0;void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
   void Project(int state)
   {
    bool first=(state&1)!=0,second=(state&2)!=0;
    ProTipsExpandButton.IsChecked=first;SetOnexNavigationGuideExpanded(first);
    // Exercise content growth without launching the component manager or any
    // diagnostics/install action. These two are explicit UI projections.
    SetOnexDriversExpanded(first);DebugExpandButton.IsChecked=second;isDebugExpanded=second;DebugContent.Visibility=second?Visibility.Visible:Visibility.Collapsed;DebugExpandIcon.Glyph=second?"\uE70E":"\uE70D";
    AboutExpandButton.IsChecked=second;SetOnexAboutExpanded(second);ApplyOnexSettingsDesign();UpdateLayout();
   }
   try
   {
    Check(_onexDetailsDialog==null&&!OnexSourceEditorsPending,"Protect owner edits");isThemeInitialized=false;SystemNavItem.IsChecked=true;
    int count=body.Children.Count;
    foreach(var name in new[]{OnexPalette.Name,"Dracula","Windows 11 Light"})
    {
     ApplyTheme(name);SystemNavItem.IsChecked=true;
     for(int state=0;state<4;state++)
     {
      Project(state);await Task.Delay(50);
      for(int repeat=0;repeat<10;repeat++)
      {
       EnsureOnexSettingsSectionOrder();ApplyOnexSettingsDesign();SystemScrollViewer.ChangeView(null,repeat%2==0?SystemScrollViewer.ScrollableHeight:0,null,true);UpdateLayout();
       Check(body.Children.Count==count,"Section ordering duplicated controls");
       var visible=body.Children.OfType<FrameworkElement>().Where(OnexVisiblePageSection).ToArray();
       Check(visible.Skip(visible.Length-4).SequenceEqual(cards),"Actual last four sections are out of order");
       for(int n=0;n<cards.Length;n++)
       {
        var bounds=cards[n].TransformToVisual(body).TransformBounds(new Rect(0,0,cards[n].ActualWidth,cards[n].ActualHeight));
        Check(bounds.Width>0&&bounds.Height>0,"Section not actually laid out");
        if(n>0){var prior=cards[n-1].TransformToVisual(body).TransformBounds(new Rect(0,0,cards[n-1].ActualWidth,cards[n-1].ActualHeight));Check(Math.Abs(bounds.Top-prior.Bottom-12)<.75,"Moved section spacing is inconsistent");}
        geometrySamples++;
       }
       Check(ReferenceEquals(cards[0],OnexNavigationGuideCard)&&ReferenceEquals(cards[1],_onexDriversCard)&&ReferenceEquals(cards[2],OnexDebugCard)&&ReferenceEquals(cards[3],OnexAboutCard),"Section identity changed");
      }
      if(state==0)
      {
       var rows=OnexPageNavigationRows();
       for(int n=0;n<headers.Length;n++)
       {
        Check(rows.SelectMany(row=>row).Any(cell=>ReferenceEquals(cell.Control,headers[n])),"Moved header missing from actual navigation geometry");
        if(n<headers.Length-1)
        {
         var target=OnexPageNavigationTarget(rows,headers[n],2);Check(ReferenceEquals(target,headers[n+1]),"Down navigation uses stale section coordinates");
         Check(target.Focus(FocusState.Keyboard),"Actual moved header cannot receive focus");focusMoves++;
        }
        if(n>0)Check(ReferenceEquals(OnexPageNavigationTarget(rows,headers[n],-2),headers[n-1]),"Up navigation uses stale section coordinates");
       }
      }
     }
     Project(0);OnexNavigationGuideCard.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});UpdateLayout();await Task.Delay(70);
     await SaveOnexImage("onex-settings409-order-"+name.Replace(' ','-')+".png",this,30);
     var sequence=body.Children.ToArray();PerformanceNavItem.IsChecked=true;SystemNavItem.IsChecked=true;EnsureOnexSettingsSectionOrder();UpdateLayout();
     Check(body.Children.SequenceEqual(sequence),"Reopening Settings reorders or duplicates controls");
    }
    Check((values.TryGetValue("WidgetTheme",out var fresh)?fresh as string:null)==savedTheme,"Order check changed owner theme preference");
    report["checks"]=JsonValue.CreateNumberValue(checks);report["actualGeometrySamples"]=JsonValue.CreateNumberValue(geometrySamples);report["actualControlFocusMoves"]=JsonValue.CreateNumberValue(focusMoves);
    report["tail"]=new JsonArray{JsonValue.CreateStringValue("Navigation Guide"),JsonValue.CreateStringValue("Drivers & components"),JsonValue.CreateStringValue("Debug"),JsonValue.CreateStringValue("About")};
    report["navigationTargetCallsUseProductionLogic"]=JsonValue.CreateBooleanValue(true);report["physicalGamepadInputTested"]=JsonValue.CreateBooleanValue(false);report["driverInventoryOrInstallerStarted"]=JsonValue.CreateBooleanValue(false);report["expandedDriverAndDebugAreUiProjections"]=JsonValue.CreateBooleanValue(true);report["hardwareWritesRequested"]=JsonValue.CreateNumberValue(0);
   }
   finally
   {
    ProTipsExpandButton.IsChecked=guide;SetOnexNavigationGuideExpanded(guide);ProTipsContent.Visibility=guideVisible;
    SetOnexDriversExpanded(driverVisible==Visibility.Visible);DebugExpandButton.IsChecked=debug;isDebugExpanded=debugFlag;DebugContent.Visibility=debugVisible;DebugExpandIcon.Glyph=debugFlag?"\uE70E":"\uE70D";
    AboutExpandButton.IsChecked=about;SetOnexAboutExpanded(about);AboutContent.Visibility=aboutVisible;
    ApplyTheme(values.TryGetValue("WidgetTheme",out var fresh)?fresh as string:OnexPalette.Name);isThemeInitialized=themeReady;
    if(original?.Visibility==Visibility.Visible)original.IsChecked=true;SystemScrollViewer.ChangeView(null,scroll,null,true);
   }
  }
 }
}
