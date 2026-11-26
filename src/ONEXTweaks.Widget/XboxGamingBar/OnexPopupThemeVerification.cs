using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Input;
using System.Collections.Generic;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexPopupThemesAsync(JsonObject report)
  {
   int checks=0,popups=0,dialogs=0,menus=0;void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
   var values=ApplicationData.Current.LocalSettings.Values;string saved=values.TryGetValue("WidgetTheme",out var raw)?raw as string:null;
   bool initialized=isThemeInitialized;var tab=GetVisibleNavigationItems().FirstOrDefault(x=>x.IsChecked==true);
   Windows.UI.Xaml.Controls.Flyout flyout=null;ContentDialog dialog=null;OnexRemapWindow view=null;MenuFlyout menu=null;
   Color? Ink(Brush b)=>(b as SolidColorBrush)?.Color;
   void FocusStyle(Control control,string name)
   {Check(Ink(control.FocusVisualPrimaryBrush)==OnexFocusColour&&Ink(control.FocusVisualSecondaryBrush)==Colors.Transparent&&control.FocusVisualPrimaryThickness.Equals(OnexHasInlineFocus(control)&&_onexControlLayers.TryGetValue(control,out var scope)?new Thickness(0):new Thickness(2))&&control.FocusVisualSecondaryThickness.Equals(new Thickness(0))&&control.FocusVisualMargin.Equals(new Thickness(2)),"Popup focus differs: "+name);}
   try
   {
    Check(_onexDetailsDialog==null&&_onexControllerPending==null&&_kbmTuning.Count==0,"Protect owner dialog/pending edits");
    _releasePowerNav.IsChecked=true;isThemeInitialized=false;UpdateLayout();
    var anchor=_onexPerformanceProfilePicker.Choose;
    var editor=new StackPanel{Spacing=10};var label=new TextBlock{Text="Preview settings",Foreground=OnexSettingsBrush(170,181,193)};
    var choice=OnexSettingsChoice("Action");choice.Items.Add(new ComboBoxItem{Content="First action"});choice.Items.Add(new ComboBoxItem{Content="Second action"});choice.SelectedIndex=0;
    var preview=new Button{Content="Preview",MinHeight=38,Style=OnexSettingsButtonStyle(false)};var check=new CheckBox{Content="Option"};
    editor.Children.Add(label);editor.Children.Add(choice);editor.Children.Add(check);editor.Children.Add(preview);
    flyout=CreateOnexRemappingWindow(editor,OnexPalette.Orange);view=_onexRemapWindows.Single(x=>ReferenceEquals(x.Flyout,flyout));
    foreach(string name in OnexAllowedThemeNames)
    {
     ApplyTheme(name);
     Check(Ink(OnexStartupToggle.Background)==(OnexBrandTheme?OnexPalette.Cyan:_onexAppliedTheme.EffectiveAccent()),"Startup switch uses system accent: "+name);
     if(name==OnexPalette.Name||name=="Windows 11 Light")
     {SystemNavItem.IsChecked=true;await QueryOnexStartupAsync();UpdateLayout();OnexStartupToggle.Focus(FocusState.Keyboard);await SaveOnexImage("onex-visual382-startup-"+(name==OnexPalette.Name?"base":"light")+".png",OnexStartupCard,40);}
     _releasePowerNav.IsChecked=true;UpdateLayout();flyout.ShowAt(anchor);await Task.Delay(100);editor.UpdateLayout();
     var theme=_onexAppliedTheme;report["popupStyle"]=new JsonObject{{"theme",JsonValue.CreateStringValue(name)},{"buttonBackground",JsonValue.CreateStringValue(Ink(preview.Background)?.ToString()??"")},{"buttonForeground",JsonValue.CreateStringValue(Ink(preview.Foreground)?.ToString()??"")},{"buttonBorder",JsonValue.CreateStringValue(Ink(preview.BorderBrush)?.ToString()??"")}};Check(flyout.IsOpen&&editor.IsLoaded&&editor.ActualWidth>0,"Actual popup did not load: "+name);
     Check(Ink(preview.Background)==theme.ButtonBackground&&Ink(preview.Foreground)==theme.TextPrimary&&Ink(preview.BorderBrush)==theme.ButtonBorder,"Remapping popup keeps dark button colours: "+name);
     var style=flyout.FlyoutPresenterStyle;
     object Value(DependencyProperty property)=>style.Setters.OfType<Setter>().Last(x=>x.Property==property).Value;
     Check((ElementTheme)Value(FrameworkElement.RequestedThemeProperty)==(OnexLight(theme)?ElementTheme.Light:ElementTheme.Dark),"Popup forces dark mode: "+name);
     Check(Ink(Value(Control.BackgroundProperty) as Brush)==theme.CardBackground&&((CornerRadius)Value(Control.CornerRadiusProperty)).Equals(new CornerRadius(12)),"Popup surface/corners differ: "+name);
     foreach(var control in new Control[]{choice,check,preview}){Check(control.Focus(FocusState.Keyboard),"Actual popup focus failed: "+name);FocusStyle(control,name);}
     foreach(var item in choice.Items.OfType<ComboBoxItem>())FocusStyle(item,name);
     Check(choice.CornerRadius.Equals(new CornerRadius(8))&&preview.CornerRadius.Equals(new CornerRadius(8)),"Popup control corners differ: "+name);
     if(name==OnexPalette.Name||name=="Dracula"||name=="Windows 11 Light")
     {
      preview.Focus(FocusState.Keyboard);editor.UpdateLayout();await Task.Delay(100);report["popupCaptureState"]=new JsonObject{{"theme",JsonValue.CreateStringValue(name)},{"open",JsonValue.CreateBooleanValue(flyout.IsOpen)},{"loaded",JsonValue.CreateBooleanValue(preview.IsLoaded)},{"mapped",JsonValue.CreateBooleanValue(_onexControlLayers.TryGetValue(preview,out var previewLayer))}};Check(flyout.IsOpen&&preview.IsLoaded,"Popup lost content after focus");await SaveOnexOwnedFocusCropAsync(preview,"onex-visual382-popup-button-"+(name==OnexPalette.Name?"base":name=="Dracula"?"dark":"light")+".png");
      choice.Focus(FocusState.Keyboard);choice.IsDropDownOpen=true;await Task.Delay(80);await SaveOnexImage("onex-visual382-dropdown-"+(name==OnexPalette.Name?"base":name=="Dracula"?"dark":"light")+".png",(ComboBoxItem)choice.Items[0],40);choice.IsDropDownOpen=false;
     }
     flyout.Hide();await Task.Delay(30);popups++;
     if(name==OnexPalette.Name||name=="Dracula"||name=="Windows 11 Light")
     {
      var button=new Button{Content="Preview",Style=OnexSettingsButtonStyle(false),MinHeight=38};
      dialog=CreateOnexSettingsDialog("Preview","Read-only appearance check",new StackPanel{Children={button}});
      _onexDetailsDialog=dialog;var showing=dialog.ShowAsync();await Task.Delay(100);Check(button.IsLoaded&&button.Focus(FocusState.Keyboard),"Actual dialog did not focus: "+name);FocusStyle(button,name);
      Check(dialog.RequestedTheme==(OnexLight(theme)?ElementTheme.Light:ElementTheme.Dark)&&Ink(dialog.Background)==theme.CardBackground,"Dialog surface ignores theme: "+name);
      var inspectedFooters=new HashSet<Button>();button.Focus(FocusState.Keyboard);
      for(int move=0;move<10&&inspectedFooters.Count<2;move++)
      {
       Check(FocusManager.TryMoveFocus(FocusNavigationDirection.Next),"Dialog forward focus navigation failed");await Task.Delay(40);
       if(FocusManager.GetFocusedElement() is Button footer&&!ReferenceEquals(footer,button)&&(footer.Content as string=="Apply"||footer.Content as string=="Cancel")&&inspectedFooters.Add(footer))
       {ConfigureOnexFocus(footer);FocusStyle(footer,name);await SaveOnexImage("onex-visual382-footer-"+(name==OnexPalette.Name?"base":name=="Dracula"?"dark":"light")+"-"+inspectedFooters.Count+".png",footer,40);}
      }
      int footers=inspectedFooters.Count;
      Check(footers==2,"Native dialog footer buttons not inspected");dialog.Hide();await showing;if(ReferenceEquals(_onexDetailsDialog,dialog))_onexDetailsDialog=null;dialog=null;dialogs++;
      menu=CreateOnexMenu();var item=new MenuFlyoutItem{Text="Preview action"};menu.Items.Add(item);menu.ShowAt(anchor);await Task.Delay(80);Check(menu.IsOpen&&item.IsLoaded,"Actual menu did not open: "+name);FocusStyle(item,name);Check(Ink(item.Foreground)==theme.TextPrimary,"Menu text ignores theme: "+name);Check(item.Focus(FocusState.Keyboard),"Menu row focus failed");await SaveOnexImage("onex-visual382-menu-"+(name==OnexPalette.Name?"base":name=="Dracula"?"dark":"light")+".png",item,40);menu.Hide();menu=null;menus++;
     }
    }
    Check((values.TryGetValue("WidgetTheme",out var fresh)?fresh as string:null)==saved,"Popup audit changed owner theme");
    report["checks"]=JsonValue.CreateNumberValue(checks);report["actualRemappingPopups"]=JsonValue.CreateNumberValue(popups);report["actualDialogs"]=JsonValue.CreateNumberValue(dialogs);report["actualMenus"]=JsonValue.CreateNumberValue(menus);
    report["syntheticUiContent"]=JsonValue.CreateBooleanValue(true);report["realProductionFactories"]=JsonValue.CreateBooleanValue(true);report["backendWritesRequested"]=JsonValue.CreateNumberValue(0);report["ownerPhysicalInputTested"]=JsonValue.CreateBooleanValue(false);
   }
   finally
   {
    flyout?.Hide();menu?.Hide();dialog?.Hide();if(ReferenceEquals(_onexDetailsDialog,dialog))_onexDetailsDialog=null;if(view!=null)_onexRemapWindows.Remove(view);
    isThemeInitialized=false;ApplyTheme(values.TryGetValue("WidgetTheme",out var fresh)?fresh as string:OnexPalette.Name);isThemeInitialized=initialized;if(tab!=null&&tab.Visibility==Visibility.Visible)tab.IsChecked=true;
   }
  }
 }
}
