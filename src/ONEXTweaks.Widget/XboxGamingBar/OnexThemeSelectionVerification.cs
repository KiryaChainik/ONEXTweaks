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
  private async Task VerifyOnexThemeSelectionAsync(JsonObject report)
  {
   int checks=0,selections=0;void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
   var values=ApplicationData.Current.LocalSettings.Values;string saved=values.TryGetValue("WidgetTheme",out var old)?old as string:null;
   bool initialized=isThemeInitialized;var original=GetVisibleNavigationItems().FirstOrDefault(x=>x.IsChecked==true);
   bool appearance=ThemeExpandButton.IsChecked==true;var themeVisibility=ThemeContent.Visibility;double scroll=SystemScrollViewer.VerticalOffset;
   var body=(StackPanel)SystemScrollViewer.Content;var fixture=new ComboBox{Name="OnexSelection388Fixture",Header="Selection state verification",Width=360,MaxDropDownHeight=280,HorizontalAlignment=HorizontalAlignment.Left};
   foreach(string text in new[]{"First","Second","Third","Fourth"})fixture.Items.Add(new ComboBoxItem{Content=text});
   var card=OnexSettingsCard(fixture);int realRowsChecked=0;
   async Task Inspect(ComboBox picker,bool requireAll)
   {
    picker.IsDropDownOpen=true;await Task.Delay(70);UpdateLayout();
    var selected=picker.SelectedItem as ComboBoxItem;selected?.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(25);
    Check(picker.Items.OfType<ComboBoxItem>().Count(x=>x.IsSelected)==1,"ComboBox actual selection is not exclusive");
    int marked=0,realized=0;
    foreach(var item in picker.Items.OfType<ComboBoxItem>())
    {
     item.ApplyTemplate();if(VisualTreeHelper.GetChildrenCount(item)==0){Check(!requireAll,"Owned fixture item not realized");continue;}
     var root=VisualTreeHelper.GetChild(item,0) as FrameworkElement;
     Check(root?.Name=="OnexChoiceRoot","Only inspect the known owned item template");
     var marker=root.FindName("OwnSelected") as Border;var tick=root.FindName("OwnSelectedCheck") as FontIcon;
     Check(marker!=null&&tick!=null,"Selection indicator/checkmark missing");
     // A pointer/native common-state transition must not leave a historical mark.
     Windows.UI.Xaml.VisualStateManager.GoToState(item,"PointerOver",false);Windows.UI.Xaml.VisualStateManager.GoToState(item,"Normal",false);
     await Task.Delay(2);
     Check((marker.Visibility==Visibility.Visible)==item.IsSelected,"Stale selection stripe survived the actual selection change");
     Check((tick.Visibility==Visibility.Visible)==item.IsSelected,"Stale selection checkmark survived the actual selection change");
     if(marker.Visibility==Visibility.Visible)marked++;realized++;
    }
    Check(realized>0,"No actual popup row rendered");Check(marked==1,"Popup does not show exactly one selected row");
    if(ReferenceEquals(picker,ThemeComboBox))realRowsChecked+=realized;
   }
   try
   {
    Check(_onexDetailsDialog==null,"Protect owner dialog");isThemeInitialized=false;SystemNavItem.IsChecked=true;
    ThemeExpandButton.IsChecked=true;ThemeContent.Visibility=Visibility.Visible;body.Children.Insert(0,card);ConfigureOnexFocus(fixture);fixture.SelectedIndex=0;
    foreach(string theme in new[]{OnexPalette.Name,"Dracula","Windows 11 Light","OLED"})
    {
     ApplyTheme(theme);ApplyOnexSettingsDesign();fixture.ItemContainerStyle=OnexOwnedComboItemStyle();ConfigureOnexFocus(fixture);UpdateLayout();card.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(60);
     foreach(int index in new[]{0,1,3,2,0,3,1,2,0})
     {
      fixture.SelectedIndex=index;await Inspect(fixture,true);selections++;
      fixture.IsDropDownOpen=false;await Task.Delay(10);
     }
     fixture.IsDropDownOpen=true;await Task.Delay(60);
     var selected=fixture.SelectedItem as ComboBoxItem;if(selected!=null)await SaveOnexImage("onex-selection388-"+theme.Replace(' ','-')+".png",selected,30);
     fixture.IsDropDownOpen=false;
    }
    foreach(int index in new[]{0,1,2,0,2,1})
    {ThemeComboBox.SelectedIndex=index;ThemeComboBox.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Inspect(ThemeComboBox,false);selections++;ThemeComboBox.IsDropDownOpen=false;}
    Check((values.TryGetValue("WidgetTheme",out var fresh)?fresh as string:null)==saved,"Selection check wrote owner theme preference");
    report["checks"]=JsonValue.CreateNumberValue(checks);report["actualSelectionChanges"]=JsonValue.CreateNumberValue(selections);report["actualThemePickerRowsInspected"]=JsonValue.CreateNumberValue(realRowsChecked);
    report["realOwnUwpComboBoxes"]=JsonValue.CreateBooleanValue(true);report["hoverStatesProgrammatic"]=JsonValue.CreateBooleanValue(true);report["ownerPhysicalInputTested"]=JsonValue.CreateBooleanValue(false);report["hardwareWritesRequested"]=JsonValue.CreateNumberValue(0);
   }
   finally
   {
    fixture.IsDropDownOpen=false;ThemeComboBox.IsDropDownOpen=false;body.Children.Remove(card);
    string current=values.TryGetValue("WidgetTheme",out var fresh)?fresh as string:OnexPalette.Name;
    ThemeComboBox.SelectedItem=ThemeComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(x=>x.Content as string==NormalizeOnexThemeName(current));
    ThemeExpandButton.IsChecked=appearance;ThemeContent.Visibility=themeVisibility;isThemeInitialized=false;ApplyTheme(current);isThemeInitialized=initialized;
    if(original!=null&&original.Visibility==Visibility.Visible)original.IsChecked=true;SystemScrollViewer.ChangeView(null,scroll,null,true);
   }
  }
 }
}
