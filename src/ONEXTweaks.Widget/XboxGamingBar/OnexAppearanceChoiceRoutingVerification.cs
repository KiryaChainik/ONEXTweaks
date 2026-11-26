using System;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexNativeChoiceFocusAsync(JsonObject report)
  {
   var originalPage=GetVisibleNavigationItems().FirstOrDefault(tab=>tab.IsChecked==true);
   ComboBox choice=null;
   void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
   async Task Wait(Func<bool> ready,string message)
   {
    for(int attempt=0;attempt<80;attempt++){if(ready())return;await Task.Delay(25);}
    throw new TimeoutException(message);
   }
   try
   {
    Check(_onexDetailsDialog==null&&!OnexSourceEditorsPending,"Preserve pending owner edits");
    LegionNavItem.IsChecked=true;
    await ReadOnexControllerAsync();
    choice=_onexControllerTransport;
    Check(choice!=null&&choice.IsEnabled&&choice.Items.Count==2,"Controller Mode is unavailable");
    choice.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});
    await Task.Delay(80);
    int selected=choice.SelectedIndex;
    Check(selected==0||selected==1,"Controller Mode has no active selection");
    Check(choice.Focus(FocusState.Keyboard),"Could not focus Controller Mode");
    choice.IsDropDownOpen=true;
    ComboBoxItem Row(int index)=>choice.ContainerFromIndex(index) as ComboBoxItem??choice.Items[index] as ComboBoxItem;
    await Wait(()=>Row(selected)?.IsLoaded==true&&Row(1-selected)?.IsLoaded==true,"Popup rows did not load");
    var first=Row(selected);
    var second=Row(1-selected);
    Check(first.Focus(FocusState.Keyboard),"Could not focus the first Controller Mode row");
    await Wait(()=>ReferenceEquals(_onexChoiceCursorRow,first),"The first row did not get a focus outline");
    var firstBorder=FindOnexChoiceCursorBorder(first);
    Check(firstBorder?.Visibility==Visibility.Visible,"The first outline is hidden");
    Check(second.Focus(FocusState.Keyboard),"Could not move focus to the other Controller Mode row");
    await Wait(()=>ReferenceEquals(_onexChoiceCursorRow,second),"The outline did not follow focus");
    var secondBorder=FindOnexChoiceCursorBorder(second);
    Check(firstBorder.Visibility==Visibility.Collapsed,"The previous row kept its outline");
    Check(secondBorder?.Visibility==Visibility.Visible&&secondBorder.ActualWidth>20&&secondBorder.ActualHeight>10&&
          secondBorder.BorderThickness.Left==2&&secondBorder.BorderBrush is Windows.UI.Xaml.Media.SolidColorBrush brush&&brush.Color.A>0,"The new outline has no visible stroke");
    Check(choice.SelectedIndex==selected&&first.IsSelected&&!second.IsSelected,"Moving focus changed the active controller mode");
    var last=Row(choice.Items.Count-1);
    Check(last.Focus(FocusState.Keyboard),"Could not focus the bottom Controller Mode row");
    await Wait(()=>ReferenceEquals(_onexChoiceCursorRow,last),"The bottom row did not get the focus outline");
    var bottomBorder=FindOnexChoiceCursorBorder(last);
    FrameworkElement viewport=null;
    for(var parent=Windows.UI.Xaml.Media.VisualTreeHelper.GetParent(last);parent!=null;parent=Windows.UI.Xaml.Media.VisualTreeHelper.GetParent(parent))
    {
     if(parent is FrameworkElement element&&(parent.GetType().Name=="ScrollContentPresenter"||parent is ScrollViewer))
     {
      viewport=element;
      break;
     }
    }
    Check(viewport!=null,"The native popup viewport was not found");
    await Wait(()=>viewport.ActualHeight>0&&bottomBorder.ActualHeight>10,"The bottom outline did not lay out");
    var frame=new Windows.Foundation.Rect(0,0,bottomBorder.ActualWidth,bottomBorder.ActualHeight);
    double rowClearance=last.ActualHeight-bottomBorder.TransformToVisual(last).TransformBounds(frame).Bottom;
    double viewportClearance=viewport.ActualHeight-bottomBorder.TransformToVisual(viewport).TransformBounds(frame).Bottom;
    Check(rowClearance>=2&&viewportClearance>=1,"Bottom focus stroke touches or exceeds the popup viewport");
    choice.IsDropDownOpen=false;
    await Wait(()=>bottomBorder.Visibility==Visibility.Collapsed&&_onexOpenChoice==null,"Closing the popup kept the outline or input owner");
    report["nativeChoiceFocusPassed"]=JsonValue.CreateBooleanValue(true);
    report["nativeChoiceName"]=JsonValue.CreateStringValue(choice.Name);
    report["nativeChoiceFocusMovedWithoutSelection"]=JsonValue.CreateBooleanValue(true);
    report["nativeChoiceOutlineStroke"]=JsonValue.CreateNumberValue(secondBorder.BorderThickness.Left);
    report["nativeChoiceBottomRowClearance"]=JsonValue.CreateNumberValue(rowClearance);
    report["nativeChoiceViewportClearance"]=JsonValue.CreateNumberValue(viewportClearance);
   }
   finally
   {
    if(choice!=null)choice.IsDropDownOpen=false;
    if(originalPage!=null)originalPage.IsChecked=true;
   }
  }
  private async Task VerifyOnexAppearanceChoiceRoutingAsync(JsonObject report)
  {
   int checks=0,steps=0;string run=Guid.NewGuid().ToString("N");var folder=ApplicationData.Current.LocalFolder;var values=ApplicationData.Current.LocalSettings.Values;
   var originalPage=GetVisibleNavigationItems().FirstOrDefault(tab=>tab.IsChecked==true);bool appearance=ThemeExpandButton.IsChecked==true,themeReady=isThemeInitialized,fontReady=isAppFontInitialized;
   int languageIndex=_onexLanguage.SelectedIndex;string themeBefore=values.TryGetValue("WidgetTheme",out var oldTheme)?oldTheme as string:OnexPalette.Name;
   string fontBefore=values.TryGetValue(AppFontSettingKey,out var oldFont)?oldFont as string:DefaultAppFont;string languageBefore=values.TryGetValue(OnexStrings.Setting,out var oldLanguage)?oldLanguage as string:"en";
   var captured=new HashSet<string>();var cursorImages=new JsonArray();var cases=new JsonArray();int guardBefore=_onexChoicePageGuardsSkipped;
   void Check(bool ok,string why){checks++;report["appearanceInputChecks"]=JsonValue.CreateNumberValue(checks);if(!ok)throw new InvalidOperationException(why);}
   async Task Wait(Func<bool> ready,string why){for(int n=0;n<120;n++){if(ready())return;await Task.Delay(30);}throw new TimeoutException(why);}
   async Task CursorVisual(ComboBoxItem row,ComboBox choice)
   {
    if(!captured.Add(choice.Name))return;await Task.Delay(20);
    var marker=FindOnexChoiceCursorBorder(row);
    Check(marker!=null&&marker.Visibility==Visibility.Visible&&marker.Opacity>0,"Explicit native XAML cursor border not visible");
    Check(marker.ActualWidth>20&&marker.ActualHeight>10&&marker.BorderThickness.Left==2,"Native cursor border has no usable layout bounds");
    Check(marker.BorderBrush is Windows.UI.Xaml.Media.SolidColorBrush ink&&ink.Color.A>0,"Native cursor border ink is transparent");
    cursorImages.Add(new JsonObject{{"picker",JsonValue.CreateStringValue(choice.Name)},{"width",JsonValue.CreateNumberValue(marker.ActualWidth)},{"height",JsonValue.CreateNumberValue(marker.ActualHeight)},{"stroke",JsonValue.CreateNumberValue(marker.BorderThickness.Left)},{"visible",JsonValue.CreateBooleanValue(marker.Visibility==Visibility.Visible)}});
   }
   async Task Input(string key,Func<bool> expected,string why)
   {
    steps++;var choice=_onexOpenChoice??FocusManager.GetFocusedElement() as ComboBox;
    Check(choice!=null,"Real picker not focused");
    Windows.System.VirtualKey code=key=="a"?Windows.System.VirtualKey.Space:key=="b"?Windows.System.VirtualKey.Escape:key=="down"?Windows.System.VirtualKey.Down:key=="up"?Windows.System.VirtualKey.Up:key=="home"?Windows.System.VirtualKey.Home:Windows.System.VirtualKey.Enter;
    if(!choice.IsDropDownOpen&&key=="a")choice.IsDropDownOpen=true;
    else Check(HandleOnexChoiceNavigation(code,false),why+"; production dispatcher router rejected key");
    await Task.Delay(80);await Wait(expected,why);
    if(choice.IsDropDownOpen&&(key=="down"||key=="up"||key=="home"))
     {
     var row=choice.ContainerFromIndex(_onexChoiceCursor) as ComboBoxItem;
     Check(ReferenceEquals(FocusManager.GetFocusedElement(),row),why+"; real native row did not receive focus");
     Check(row!=null&&ReferenceEquals(_onexChoiceCursorRow,row)&&_onexChoiceCursorBorder?.Visibility==Visibility.Visible,why+"; explicit visible cursor marker not active");await CursorVisual(row,choice);
     }
   }
   async Task Focus(ComboBox choice)
   {choice.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(80);UpdateLayout();Check(choice.Focus(FocusState.Keyboard),"Cannot focus actual Appearance picker");await Wait(()=>ReferenceEquals(FocusManager.GetFocusedElement(),choice),"Actual picker focus did not settle");}
   try
   {
    Check(_onexDetailsDialog==null&&!OnexSourceEditorsPending,"Protect owner pending edits");await _onexUiFontApplyTask;
    isThemeInitialized=false;isAppFontInitialized=false;SystemNavItem.IsChecked=true;ThemeExpandButton.IsChecked=true;await Task.Delay(100);ApplyOnexPageFocus();
    foreach(var item in new[]{new{Role="theme",Choice=ThemeComboBox},new{Role="language",Choice=_onexLanguage},new{Role="font",Choice=AppFontComboBox}})
    {
     var choice=item.Choice;Check(choice.Items.Count>=2,"Actual picker has insufficient choices");choice.IsDropDownOpen=false;choice.SelectedIndex=0;await Focus(choice);
     await Input("a",()=>choice.IsDropDownOpen,item.Role+" gamepad-mapped A did not open popup");
     Check(ReferenceEquals(_onexOpenChoice,choice),item.Role+" popup input owner not tracked");
     await Input("down",()=>choice.IsDropDownOpen,item.Role+" Down escaped popup");
     await Input("a",()=>!choice.IsDropDownOpen&&choice.SelectedIndex==1,item.Role+" Down+A did not commit next real item");
     Check(_onexOpenChoice==null&&_onexChoiceOriginal==null&&_onexChoiceCursorRow==null,item.Role+" closed popup kept input ownership or retained selection");
     await Focus(choice);await Input("a",()=>choice.IsDropDownOpen,item.Role+" could not reopen");
     await Input("up",()=>choice.IsDropDownOpen,item.Role+" Up escaped popup");
     await Input("b",()=>!choice.IsDropDownOpen&&choice.SelectedIndex==1,item.Role+" B did not cancel without committing");
     int destination=Math.Min(8,choice.Items.Count-1);choice.SelectedIndex=0;await Focus(choice);await Input("a",()=>choice.IsDropDownOpen,item.Role+" popup not open for repeated directions");
     for(int n=0;n<destination;n++)await Input("down",()=>choice.IsDropDownOpen,item.Role+" repeated Down escaped list");
     await Input("enter",()=>!choice.IsDropDownOpen&&choice.SelectedIndex==destination,item.Role+" repeated Down/Enter selection incorrect");
     await Focus(choice);await Input("a",()=>choice.IsDropDownOpen,item.Role+" popup not open for upper boundary");
     await Input("home",()=>choice.IsDropDownOpen,item.Role+" Home escaped list");await Input("up",()=>choice.IsDropDownOpen,item.Role+" upper boundary escaped list");
     await Input("a",()=>!choice.IsDropDownOpen&&choice.SelectedIndex==0,item.Role+" first-item boundary commit incorrect");
     Check(!OnexChoiceOwnsInput(Windows.System.VirtualKey.Down),item.Role+" closed popup retained page input");
     await Focus(choice);choice.IsDropDownOpen=true;await Task.Delay(100);int nativeCommits=_onexChoiceNativeCommits;choice.SelectedIndex=1;choice.IsDropDownOpen=false;
     if(item.Role!="language")await Wait(()=>_onexChoiceNativeCommits>nativeCommits,item.Role+" native selection close did not preserve deferred apply path");
     Check(choice.SelectedIndex==1,item.Role+" native selection projection lost on close");
     cases.Add(new JsonObject{{"role",JsonValue.CreateStringValue(item.Role)},{"realChoiceCount",JsonValue.CreateNumberValue(choice.Items.Count)},{"downStepsBeforeCommit",JsonValue.CreateNumberValue(destination)},{"openMoveCommitCancelReopenAndClosedPageNavigation",JsonValue.CreateBooleanValue(true)}});
    }
    Check((values.TryGetValue("WidgetTheme",out var theme)?theme as string:OnexPalette.Name)==themeBefore,"Owner theme changed during input test");
    Check((values.TryGetValue(AppFontSettingKey,out var face)?face as string:DefaultAppFont)==fontBefore,"Owner font changed during input test");
    Check((values.TryGetValue(OnexStrings.Setting,out var language)?language as string:"en")==languageBefore,"Owner language changed during input test");
    report["appearanceInputCases"]=cases;report["explicitCursorNativeVisualTree"]=cursorImages;report["cursorPixelsCaptured"]=JsonValue.CreateBooleanValue(false);report["compositorFocusVisualCaptured"]=JsonValue.CreateBooleanValue(false);report["actualWindowsKeyInjectionSteps"]=JsonValue.CreateNumberValue(0);report["programmaticProductionKeyRouterSteps"]=JsonValue.CreateNumberValue(steps);report["realNativeItemFocusVerified"]=JsonValue.CreateBooleanValue(true);report["nativeSelectionClosePathProjected"]=JsonValue.CreateBooleanValue(true);report["physicalPointerTested"]=JsonValue.CreateBooleanValue(false);report["openPopupPageGuardsYielded"]=JsonValue.CreateNumberValue(_onexChoicePageGuardsSkipped-guardBefore);
    report["selectedIndexUsedOnlyForReadOnlyFixtureSetup"]=JsonValue.CreateBooleanValue(true);report["movementAndCommitUsedWindowsSendInput"]=JsonValue.CreateBooleanValue(false);report["verificationKind"]=JsonValue.CreateStringValue("Actual UWP ComboBox popup, focus API and production key router invoked programmatically; no OS key injection, no physical gamepad");
    report["themeAndFontSaveHandlersSuppressedForReadOnlyTest"]=JsonValue.CreateBooleanValue(true);report["ownerPreferenceWrites"]=JsonValue.CreateNumberValue(0);report["physicalOwnerGamepadTested"]=JsonValue.CreateBooleanValue(false);
   }
   finally
   {
    isThemeInitialized=false;isAppFontInitialized=false;ThemeComboBox.IsDropDownOpen=false;AppFontComboBox.IsDropDownOpen=false;_onexLanguage.IsDropDownOpen=false;
    string currentTheme=values.TryGetValue("WidgetTheme",out var theme)?theme as string:OnexPalette.Name;string currentFont=values.TryGetValue(AppFontSettingKey,out var font)?font as string:DefaultAppFont;
    ThemeComboBox.SelectedItem=ThemeComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item=>item.Content as string==currentTheme);
    AppFontComboBox.SelectedItem=AppFontComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item=>item.Tag as string==currentFont);_onexLanguage.SelectedIndex=languageIndex;
    isThemeInitialized=themeReady;isAppFontInitialized=fontReady;ThemeExpandButton.IsChecked=appearance;if(originalPage!=null)originalPage.IsChecked=true;

   }
  }
 }
}
