// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Automation.Provider;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexSettingsHeadersAsync(JsonObject report)
  {
   var values=ApplicationData.Current.LocalSettings.Values;string savedTheme=values.TryGetValue("WidgetTheme",out var raw)?raw as string:null;
   var original=GetVisibleNavigationItems().FirstOrDefault(x=>x.IsChecked==true);bool themeReady=isThemeInitialized;double scroll=SystemScrollViewer.VerticalOffset;
   var headers=new[]{_onexTabsHeader,ThemeExpandButton,ProTipsExpandButton,_onexDriversExpand,DebugExpandButton,AboutExpandButton};
   var cards=new[]{_onexTabsCard,OnexAppearanceCard,OnexNavigationGuideCard,_onexDriversCard,OnexDebugCard,OnexAboutCard};
   var bodies=new FrameworkElement[]{_onexTabsBody,ThemeContent,ProTipsContent,_onexDriversBody,DebugContent,AboutContent};
   var arrows=new[]{_onexTabsHeaderChevron,ThemeExpandIcon,ProTipsExpandIcon,_onexDriversChevron,DebugExpandIcon,AboutExpandIcon};
   var measures=new JsonArray();report["headerMeasurements"]=measures;
   var expanded=headers.Select(header=>header.IsChecked==true).ToArray();int checks=0,toggles=0,hitPoints=0,focusMoves=0,captures=0;
   void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
   void Collapse(){foreach(var header in headers)header.IsChecked=false;ApplyOnexSettingsDesign();UpdateLayout();}
   async Task Capture(string name)
   {UpdateLayout();await Task.Delay(80);await SaveOnexImage("onex-settings413-"+name+".png",this,30);captures++;}
   try
   {
    Check(_onexDetailsDialog==null&&!OnexSourceEditorsPending,"Protect owner edits");_onexSettingsHeaderQaProjection=true;isThemeInitialized=false;SystemNavItem.IsChecked=true;
    foreach(var name in new[]{OnexPalette.Name,"Dracula","Windows 11 Light"})
    {
     ApplyTheme(name);SystemNavItem.IsChecked=true;Collapse();await Task.Delay(60);string label=name.Replace(' ','-');
     for(int n=0;n<headers.Length;n++)
     {
      var header=headers[n];header.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false,VerticalAlignmentRatio=.5});UpdateLayout();await Task.Delay(60);
      Check(header.Content is Grid,"Header must contain its complete row");Check(header.HorizontalAlignment==HorizontalAlignment.Stretch&&header.HorizontalContentAlignment==HorizontalAlignment.Stretch,"Header must stretch across the card");
      Check(Math.Abs(header.ActualWidth-(cards[n].ActualWidth-26))<1,"Only the chevron is tappable");measures.Add(new JsonObject{{"theme",JsonValue.CreateStringValue(name)},{"index",JsonValue.CreateNumberValue(n)},{"width",JsonValue.CreateNumberValue(header.ActualWidth)},{"height",JsonValue.CreateNumberValue(header.ActualHeight)}});
      Check(header.ActualHeight>=55.25,"Header is smaller than the common minimum touch target");
      Check(Math.Abs(cards[n].ActualHeight-header.ActualHeight-26)<.75,"Collapsed section retains an empty body spacing gap");
      var typography=OnexOwnedContent(header.Content as DependencyObject).OfType<TextBlock>().ToArray();
      Check(typography[0].FontSize==18&&typography[0].FontWeight.Weight==600,"Title typography differs between section headers");
      Check(typography[1].FontSize==12&&typography[1].FontWeight.Weight==400,"Caption incorrectly inherits title size/weight");
      foreach(var text in OnexOwnedContent(header.Content as DependencyObject).OfType<TextBlock>())
      {var ink=text.TransformToVisual(header).TransformBounds(new Rect(0,0,text.ActualWidth,text.ActualHeight));Check(ink.Left>=-.75&&ink.Top>=-.75&&ink.Right<=header.ActualWidth+.75&&ink.Bottom<=header.ActualHeight+.75,"Header text clipped by fixed-height target");}
      foreach(var icon in OnexOwnedContent(header.Content as DependencyObject).OfType<FontIcon>())
      {var glyph=icon.TransformToVisual(header).TransformBounds(new Rect(0,0,icon.ActualWidth,icon.ActualHeight));Check(glyph.Left>=6&&glyph.Right<=header.ActualWidth-6,"Focus outline touches the header icon/chevron");}
      Check(header.BorderThickness.Equals(new Thickness(0))&&header.CornerRadius.Equals(new CornerRadius(8)),"Header has separate nested border or sharp focus corners");Check(OnexHasInlineFocus(header)&&!header.UseSystemFocusVisuals,"Header must use the single owned focus template");
      Check(OnexOwnedContent(header.Content as DependencyObject).OfType<TextBlock>().Count()==2,"Title and caption must share the touch target");
      Check(!OnexOwnedContent(header.Content as DependencyObject).OfType<Control>().Any(),"Nested focus target inside header");
      var host=Window.Current.Content as UIElement;var bounds=header.TransformToVisual(host).TransformBounds(new Rect(0,0,header.ActualWidth,header.ActualHeight));
      foreach(double fraction in new[]{.05,.5,.95})
      {
       var point=new Point(bounds.Left+bounds.Width*fraction,bounds.Top+bounds.Height*.5);
       bool belongs(DependencyObject node){for(var item=node;item!=null;item=VisualTreeHelper.GetParent(item))if(ReferenceEquals(item,header))return true;return false;}
       Check(VisualTreeHelper.FindElementsInHostCoordinates(point,host).Any(belongs),"Header title/centre/chevron is not hit-testable");hitPoints++;
      }
      Check(header.Focus(FocusState.Keyboard),"Whole header cannot receive focus");focusMoves++;
      var peer=new ToggleButtonAutomationPeer(header);var provider=peer.GetPattern(PatternInterface.Toggle) as IToggleProvider;Check(provider!=null,"Missing native Toggle automation pattern");
      provider.Toggle();toggles++;await Task.Delay(40);UpdateLayout();
      Check(header.IsChecked==true&&bodies[n].Visibility==Visibility.Visible&&arrows[n].Glyph=="\uE70E","Native automation did not expand the actual section");
      // Retained native Click handlers must not flip the state back after the
      // ToggleButton has already changed IsChecked.
      if(n==1)ThemeExpandButton_Click(header,null);if(n==2)ProTipsExpandButton_Click(header,null);if(n==4)DebugExpandButton_Click(header,null);if(n==5)AboutExpandButton_Click(header,null);
      Check(header.IsChecked==true&&bodies[n].Visibility==Visibility.Visible,"Click double-toggles the section");
      foreach(var state in new[]{"Checked","CheckedPointerOver","CheckedPressed"})
      {
       Check(VisualStateManager.GoToState(header,state,false),"Checked interaction state missing");
       foreach(var text in OnexOwnedContent(header.Content as DependencyObject).OfType<TextBlock>())Check(text.Opacity==1,"Enabled header text is dimmed by interaction");
      }
      provider.Toggle();toggles++;await Task.Delay(30);UpdateLayout();
      Check(header.IsChecked==false&&bodies[n].Visibility==Visibility.Collapsed&&arrows[n].Glyph=="\uE70D","Native automation did not collapse the actual section");
     }
     Collapse();var rows=OnexPageNavigationRows();
     for(int n=0;n<headers.Length-1;n++)
     {Check(ReferenceEquals(OnexPageNavigationTarget(rows,headers[n],2),headers[n+1]),"Down skips a whole-row header");Check(ReferenceEquals(OnexPageNavigationTarget(rows,headers[n+1],-2),headers[n]),"Up skips a whole-row header");}
     SystemScrollViewer.ChangeView(null,0,null,true);await Capture(label+"-top");SystemScrollViewer.ChangeView(null,SystemScrollViewer.ScrollableHeight,null,true);await Capture(label+"-bottom");
     headers[0].IsChecked=true;SystemScrollViewer.ChangeView(null,0,null,true);await Capture(label+"-tabs");headers[0].IsChecked=false;
     headers[1].IsChecked=true;ThemeExpandButton.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false,VerticalAlignmentRatio=0});await Capture(label+"-appearance");headers[1].IsChecked=false;
    }
    Check((values.TryGetValue("WidgetTheme",out var fresh)?fresh as string:null)==savedTheme,"Header checks changed owner theme preference");
    report["checks"]=JsonValue.CreateNumberValue(checks);report["headers"]=JsonValue.CreateNumberValue(headers.Length);report["actualUiAutomationToggleCalls"]=JsonValue.CreateNumberValue(toggles);report["actualHeaderHitTestPoints"]=JsonValue.CreateNumberValue(hitPoints);report["actualControlFocusMoves"]=JsonValue.CreateNumberValue(focusMoves);report["captures"]=JsonValue.CreateNumberValue(captures);
    report["driverInventorySuppressedForQaProjection"]=JsonValue.CreateBooleanValue(true);report["physicalGamepadOrTouchInputTested"]=JsonValue.CreateBooleanValue(false);report["hardwareWritesRequested"]=JsonValue.CreateNumberValue(0);
   }
   finally
   {
    for(int n=0;n<headers.Length;n++)headers[n].IsChecked=expanded[n];
    ApplyTheme(values.TryGetValue("WidgetTheme",out var fresh)?fresh as string:OnexPalette.Name);isThemeInitialized=themeReady;_onexSettingsHeaderQaProjection=false;
    if(original?.Visibility==Visibility.Visible)original.IsChecked=true;SystemScrollViewer.ChangeView(null,scroll,null,true);
   }
  }
 }
}
