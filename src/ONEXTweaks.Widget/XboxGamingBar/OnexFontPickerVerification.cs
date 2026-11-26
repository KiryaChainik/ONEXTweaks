using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexFontPickerAsync(JsonObject report)
  {
   int checks=0,transitions=0;void Check(bool ok,string reason){checks++;if(!ok)throw new InvalidOperationException(reason);}
   var values=ApplicationData.Current.LocalSettings.Values;
   bool had=values.TryGetValue(AppFontSettingKey,out var originalValue);
   string saved=originalValue as string??DefaultAppFont,lastOwn=saved;
   bool wasReady=isAppFontInitialized;var originalPage=GetVisibleNavigationItems().FirstOrDefault(x=>x.IsChecked==true);
   var appearance=ThemeContent.Visibility;
   try
   {
    Check(_onexDetailsDialog==null&&!OnexSourceEditorsPending,"Protect pending owner edits");
    SystemNavItem.IsChecked=true;ThemeContent.Visibility=Visibility.Visible;
    AppFontComboBox.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});UpdateLayout();
    isAppFontInitialized=true;
    foreach(var face in OnexUiTypefaces)
    {
     string fresh=values.TryGetValue(AppFontSettingKey,out var current)?current as string:DefaultAppFont;
     Check(fresh==lastOwn,"Concurrent owner font edit: stop without overwriting it");
     var choice=AppFontComboBox.Items.OfType<ComboBoxItem>().Single(x=>x.Tag as string==face.Name);
     if(ReferenceEquals(AppFontComboBox.SelectedItem,choice))continue;
     var progress=await ApplicationData.Current.LocalFolder.CreateFileAsync("onex-font-picker-progress.json",CreationCollisionOption.ReplaceExisting);
     await FileIO.WriteTextAsync(progress,new JsonObject{{"phase",JsonValue.CreateStringValue("open")},{"font",JsonValue.CreateStringValue(face.Name)},{"completed",JsonValue.CreateNumberValue(transitions)}}.Stringify());
     AppFontComboBox.IsDropDownOpen=true;await Task.Delay(80);
     Check(AppFontComboBox.IsDropDownOpen,"Actual font popup opened");
     string before=FontFamily.Source;
     AppFontComboBox.SelectedItem=choice;
     Check(FontFamily.Source==before,"Font application deferred out of open picker");
     AppFontComboBox.IsDropDownOpen=false;
     await _onexUiFontApplyTask;await Task.Delay(180);UpdateLayout();
     Check(_onexUiFontLastError==null,"Font application failed");
     Check(FontFamily.Source==ResolveOnexUiFont(face.Name).Source,"Font applied after picker close");
     Check(values.TryGetValue(AppFontSettingKey,out var persisted)&&persisted as string==face.Name,"Font selection persisted");
     lastOwn=face.Name;transitions++;
    }
    report["checks"]=JsonValue.CreateNumberValue(checks);report["popupFontTransitions"]=JsonValue.CreateNumberValue(transitions);
    report["actualPopupOpenSelectClose"]=JsonValue.CreateBooleanValue(true);
    report["programmaticUiInput"]=JsonValue.CreateBooleanValue(true);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);
    report["hardwareWritesRequested"]=JsonValue.CreateNumberValue(0);
   }
   finally
   {
    _onexUiFontRequestEpoch++;isAppFontInitialized=false;AppFontComboBox.IsDropDownOpen=false;
    string fresh=values.TryGetValue(AppFontSettingKey,out var now)?now as string:DefaultAppFont;
    if(fresh==lastOwn){if(had)values[AppFontSettingKey]=originalValue;else values.Remove(AppFontSettingKey);}
    string restore=values.TryGetValue(AppFontSettingKey,out var selected)?selected as string:DefaultAppFont;
    ApplyAppFont(restore??DefaultAppFont);
    AppFontComboBox.SelectedItem=AppFontComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(x=>x.Tag as string==restore);
    ThemeContent.Visibility=appearance;isAppFontInitialized=wasReady;
    if(originalPage!=null)originalPage.IsChecked=true;
    report["ownerFontRestoredOrConcurrentEditPreserved"]=JsonValue.CreateBooleanValue(true);
   }
  }
 }
}
