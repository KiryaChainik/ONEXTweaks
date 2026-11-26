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
  private async Task VerifyOnexThemeCatalogAsync(JsonObject report,bool restart)
  {
   var values=ApplicationData.Current.LocalSettings.Values;string ownerTheme=values.TryGetValue("WidgetTheme",out var raw)?raw as string:null;
   var originalTab=GetVisibleNavigationItems().FirstOrDefault(x=>x.IsChecked==true);bool initialized=isThemeInitialized;
   string ownerTabs=ReadOnexTabValue(OnexTabsKey);bool tabsStore=_onexTabsQaStore;bool themeStore=_onexThemeQaStore;_onexThemeQaStore=true;bool expanded=_onexTabsHeader.IsChecked==true;int checks=0;var rendered=new JsonArray();
   void Check(bool value,string why){checks++;if(!value)throw new InvalidOperationException(why);}
   try
   {
    EnsureOnexThemeCatalog();_onexTabsQaStore=true;EnsureOnexReleaseLayout();_onexTabsHeader.IsChecked=true;
    if(restart)
    {
     Check(values.TryGetValue("OnexThemeQa363",out var saved)&&saved as string=="Windows 11 Light","Isolated saved theme did not survive actual UWP restart");
     isThemeInitialized=false;await ApplyThemeOnLoadAsync(saved as string);
     Check(currentThemeName=="Windows 11 Light"&&RequestedTheme==ElementTheme.Light,"Saved light theme was not reapplied by production load path");
     Check((ThemeComboBox.SelectedItem as ComboBoxItem)?.Content as string=="Windows 11 Light","Saved theme selection differs from applied palette");
     values.Remove("OnexThemeQa363");
    }
    else
    {
     Check(OnexCatalogNames.Length==18,"Requested catalog is incomplete");
     Check(ThemeComboBox.Width>=280&&ThemeComboBox.MaxDropDownHeight<=360,"Theme picker is too narrow or unbounded");
     int i=0;
     foreach(string name in OnexCatalogNames)
     {
      var theme=WidgetThemes[name];Check(ThemeComboBox.Items.OfType<ComboBoxItem>().Count(x=>x.Content as string==name)==1,"Theme is missing or duplicated in picker");
      Check(OnexContrast(theme.TextPrimary,theme.CardBackground)>=4.5&&OnexContrast(theme.TextSecondary,theme.CardBackground)>=4.5&&OnexContrast(theme.TextSecondary,theme.ButtonBackground)>=4.5,"Primary/secondary palette contrast is too low: "+name);
      Check(OnexContrast(theme.EffectiveAccent(),theme.CardBackground)>=3,"Interactive accent lacks contrast: "+name);
      var choice=new ComboBox{RequestedTheme=ElementTheme.Dark};var option=new ComboBoxItem{Content="QA explicit dark item",RequestedTheme=ElementTheme.Dark};choice.Items.Add(option);ApplyTheme(name);ApplyOnexCatalogSurfaces(choice,theme);
      Check(choice.RequestedTheme==(OnexLight(theme)?ElementTheme.Light:ElementTheme.Dark)&&option.RequestedTheme==choice.RequestedTheme&&(option.Foreground as SolidColorBrush)?.Color==theme.TextPrimary,"Explicit dark editor/item overrides selected theme");
      SystemNavItem.IsChecked=true;isThemeInitialized=true;
      ThemeComboBox.SelectedItem=ThemeComboBox.Items.OfType<ComboBoxItem>().First(x=>x.Content as string==name);ApplyTheme(name);UpdateLayout();await Task.Delay(85);
      Check(currentThemeName==name&&RequestedTheme==(OnexLight(theme)?ElementTheme.Light:ElementTheme.Dark),"Theme light/dark root was not applied");
      Check((_onexTabsHeader.Foreground as SolidColorBrush)?.Color==theme.TextPrimary,"Settings header uses another theme");
      var cool=KbmCard(new TextBlock{Text="QA cool neutral card",Foreground=new SolidColorBrush(Colors.White)});ApplyOnexCatalogSurfaces(cool,theme);Check((cool.Background as SolidColorBrush)?.Color==theme.CardBackground,"Cool neutral card retained a dark background");
      Check((_onexKeyboardMouseNav.Foreground as SolidColorBrush)?.Color==theme.TextPrimary&&LegionNavItem.Content is Viewbox controllerArtwork&&controllerArtwork.Child is BitmapIcon originalController,"Navigation artwork has no readable catalog tint");
      Check(OnexDialogSurface.Color==theme.CardBackground&&OnexDialogAccent.Color==theme.EffectiveAccent(),"Dialog tokens use hard-coded dark/cyan values");
      using(var dialogScope=new OnexThemeDialogQa(this,theme))
      {Check(dialogScope.Valid,"Dialog resources do not match selected palette");if(name=="Dracula"||name=="Windows 11 Light")await dialogScope.RenderAsync(this,OnexLight(theme)?"light":"dark",report);}
      var menu=TrackOnexMenu(new MenuFlyout());menu.Items.Add(new MenuFlyoutItem{Text="QA selectable item"});ConfigureOnexCatalogFlyout(menu);
      Check((menu.Items[0].Foreground as SolidColorBrush)?.Color==theme.TextPrimary&&menu.MenuFlyoutPresenterStyle!=null,"Menu text/presenter is not themed");_onexOpenMenus.Remove(menu);
      _onexTabsCard.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(35);
      await SaveOnexImage("onex-theme363-"+(i++).ToString("00")+".png",_onexTabsCard,35);rendered.Add(JsonValue.CreateStringValue(name));
     }
     foreach(string name in new[]{"Dracula","Windows 11 Light"})
     {
      isThemeInitialized=false;ApplyTheme(name);var theme=WidgetThemes[name];
      foreach(var nav in GetVisibleNavigationItems().ToArray())
      {nav.IsChecked=true;await Task.Delay(90);Check(RequestedTheme==(OnexLight(theme)?ElementTheme.Light:ElementTheme.Dark),"Tab changed root theme");await SaveOnexImage("onex-theme363-tab-"+(OnexLight(theme)?"light":"dark")+"-"+(nav.Tag as string)+".png",this,30);}
      SystemNavItem.IsChecked=true;ApplyTheme(name);await Task.Delay(100);await SaveOnexImage("onex-theme363-page-"+(OnexLight(theme)?"light":"dark")+".png",SystemScrollViewer,50);
     }
     isThemeInitialized=true;ThemeComboBox.SelectedItem=ThemeComboBox.Items.OfType<ComboBoxItem>().First(x=>x.Content as string=="Windows 11 Light");
     Check(values.TryGetValue("OnexThemeQa363",out var saved)&&saved as string=="Windows 11 Light","Production selection handler did not save isolated theme");
    }
    Check((values.TryGetValue("WidgetTheme",out var current)?current as string:null)==ownerTheme&&ReadOnexTabValue(OnexTabsKey)==ownerTabs,"Theme test wrote owner preferences");
    report["checks"]=JsonValue.CreateNumberValue(checks);report["renderedThemes"]=rendered;
    report["actualOwnUwpRendering"]=JsonValue.CreateBooleanValue(true);report["isolatedThemeStore"]=JsonValue.CreateBooleanValue(true);
    report["actualProcessRestartStage"]=JsonValue.CreateBooleanValue(restart);report["ownerPreferencesWrites"]=JsonValue.CreateNumberValue(0);report["ownerPhysicalInputTested"]=JsonValue.CreateBooleanValue(false);
   }
   finally
   {
    try{
    isThemeInitialized=false;_onexTabsQaStore=tabsStore;EnsureOnexReleaseLayout();_onexTabsHeader.IsChecked=expanded;
    string fresh=values.TryGetValue("WidgetTheme",out var current)?current as string:OnexPalette.Name;
    ApplyTheme(fresh??OnexPalette.Name);ThemeComboBox.SelectedItem=ThemeComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(x=>x.Content as string==fresh);
    isThemeInitialized=initialized;if(originalTab!=null&&originalTab.Visibility==Visibility.Visible)originalTab.IsChecked=true;
    }finally{_onexThemeQaStore=themeStore;report["scopedThemeStoreReleased"]=JsonValue.CreateBooleanValue(!_onexThemeQaStore);report["checksBeforeCompletionOrFailure"]=JsonValue.CreateNumberValue(checks);}
   }
  }
  private sealed class OnexThemeDialogQa:IDisposable
  {
   internal bool Valid;private readonly ContentDialog dialog;
   internal OnexThemeDialogQa(GamingWidget owner,ThemeColors theme)
   {dialog=new ContentDialog{Title="QA theme dialog",Content=owner.OnexSettingsCard(new TextBlock{Text="Readable dialog text",Foreground=new SolidColorBrush(Colors.White)}),CloseButtonText="Close"};owner.ApplyOnexDialogAccents(dialog);var card=dialog.Content as Border;Valid=dialog.RequestedTheme==(OnexLight(theme)?ElementTheme.Light:ElementTheme.Dark)&&(dialog.Background as SolidColorBrush)?.Color==theme.CardBackground&&(card?.Child as TextBlock)?.Foreground is SolidColorBrush text&&text.Color==theme.TextPrimary&&card.Background is SolidColorBrush fill&&OnexContrast(text.Color,fill.Color)>=4.5;}
   public void Dispose(){dialog.Content=null;}
   internal async Task RenderAsync(GamingWidget owner,string suffix,JsonObject report)
   {
    var shown=dialog.ShowAsync();try{
     var content=(FrameworkElement)dialog.Content;await Task.Delay(180);dialog.UpdateLayout();content.UpdateLayout();
     for(int n=0;n<20&&(content.ActualWidth<=0||content.ActualHeight<=0);n++){await Task.Delay(50);dialog.UpdateLayout();content.UpdateLayout();}
     report["dialogGeometry"+suffix]=new JsonObject{{"dialogWidth",JsonValue.CreateNumberValue(dialog.ActualWidth)},{"dialogHeight",JsonValue.CreateNumberValue(dialog.ActualHeight)},{"contentWidth",JsonValue.CreateNumberValue(content.ActualWidth)},{"contentHeight",JsonValue.CreateNumberValue(content.ActualHeight)}};
     if(content.ActualWidth<=0||content.ActualHeight<=0)throw new InvalidOperationException("Own shown dialog content has no arranged bounds");
     // A popup-hosted ContentDialog can reject RenderTargetBitmap on the host itself.
     // Render its public, arranged content; no private template traversal or foreign UI.
     await owner.SaveOnexImage("onex-theme363-dialog-"+suffix+".png",content,100);
    }finally{dialog.Hide();}await shown;
   }
  }
 }
}
