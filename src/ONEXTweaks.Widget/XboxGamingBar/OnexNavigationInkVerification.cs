using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task ReadOnexNavigationInkAsync(JsonObject report)
  {
   int steps=0,oldMismatches=0,productionMismatches=0;var samples=new JsonArray();
   void Check(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
   string Ink(Brush brush)=>brush is SolidColorBrush solid?solid.Color.ToString():brush?.GetType().Name??"null";
   var values=ApplicationData.Current.LocalSettings.Values;string saved=values.TryGetValue("WidgetTheme",out var raw)?raw as string:null;
   bool ready=isThemeInitialized,store=_onexThemeQaStore;var original=GetVisibleNavigationItems().FirstOrDefault(x=>x.IsChecked==true);
   void Inspect(string state)
   {
    var theme=_onexAppliedTheme;var tabs=GetVisibleNavigationItems();Check(tabs.Count(x=>x.IsChecked==true)==1,"Readback requires one selected tab");
    foreach(var tab in tabs)
    {
     bool selected=tab.IsChecked==true;var oldExpected=selected?theme.TextPrimary:theme.TextSecondary;var expected=selected?(theme.SelectionText??theme.TextPrimary):theme.TextSecondary;
     tab.ApplyTemplate();Check(VisualTreeHelper.GetChildrenCount(tab)>0,"Owned nav template unavailable");var root=VisualTreeHelper.GetChild(tab,0) as FrameworkElement;Check(root?.Name=="RootGrid","Read only known owned navigation template");
     var content=root.FindName("ContentPresenter") as ContentPresenter;var frame=root.FindName("SelectionBackground") as Border;Check(content!=null&&frame!=null,"Owned nav parts missing");
     var icons=new JsonArray();foreach(var icon in OnexNavigationIcons(tab.Content as DependencyObject))icons.Add(new JsonObject{{"kind",JsonValue.CreateStringValue(icon.GetType().Name)},{"ink",JsonValue.CreateStringValue(Ink(icon.Foreground))}});
     var shapes=new JsonArray();if(ReferenceEquals(tab,_onexKeyboardMouseNav)&&tab.Content is Viewbox view&&view.Child is Canvas canvas)foreach(var path in canvas.Children.OfType<Windows.UI.Xaml.Shapes.Path>())shapes.Add(JsonValue.CreateStringValue(Ink(path.Fill)));
     string controlInk=Ink(tab.Foreground),presenterInk=Ink(content.Foreground);bool oldMatch=controlInk==oldExpected.ToString()&&presenterInk==oldExpected.ToString()&&icons.All(x=>x.GetObject().GetNamedString("ink")==oldExpected.ToString())&&shapes.All(x=>x.GetString()==oldExpected.ToString());
     bool productionMatch=controlInk==expected.ToString()&&presenterInk==expected.ToString()&&icons.All(x=>x.GetObject().GetNamedString("ink")==expected.ToString())&&shapes.All(x=>x.GetString()==expected.ToString());if(!oldMatch)oldMismatches++;if(!productionMatch)productionMismatches++;
     var background=selected&&frame.Visibility==Visibility.Visible&&frame.Background is SolidColorBrush actualSurface&&actualSurface.Color.A==255?actualSurface.Color:theme.PageBackground;
     double opacity=tab.Content is UIElement artwork?artwork.Opacity:1;double contrast=tab.Foreground is SolidColorBrush ink?OnexContrast(ink.Color,background):0;
     samples.Add(new JsonObject{{"theme",JsonValue.CreateStringValue(theme.Name)},{"selectedTab",JsonValue.CreateStringValue(tabs.Single(x=>x.IsChecked==true).Tag as string??"")},{"tab",JsonValue.CreateStringValue(tab.Tag as string??"")},{"commonStateAppliedToSelectedTab",JsonValue.CreateStringValue(state)},{"selected",JsonValue.CreateBooleanValue(selected)},{"themePrimary",JsonValue.CreateStringValue(theme.TextPrimary.ToString())},{"themeSecondary",JsonValue.CreateStringValue(theme.TextSecondary.ToString())},{"themeSelectionText",JsonValue.CreateStringValue(theme.SelectionText?.ToString()??"null")},{"old169Expected",JsonValue.CreateStringValue(oldExpected.ToString())},{"productionExpected",JsonValue.CreateStringValue(expected.ToString())},{"controlInk",JsonValue.CreateStringValue(controlInk)},{"presenterInk",JsonValue.CreateStringValue(presenterInk)},{"icons",icons},{"kbmShapeFills",shapes},{"old169Matches",JsonValue.CreateBooleanValue(oldMatch)},{"productionMatches",JsonValue.CreateBooleanValue(productionMatch)},{"selectionFrameVisible",JsonValue.CreateBooleanValue(frame.Visibility==Visibility.Visible)},{"frameBackground",JsonValue.CreateStringValue(Ink(frame.Background))},{"frameBorder",JsonValue.CreateStringValue(Ink(frame.BorderBrush))},{"declaredContrastSurface",JsonValue.CreateStringValue(background.ToString())},{"foregroundContrastAgainstDeclaredSurface",JsonValue.CreateNumberValue(contrast)},{"contentOpacity",JsonValue.CreateNumberValue(opacity)},{"pageBackground",JsonValue.CreateStringValue(theme.PageBackground.ToString())}});
    }
   }
   try
   {
    Check(_onexDetailsDialog==null&&!OnexSourceEditorsPending,"Protect pending owner edits");_onexThemeQaStore=true;isThemeInitialized=false;
    foreach(string name in new[]{OnexPalette.Name,"Miasma","Catppuccin Latte","Miasma"})
    {
     ApplyTheme(name);var tabs=GetVisibleNavigationItems().ToArray();
     for(int lap=0;lap<2;lap++)foreach(var tab in lap==0?tabs:tabs.Reverse())
     {
      tab.IsChecked=true;UpdateLayout();await Task.Delay(20);
      foreach(string state in new[]{"Normal","PointerOver","Normal"}){VisualStateManager.GoToState(tab,state,false);UpdateLayout();Inspect(state);steps++;}
     }
    }
    Check((values.TryGetValue("WidgetTheme",out var fresh)?fresh as string:null)==saved,"Theme preference changed during read-only collection");
    report["navigationInkSamples"]=samples;report["readbackCompleted"]=JsonValue.CreateBooleanValue(true);report["colourAcceptanceClaimed"]=JsonValue.CreateBooleanValue(false);report["old169MismatchSamples"]=JsonValue.CreateNumberValue(oldMismatches);report["productionMismatchSamples"]=JsonValue.CreateNumberValue(productionMismatches);report["programmaticStatesApplied"]=JsonValue.CreateNumberValue(steps);report["productionColoursChanged"]=JsonValue.CreateBooleanValue(false);report["ownerSettingsWritten"]=JsonValue.CreateBooleanValue(false);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);report["rasterCapture"]=JsonValue.CreateBooleanValue(false);report["contrastIsDeclaredBrushMathNotPixels"]=JsonValue.CreateBooleanValue(true);
   }
   finally
   {
    isThemeInitialized=false;ApplyTheme(values.TryGetValue("WidgetTheme",out var fresh)?fresh as string:OnexPalette.Name);isThemeInitialized=ready;_onexThemeQaStore=store;if(original?.Visibility==Visibility.Visible)original.IsChecked=true;
   }
  }
  private async Task VerifyOnexNavigationInkAsync(JsonObject report)
  {
   int checks=0,transitions=0,shapesChecked=0;void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
   var values=ApplicationData.Current.LocalSettings.Values;string saved=values.TryGetValue("WidgetTheme",out var raw)?raw as string:null;
   bool initialized=isThemeInitialized;var original=GetVisibleNavigationItems().FirstOrDefault(x=>x.IsChecked==true);
   void Inspect()
   {
    var theme=_onexAppliedTheme;var tabs=GetVisibleNavigationItems();Check(tabs.Count(x=>x.IsChecked==true)==1,"More than one selected tab");
    foreach(var tab in tabs)
    {
     // Native211 confirmed Miasma intentionally uses SelectionText on its
     // selected gold surface; TextPrimary is the page's different text role.
     var expected=tab.IsChecked==true?(theme.SelectionText??theme.TextPrimary):theme.TextSecondary;
     Check(tab.Foreground is SolidColorBrush ink&&ink.Color==expected,"Retained tab ink: "+theme.Name+"/"+tab.Tag);
     foreach(var icon in OnexNavigationIcons(tab.Content as DependencyObject))Check(icon.Foreground is SolidColorBrush tint&&tint.Color==expected,"Retained icon ink: "+theme.Name+"/"+tab.Tag);
     if(ReferenceEquals(tab,_onexKeyboardMouseNav)&&tab.Content is Viewbox view&&view.Child is Canvas canvas)
      foreach(var path in canvas.Children.OfType<Windows.UI.Xaml.Shapes.Path>()){Check(path.Fill is SolidColorBrush fill&&fill.Color==expected,"Keyboard/mouse retained previous ink");shapesChecked++;}
     tab.ApplyTemplate();Check(VisualTreeHelper.GetChildrenCount(tab)>0,"Owned nav template not realized");var root=VisualTreeHelper.GetChild(tab,0) as FrameworkElement;
     Check(root?.Name=="RootGrid","Only inspect the known owned navigation template");var content=root.FindName("ContentPresenter") as ContentPresenter;var frame=root.FindName("SelectionBackground") as Border;
     Check(content!=null&&content.Foreground is SolidColorBrush foreground&&foreground.Color==expected,"Navigation VSM overrides foreground: "+theme.Name+"/"+tab.Tag);
     Check(frame!=null&&(frame.Visibility==Visibility.Visible)==(tab.IsChecked==true),"Old selection frame stayed visible");
    }
   }
   try
   {
    Check(_onexDetailsDialog==null&&!OnexSourceEditorsPending,"Protect owner edit");isThemeInitialized=false;int index=0;
    foreach(string name in OnexAllowedThemeNames)
    {
     ApplyTheme(name);var tabs=GetVisibleNavigationItems();
     for(int lap=0;lap<2;lap++)foreach(var tab in lap==0?tabs:tabs.AsEnumerable().Reverse().ToList())
     {
      tab.IsChecked=true;UpdateLayout();await Task.Delay(145);Inspect();
      VisualStateManager.GoToState(tab,"PointerOver",false);UpdateLayout();Inspect();VisualStateManager.GoToState(tab,"Normal",false);Inspect();transitions++;
     }
     foreach(var sample in new[]{Tuple.Create("home",PerformanceNavItem),Tuple.Create("settings",SystemNavItem)})
     {
      sample.Item2.IsChecked=true;UpdateLayout();await Task.Delay(145);Inspect();var panel=MainNavPanel as Panel;var old=panel.Background;panel.Background=new SolidColorBrush(_onexAppliedTheme.PageBackground);
      try{await SaveOnexImage("onex-nav392-"+index+"-"+sample.Item1+".png",panel,30);}finally{panel.Background=old;}
     }
     if(OnexLight(_onexAppliedTheme))
     {
      bool tabsExpanded=_onexTabsHeader.IsChecked==true;var bodyVisibility=_onexTabsBody.Visibility;
      try
      {
       _onexTabsHeader.IsChecked=true;_onexTabsBody.Visibility=Visibility.Visible;ApplyOnexSettingsDesign();UpdateLayout();
       Check(_onexTabsCard.Background is SolidColorBrush surface&&surface.Color.A==255,"Light Settings capture has no opaque real card background");
       foreach(var row in _onexTabRows.Values)
        Check(row.Visible.Foreground is SolidColorBrush text&&OnexContrast(text.Color,_onexAppliedTheme.CardBackground)>=4.5,"Light tab label contrast is insufficient");
       // Capture the real parent card, including its background. A transparent
       // rows-only PNG is not a valid visual readability check.
       _onexTabsCard.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});UpdateLayout();
       await SaveOnexImage("onex-nav392-settings-"+index+".png",_onexTabsCard,30);
      }
      finally{_onexTabsHeader.IsChecked=tabsExpanded;_onexTabsBody.Visibility=bodyVisibility;ApplyOnexSettingsDesign();}
     }
     index++;report["completedThemes"]=JsonValue.CreateNumberValue(index);var progress=await ApplicationData.Current.LocalFolder.CreateFileAsync("onex-nav-ink-progress.json",CreationCollisionOption.ReplaceExisting);await FileIO.WriteTextAsync(progress,report.Stringify());
    }
    Check(shapesChecked>0,"Keyboard/mouse shapes were not checked");Check((values.TryGetValue("WidgetTheme",out var fresh)?fresh as string:null)==saved,"Nav test changed owner theme");
    report["checks"]=JsonValue.CreateNumberValue(checks);report["themes"]=JsonValue.CreateNumberValue(OnexAllowedThemeNames.Count());report["actualTabTransitions"]=JsonValue.CreateNumberValue(transitions);report["keyboardMouseShapeSamples"]=JsonValue.CreateNumberValue(shapesChecked);report["programmaticHover"]=JsonValue.CreateBooleanValue(true);report["ownerPhysicalInputTested"]=JsonValue.CreateBooleanValue(false);report["hardwareWritesRequested"]=JsonValue.CreateNumberValue(0);
   }
   finally
   {
    isThemeInitialized=false;ApplyTheme(values.TryGetValue("WidgetTheme",out var fresh)?fresh as string:OnexPalette.Name);isThemeInitialized=initialized;
    if(original!=null&&original.Visibility==Visibility.Visible)original.IsChecked=true;
   }
  }
 }
}
