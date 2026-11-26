// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexChoiceContrastAsync(JsonObject report)
  {
   int checks=0,samples=0;var failures=new JsonArray();var rows=new JsonArray();
   void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
   Color Colour(Brush brush)=>brush is SolidColorBrush solid?solid.Color:brush is LinearGradientBrush gradient&&gradient.GradientStops.Count>0?gradient.GradientStops.Last().Color:Colors.Transparent;
   var prefs=ApplicationData.Current.LocalSettings.Values;bool ready=isThemeInitialized,store=_onexThemeQaStore;
   var original=GetVisibleNavigationItems().FirstOrDefault(item=>item.IsChecked==true);var body=(StackPanel)SystemScrollViewer.Content;
   var choice=new ComboBox{Name="ReadOnlyChoiceContrast208",Width=420,MaxDropDownHeight=240};
   foreach(var caption in new[]{"First choice","Second choice","Third choice"})choice.Items.Add(new ComboBoxItem{Content=caption});
   var card=OnexSettingsCard(choice);bool inserted=false;
   try
   {
    Check(_onexDetailsDialog==null&&!OnexSourceEditorsPending&&!_kbmBusy&&_kbmTuning.Count==0,"Protect pending owner edits");
    _onexThemeQaStore=true;isThemeInitialized=false;SystemNavItem.IsChecked=true;body.Children.Insert(0,card);inserted=true;ConfigureOnexFocus(choice);
    // Include mixed dark/light ordering before Miasma and repeat Miasma after
    // selection reuse; this isolates the previous order-dependent finding.
    var names=OnexAllowedThemeNames.Concat(new[]{"Catppuccin Frappé","Catppuccin Latte","Miasma","Windows 11 Light","Miasma"}).ToArray();
    foreach(string name in names)
    {
     isThemeInitialized=false;ApplyTheme(name);SystemNavItem.IsChecked=true;card.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});UpdateLayout();
     choice.IsDropDownOpen=true;await Task.Delay(60);Check(choice.IsDropDownOpen,"Production dropdown did not open");
     foreach(int selected in new[]{0,1,2,0,2,1})
     {
      choice.SelectedIndex=selected;await Task.Delay(4);UpdateLayout();
      foreach(ComboBoxItem item in choice.Items)
      {
       ConfigureOnexFocus(item);ConfigureOnexChoiceColours(item);item.ApplyTemplate();
       var root=VisualTreeHelper.GetChildrenCount(item)>0?VisualTreeHelper.GetChild(item,0) as FrameworkElement:null;
       var surface=root?.FindName("OwnSurface") as Border;var presenter=root?.FindName("OwnChoiceContent") as ContentPresenter;var glyph=root?.FindName("OwnSelectedCheck") as FontIcon;
       Check(surface!=null&&presenter!=null&&glyph!=null,"Owned production template parts missing");
       var states=item.IsSelected?new[]{"Normal","PointerOver","Pressed","Selected","SelectedUnfocused","SelectedPointerOver","SelectedPressed"}:new[]{"Normal","PointerOver","Pressed"};
       foreach(string state in states)
       {
        Check(VisualStateManager.GoToState(item,state,false),"Missing production state: "+state);await Task.Delay(2);
        var ink=Colour(presenter.Foreground);var background=Colour(surface.Background);var markerInk=Colour(glyph.Foreground);double ratio=OnexContrast(ink,background),markerRatio=OnexContrast(markerInk,background);
        var sample=new JsonObject{{"theme",JsonValue.CreateStringValue(name)},{"selectedIndex",JsonValue.CreateNumberValue(selected)},{"selected",JsonValue.CreateBooleanValue(item.IsSelected)},{"state",JsonValue.CreateStringValue(state)},{"ink",JsonValue.CreateStringValue(ink.ToString())},{"background",JsonValue.CreateStringValue(background.ToString())},{"contrast",JsonValue.CreateNumberValue(ratio)},{"checkContrast",JsonValue.CreateNumberValue(markerRatio)}};
        samples++;if(ratio<4.5||(item.IsSelected&&markerRatio<4.5))failures.Add(sample);
        if(name=="Miasma"||name=="Catppuccin Frappé")rows.Add(sample);
       }
      }
     }
     choice.IsDropDownOpen=false;
    }
    report["contrastChecks"]=JsonValue.CreateNumberValue(checks);report["actualStateSamples"]=JsonValue.CreateNumberValue(samples);report["contrastFailures"]=failures;report["readbacks"]=rows;report["paletteVisits"]=JsonValue.CreateNumberValue(names.Length);
    report["realProductionTemplates"]=JsonValue.CreateBooleanValue(true);report["syntheticFixtureLabels"]=JsonValue.CreateBooleanValue(true);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);report["ownerSettingsWrites"]=JsonValue.CreateNumberValue(0);report["rasterCapture"]=JsonValue.CreateBooleanValue(false);
    Check(failures.Count==0,"Production choice contrast below4.5:1; inspect actual presenter/check readbacks");
   }
   finally
   {
    choice.IsDropDownOpen=false;if(inserted)body.Children.Remove(card);isThemeInitialized=false;
    ApplyTheme(prefs.TryGetValue("WidgetTheme",out var latest)?latest as string:OnexPalette.Name);isThemeInitialized=ready;_onexThemeQaStore=store;
    if(original?.Visibility==Visibility.Visible)original.IsChecked=true;
   }
  }
 }
}
