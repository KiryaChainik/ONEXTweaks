using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Automation.Provider;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexNavigationGuideAsync(JsonObject report)
  {
   var values=ApplicationData.Current.LocalSettings.Values;string stored=values.TryGetValue("WidgetTheme",out var old)?old as string:null;
   string tabs=ReadOnexTabValue(OnexTabsKey);bool expanded=ProTipsExpandButton.IsChecked==true,initialized=isThemeInitialized;var original=GetVisibleNavigationItems().FirstOrDefault(x=>x.IsChecked==true);int checks=0;
   void Check(bool ok,string reason){checks++;if(!ok)throw new InvalidOperationException(reason);}
   var toggle=(IToggleProvider)new ToggleButtonAutomationPeer(ProTipsExpandButton).GetPattern(PatternInterface.Toggle);StackPanel fixture=null;int inputs=0;string run=Guid.NewGuid().ToString("N");var folder=ApplicationData.Current.LocalFolder;
   async Task Input(string key)
   {
    string id=run+":"+(++inputs);await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-guide365-input.json",CreationCollisionOption.ReplaceExisting),new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"key",JsonValue.CreateStringValue(key)},{"utc",JsonValue.CreateStringValue(DateTimeOffset.UtcNow.ToString("O"))}}.Stringify());
    bool accepted=false;for(int n=0;n<100;n++){await Task.Delay(50);try{accepted=await FileIO.ReadTextAsync(await folder.GetFileAsync("onex-guide365-input-ack.txt"))==id;}catch{}if(accepted)break;}
    Check(accepted,"Own focused keyboard fixture did not acknowledge "+key);await Task.Delay(90);
   }
   try
   {
    EnsureOnexNavigationGuide();SystemNavItem.IsChecked=true;
    if(ProTipsExpandButton.IsChecked==true)toggle.Toggle();Check(ProTipsContent.Visibility==Visibility.Collapsed,"Guide initially fails to collapse");
    Check(!OnexPageNavigationRows().SelectMany(x=>x).Any(x=>x.Control.Name.StartsWith("OnexGuide")),"Collapsed guide rows remain in the focus graph");
    toggle.Toggle();Check(ProTipsContent.Visibility==Visibility.Visible,"Native toggle provider did not expand guide");
    Check(_onexNavigationGuideRows.Count==11,"Guide contexts are missing");
    foreach(string name in new[]{OnexPalette.Name,"Windows 11 Light"})
    {
     isThemeInitialized=false;ApplyTheme(name);UpdateLayout();await Task.Delay(180);
     foreach(var entry in OnexGuideEntries)
     {
      var row=_onexNavigationGuideRows[entry.Id];row.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(30);UpdateLayout();
      Check(row.Focus(FocusState.Keyboard),"Guide row cannot receive gamepad focus: "+entry.Id);
      Check(row.ActualWidth>400&&row.ActualHeight>=64,"Guide row is unarranged or too narrow: "+entry.Id);
      Check(OnexPageNavigationRows().SelectMany(x=>x).Any(x=>ReferenceEquals(x.Control,row)),"Guide row missing from content navigation: "+entry.Id);
      var line=(Grid)row.Content;var copy=(StackPanel)line.Children[1];
      Check(copy.Children.OfType<TextBlock>().All(t=>t.TextWrapping==TextWrapping.Wrap&&!t.IsTextTrimmed&&t.ActualHeight>0),"Guide text clips/trims: "+entry.Id);
      foreach(var image in ((StackPanel)line.Children[0]).Children.OfType<Border>().Select(b=>b.Child).OfType<Image>())
      {var source=image.Source as BitmapImage;Check(source!=null&&source.PixelWidth>0&&source.PixelHeight>0&&image.ActualWidth==26&&image.Stretch==Stretch.Uniform,"Guide icon failed to load or clips: "+entry.Id);}
     }
     var first=_onexNavigationGuideRows["move"];var next=OnexPageNavigationTarget(OnexPageNavigationRows(),first,2);
     Check(ReferenceEquals(next,_onexNavigationGuideRows["tabs"]),"Down does not reach the next guide row");
     var last=_onexNavigationGuideRows["keyboard"];var up=OnexPageNavigationTarget(OnexPageNavigationRows(),last,-2);
     Check(ReferenceEquals(up,_onexNavigationGuideRows["desktop"]),"Up does not reach the previous guide row");
     Check(_onexNavigationGuide.Children.Count==11&&_onexNavigationGuide.Children.All(x=>x is ContentControl),"Guide rows gained a nested scroll host");
     var guideBounds=OnexNavigationGuideCard.TransformToVisual(SystemScrollViewer.Content as UIElement).TransformBounds(new Windows.Foundation.Rect(0,0,OnexNavigationGuideCard.ActualWidth,OnexNavigationGuideCard.ActualHeight));
     Check(ProTipsExpandButton.Focus(FocusState.Keyboard),"Guide header cannot regain focus");
     SystemScrollViewer.ChangeView(null,guideBounds.Y,null,true);await Task.Delay(180);
     await SaveOnexImage("onex-guide365-"+(name==OnexPalette.Name?"dark":"light")+"-full.png",OnexNavigationGuideCard,80);
     await SaveOnexImage("onex-guide365-"+(name==OnexPalette.Name?"dark":"light")+"-top.png",SystemScrollViewer,80);
     SystemScrollViewer.ChangeView(null,guideBounds.Bottom-SystemScrollViewer.ViewportHeight,null,true);await Task.Delay(180);
     await SaveOnexImage("onex-guide365-"+(name==OnexPalette.Name?"dark":"light")+"-bottom.png",SystemScrollViewer,80);
    }
    int commits=0;fixture=new StackPanel{Spacing=10,Margin=new Thickness(0,12,0,0)};
    var before=new Button{Name="OnexGuideTestBefore",Content="QA before"};var slider=new Slider{Name="OnexGuideTestSlider",Minimum=0,Maximum=100,Value=40,IsTabStop=true};var after=new Button{Name="OnexGuideTestAfter",Content="QA after"};
    fixture.Children.Add(before);fixture.Children.Add(slider);fixture.Children.Add(after);ProTipsContent.Children.Add(fixture);OnexInputGuard.ConfigurePercentage(slider);OnexInputGuard.SetCommit(slider,()=>commits++);
    async Task FocusSlider(){slider.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(100);Check(slider.Focus(FocusState.Keyboard),"Local slider fixture cannot focus");await Task.Delay(50);}
    await FocusSlider();await Input("a");Check(OnexInputGuard.IsPreviewing(slider),"A-equivalent did not enter slider edit");await Input("right");Check(slider.Value==45&&commits==0,"Preview wrote before confirmation or percentage step differs from5");await Input("a");Check(commits==1&&!OnexInputGuard.IsPreviewing(slider)&&ReferenceEquals(FocusManager.GetFocusedElement(),slider),"A-equivalent did not apply exactly once with focus retained");
    await Input("a");await Input("right");await Input("b");Check(slider.Value==45&&commits==1&&!OnexInputGuard.IsPreviewing(slider),"B-equivalent failed to cancel without committing");
    await Input("a");await Input("right");await Input("down");Check(slider.Value==50&&commits==2&&ReferenceEquals(FocusManager.GetFocusedElement(),after),"Down failed to apply and move to next control");
    await FocusSlider();await Input("a");await Input("left");await Input("up");Check(slider.Value==45&&commits==3&&ReferenceEquals(FocusManager.GetFocusedElement(),before),"Up failed to apply and move to previous control");
    report["localSliderCommitCallbacks"]=JsonValue.CreateNumberValue(commits);report["injectedKeyboardSteps"]=JsonValue.CreateNumberValue(inputs);report["sliderFixtureHasNoHardwareWriter"]=JsonValue.CreateBooleanValue(true);
    // This exact private key was created by our failed362 fixture; never remove owner keys.
    values.Remove("OnexThemeQa362");report["inactiveQa362Removed"]=JsonValue.CreateBooleanValue(!values.ContainsKey("OnexThemeQa362"));
    Check((values.TryGetValue("WidgetTheme",out var current)?current as string:null)==stored&&ReadOnexTabValue(OnexTabsKey)==tabs,"Guide inspection wrote owner preferences");
    report["checks"]=JsonValue.CreateNumberValue(checks);report["actualOwnUwpRendering"]=JsonValue.CreateBooleanValue(true);report["nativeToggleProvider"]=JsonValue.CreateBooleanValue(true);report["actualContentFocusGraph"]=JsonValue.CreateBooleanValue(true);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);report["hardwareWritesRequested"]=JsonValue.CreateNumberValue(0);
   }
   finally
   {
    if(fixture!=null)ProTipsContent.Children.Remove(fixture);await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-guide365-input.json",CreationCollisionOption.ReplaceExisting),"{\"finished\":true}");
    isThemeInitialized=false;if(ProTipsExpandButton.IsChecked!=expanded)toggle.Toggle();
    string fresh=values.TryGetValue("WidgetTheme",out var current)?current as string:OnexPalette.Name;ApplyTheme(fresh??OnexPalette.Name);isThemeInitialized=initialized;
    if(original!=null&&original.Visibility==Visibility.Visible)original.IsChecked=true;
   }
  }
 }
}
