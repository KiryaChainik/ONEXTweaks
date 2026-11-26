using System;
using System.Linq;
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
  private async Task VerifyOnexAppearanceChoiceInputAsync(JsonObject report)
  {
   int checks=0,steps=0;string run=Guid.NewGuid().ToString("N");var folder=ApplicationData.Current.LocalFolder;var values=ApplicationData.Current.LocalSettings.Values;
   var originalPage=GetVisibleNavigationItems().FirstOrDefault(tab=>tab.IsChecked==true);bool appearance=ThemeExpandButton.IsChecked==true,themeReady=isThemeInitialized,fontReady=isAppFontInitialized;
   int languageIndex=_onexLanguage.SelectedIndex;string themeBefore=values.TryGetValue("WidgetTheme",out var oldTheme)?oldTheme as string:OnexPalette.Name;
   string fontBefore=values.TryGetValue(AppFontSettingKey,out var oldFont)?oldFont as string:DefaultAppFont;string languageBefore=values.TryGetValue(OnexStrings.Setting,out var oldLanguage)?oldLanguage as string:"en";
   var cases=new JsonArray();int guardBefore=_onexChoicePageGuardsSkipped;
   void Check(bool ok,string why){checks++;report["appearanceInputChecks"]=JsonValue.CreateNumberValue(checks);if(!ok)throw new InvalidOperationException(why);}
   async Task Wait(Func<bool> ready,string why){for(int n=0;n<120;n++){if(ready())return;await Task.Delay(30);}throw new TimeoutException(why);}
   async Task Input(string key,Func<bool> expected,string why)
   {
    string id=run+":"+(++steps);var request=new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"key",JsonValue.CreateStringValue(key)},{"utc",JsonValue.CreateStringValue(DateTimeOffset.UtcNow.ToString("o"))}};
    await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-appearance-input480.json",CreationCollisionOption.ReplaceExisting),request.Stringify());
    for(int n=0;n<100;n++)
    {
     await Task.Delay(60);
     try{if(await FileIO.ReadTextAsync(await folder.GetFileAsync("onex-appearance-input480-ack.txt"))==id&&expected())return;}catch(System.IO.FileNotFoundException){}
    }
    throw new InvalidOperationException(why+"; step "+steps+" key "+key);
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
     Check(_onexOpenChoice==null,item.Role+" closed popup kept input ownership");
     await Focus(choice);await Input("a",()=>choice.IsDropDownOpen,item.Role+" could not reopen");
     await Input("up",()=>choice.IsDropDownOpen,item.Role+" Up escaped popup");
     await Input("b",()=>!choice.IsDropDownOpen&&choice.SelectedIndex==1,item.Role+" B did not cancel without committing");
     int destination=Math.Min(8,choice.Items.Count-1);choice.SelectedIndex=0;await Focus(choice);await Input("a",()=>choice.IsDropDownOpen,item.Role+" popup not open for repeated directions");
     for(int n=0;n<destination;n++)await Input("down",()=>choice.IsDropDownOpen,item.Role+" repeated Down escaped list");
     await Input("enter",()=>!choice.IsDropDownOpen&&choice.SelectedIndex==destination,item.Role+" repeated Down/Enter selection incorrect");
     await Focus(choice);await Input("a",()=>choice.IsDropDownOpen,item.Role+" popup not open for upper boundary");
     await Input("home",()=>choice.IsDropDownOpen,item.Role+" Home escaped list");await Input("up",()=>choice.IsDropDownOpen,item.Role+" upper boundary escaped list");
     await Input("a",()=>!choice.IsDropDownOpen&&choice.SelectedIndex==0,item.Role+" first-item boundary commit incorrect");
     await Focus(choice);await Input("down",()=>!choice.IsDropDownOpen&&choice.SelectedIndex==0&&!ReferenceEquals(FocusManager.GetFocusedElement(),choice),item.Role+" closed picker blocked page navigation or changed value");
     cases.Add(new JsonObject{{"role",JsonValue.CreateStringValue(item.Role)},{"realChoiceCount",JsonValue.CreateNumberValue(choice.Items.Count)},{"downStepsBeforeCommit",JsonValue.CreateNumberValue(destination)},{"openMoveCommitCancelReopenAndClosedPageNavigation",JsonValue.CreateBooleanValue(true)}});
    }
    Check((values.TryGetValue("WidgetTheme",out var theme)?theme as string:OnexPalette.Name)==themeBefore,"Owner theme changed during input test");
    Check((values.TryGetValue(AppFontSettingKey,out var face)?face as string:DefaultAppFont)==fontBefore,"Owner font changed during input test");
    Check((values.TryGetValue(OnexStrings.Setting,out var language)?language as string:"en")==languageBefore,"Owner language changed during input test");
    report["appearanceInputCases"]=cases;report["actualWindowsKeyInjectionSteps"]=JsonValue.CreateNumberValue(steps);report["openPopupPageGuardsYielded"]=JsonValue.CreateNumberValue(_onexChoicePageGuardsSkipped-guardBefore);
    report["selectedIndexUsedOnlyForReadOnlyFixtureSetup"]=JsonValue.CreateBooleanValue(true);report["movementAndCommitUsedWindowsSendInput"]=JsonValue.CreateBooleanValue(true);
    report["themeAndFontSaveHandlersSuppressedForReadOnlyTest"]=JsonValue.CreateBooleanValue(true);report["ownerPreferenceWrites"]=JsonValue.CreateNumberValue(0);report["physicalOwnerGamepadTested"]=JsonValue.CreateBooleanValue(false);
   }
   finally
   {
    isThemeInitialized=false;isAppFontInitialized=false;ThemeComboBox.IsDropDownOpen=false;AppFontComboBox.IsDropDownOpen=false;_onexLanguage.IsDropDownOpen=false;
    string currentTheme=values.TryGetValue("WidgetTheme",out var theme)?theme as string:OnexPalette.Name;string currentFont=values.TryGetValue(AppFontSettingKey,out var font)?font as string:DefaultAppFont;
    ThemeComboBox.SelectedItem=ThemeComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item=>item.Content as string==currentTheme);
    AppFontComboBox.SelectedItem=AppFontComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item=>item.Tag as string==currentFont);_onexLanguage.SelectedIndex=languageIndex;
    isThemeInitialized=themeReady;isAppFontInitialized=fontReady;ThemeExpandButton.IsChecked=appearance;if(originalPage!=null)originalPage.IsChecked=true;
    await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-appearance-input480.json",CreationCollisionOption.ReplaceExisting),new JsonObject{{"finished",JsonValue.CreateBooleanValue(true)},{"run",JsonValue.CreateStringValue(run)},{"utc",JsonValue.CreateStringValue(DateTimeOffset.UtcNow.ToString("o"))}}.Stringify());
   }
  }
 }
}
