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
  private async Task VerifyOnexOwnerPalettesAsync(JsonObject report)
  {
   var values=ApplicationData.Current.LocalSettings.Values;string savedTheme=values.TryGetValue("WidgetTheme",out var raw)?raw as string:null;
   bool hadGlass=values.TryGetValue(OnexGlassSurfaces.Setting,out var savedGlass);bool? lastOwnGlass=null;
   bool themeReady=isThemeInitialized,glassReady=isGlassToggleInitialized;var original=GetVisibleNavigationItems().FirstOrDefault(x=>x.IsChecked==true);
   var appearance=ThemeContent.Visibility;var body=(StackPanel)SystemScrollViewer.Content;
   var fixture=new ComboBox{Name="OnexOwnerPaletteFixture",Header="Palette selection check",Width=480,MaxDropDownHeight=260};
   foreach(string label in new[]{"First choice","Second choice","Third choice"})fixture.Items.Add(new ComboBoxItem{Content=label});
   var fixtureCard=OnexSettingsCard(fixture);int checks=0,visits=0,popupStates=0,profileTexts=0;var rows=new JsonArray();
   void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
   Color Background(Brush brush)=>brush is SolidColorBrush solid?solid.Color:brush is LinearGradientBrush gradient?gradient.GradientStops.Last().Color:Colors.Transparent;
   try
   {
    Check(_onexDetailsDialog==null&&!OnexSourceEditorsPending&&!_kbmBusy&&_kbmTuning.Count==0,"Protect owner edits");
    isThemeInitialized=false;isGlassToggleInitialized=true;SystemNavItem.IsChecked=true;ThemeContent.Visibility=Visibility.Visible;
    body.Children.Insert(0,fixtureCard);ConfigureOnexFocus(fixture);fixture.SelectedIndex=0;
    Check(OnexOwnerPaletteRecords.Length==11&&OnexAllowedThemeNames.Length==20&&ThemeComboBox.Items.Count==20,"Expected11 source palettes and20 retained choices");
    Check(NormalizeOnexThemeName("Kanagawa Dark")=="Kanagawa Wave"&&NormalizeOnexThemeName("Kanagawa Light")=="Kanagawa Lotus"&&NormalizeOnexThemeName("Catppuccin Frappe")=="Catppuccin Frappé","Saved alias lost its palette");
    int index=0;
    foreach(var name in OnexAllowedThemeNames)
    {
     SystemNavItem.IsChecked=true;ThemeComboBox.SelectedItem=ThemeComboBox.Items.OfType<ComboBoxItem>().Single(x=>x.Content as string==name);ApplyTheme(name);UpdateLayout();
     var theme=_onexAppliedTheme;var source=OnexOwnerPaletteRecords.FirstOrDefault(x=>x.Name==name);
     if(source!=null)
     {
      Check(theme.PageBackground==OnexHex(source.Page)&&theme.TextPrimary==OnexHex(source.Text),"Source page/text drift: "+name);
      Check(theme.SelectionBackground==OnexHex(source.Selection)&&theme.SelectionText==OnexHex(source.SelectedText),"Source selection drift: "+name);
      Check(theme.CardBackground==OnexOver(theme.TextPrimary,theme.PageBackground,.04)&&theme.ButtonBackground==OnexOver(theme.TextPrimary,theme.PageBackground,.08),"Undocumented derived surface: "+name);
      Check(OnexContrast(theme.SelectionText.Value,theme.SelectionBackground.Value)>=4.5,"Source selected text contrast: "+name);
     }
     var surfaces=OnexThemeContrastSurfaces(theme);var roles=OnexRoles(theme);
     foreach(var background in surfaces)
     {
      Check(OnexContrast(theme.TextPrimary,background)>=4.5,"Primary ink contrast: "+name);
      // OLED retains its owner-requested secondary grey; its baseline is protected.
      Check(OnexContrast(theme.TextSecondary,background)+.001>=Math.Min(4.5,OnexContrast(theme.TextSecondary,theme.ButtonBackground)),"Secondary ink contrast: "+name);
      for(int role=0;role<4;role++)
      {Check(OnexContrast(roles.Ink[role],background)>=4.5,"Role text contrast: "+name);Check(OnexContrast(OnexOver(roles.Accent[role],background,.85),background)>=3,"Role graphic contrast: "+name);}
     }
     Check(roles.Accent.Distinct().Count()==4,"Palette collapsed to one accent: "+name);
     foreach(bool glass in new[]{false,true})
     {
      SystemNavItem.IsChecked=true;lastOwnGlass=glass;
      if(GlassEffectToggle.IsOn==glass)GlassEffectToggle_Toggled(GlassEffectToggle,null);else GlassEffectToggle.IsOn=glass;
      foreach(var tab in GetVisibleNavigationItems())
      {
       tab.IsChecked=true;UpdateLayout();await Task.Delay(30);var root=OnexNavigationRoot();
       Check(root!=null&&_onexAppliedTheme.Name==name,"Incoming page lost selected palette");
       Check(root.ActualTheme==(OnexLight(theme)?ElementTheme.Light:ElementTheme.Dark),"Incoming light/dark theme drift");
       foreach(var badge in new[]{OnexLtHint,OnexRtHint})if(badge.Child is TextBlock caption&&caption.Foreground is SolidColorBrush ink&&badge.Background is SolidColorBrush fill)
        Check(OnexContrast(ink.Color,fill.Color)>=4.5&&caption.Opacity==1,"Unreadable LT/RT badge: "+name);
       foreach(var header in new[]{_onexPerformanceProfilePickerCard,_onexScreenProfilePickerCard,_onexFanProfilePickerCard,_onexControllerPickerCard,_onexOverlayPresetHeader}.Where(x=>x!=null&&x.IsLoaded))
        foreach(var caption in OnexOwnedContent(header).OfType<TextBlock>().Where(x=>x.FontSize==12&&x.IsLoaded&&x.Foreground is SolidColorBrush))
        {
         Check(caption.Opacity==1,"Profile summary still fades enabled text: "+name);
         // Inspect actual owner UI without logging any private profile names.
         profileTexts++;
        }
       visits++;
      }
     }
     SystemNavItem.IsChecked=true;fixtureCard.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});ConfigureOnexFocus(fixture);UpdateLayout();
     fixture.IsDropDownOpen=true;await Task.Delay(100);
     foreach(int selected in new[]{0,1,2,0})
     {
      fixture.SelectedIndex=selected;await Task.Delay(25);UpdateLayout();
      Check(fixture.Items.OfType<ComboBoxItem>().Count(x=>x.IsSelected)==1,"Popup selection not exclusive");
      foreach(var item in fixture.Items.OfType<ComboBoxItem>())
      {
       item.ApplyTemplate();Check(VisualTreeHelper.GetChildrenCount(item)>0,"Fixture row not realized");
       var itemRoot=VisualTreeHelper.GetChild(item,0) as FrameworkElement;Check(itemRoot?.Name=="OnexChoiceRoot","Expected owned popup template");
       var marker=itemRoot.FindName("OwnSelected") as Border;var tick=itemRoot.FindName("OwnSelectedCheck") as FontIcon;var surface=itemRoot.FindName("OwnSurface") as Border;
       Color expected=item.IsSelected?(theme.SelectionText??theme.TextPrimary):theme.TextPrimary;
       Check((item.Foreground as SolidColorBrush)?.Color==expected,"Selected row uses wrong text ink: "+name);
       foreach(var state in new[]{"Normal","PointerOver","Pressed","Normal"})
       {
        VisualStateManager.GoToState(item,state,false);await Task.Delay(2);
        Check(surface.Opacity==1,"Enabled popup state dims text: "+name+"/"+state);
        Check(OnexContrast(expected,Background(surface.Background))>=4.5,"Popup state contrast: "+name+"/"+state);
        Check((marker.Visibility==Visibility.Visible)==item.IsSelected&&(tick.Visibility==Visibility.Visible)==item.IsSelected,"Historical selection marker survived: "+name);popupStates++;
       }
      }
     }
     var selectedRow=fixture.Items.OfType<ComboBoxItem>().Single(x=>x.IsSelected);await SaveOnexImage("onex-owner408-choice-"+index+".png",selectedRow,30);fixture.IsDropDownOpen=false;
     fixtureCard.Visibility=Visibility.Collapsed;ThemeContent.Visibility=Visibility.Visible;GlassEffectToggle.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});UpdateLayout();await Task.Delay(50);
     await SaveOnexImage("onex-owner408-settings-"+index+".png",this,30);fixtureCard.Visibility=Visibility.Visible;
     var sampleName=new TextBlock{Text="Example profile",FontSize=16};var sampleKind=new TextBlock{Text="Manual · Active",FontSize=12};var sampleDetails=new TextBlock{Text="15 W · CPU Boost Off · Balanced · Fan Auto",FontSize=12};
     var choose=new Button{Content=sampleName,Style=OnexSettingsButtonStyle(false)};var panel=new StackPanel{Spacing=8};panel.Children.Add(choose);panel.Children.Add(sampleKind);panel.Children.Add(sampleDetails);var sample=OnexSettingsCard(panel);body.Children.Insert(0,sample);
     try
     {
      PaintOnexProfileChoiceRow(choose,sampleName,sampleKind,sampleDetails,sample,true,false,OnexSolidBrush(OnexRoleAccent(0)));sample.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});UpdateLayout();await Task.Delay(35);
      Check(OnexContrast(((SolidColorBrush)sampleName.Foreground).Color,theme.CardBackground)>=4.5&&OnexContrast(((SolidColorBrush)sampleDetails.Foreground).Color,theme.CardBackground)>=4.5,"Saved profile fixture is unreadable: "+name);
      Check(OnexContrast(((SolidColorBrush)sampleKind.Foreground).Color,theme.CardBackground)>=4.5,"Active profile summary uses graphic-only ink: "+name);
      await SaveOnexImage("onex-owner408-profile-"+index+".png",sample,30);
     }
     finally{body.Children.Remove(sample);}
     rows.Add(new JsonObject{{"theme",JsonValue.CreateStringValue(name)},{"sourceFile",JsonValue.CreateStringValue(source?.File??"retained adaptation")},{"sourceSha256",JsonValue.CreateStringValue(source?.Sha??"")}});
     index++;report["completedThemes"]=JsonValue.CreateNumberValue(index);await FileIO.WriteTextAsync(await ApplicationData.Current.LocalFolder.CreateFileAsync("onex-owner-palettes-progress.json",CreationCollisionOption.ReplaceExisting),report.Stringify());
    }
    Check(profileTexts>0,"No actual profile headers inspected");
    Check((values.TryGetValue("WidgetTheme",out var fresh)?fresh as string:null)==savedTheme,"Owner theme preference changed");
    report["checks"]=JsonValue.CreateNumberValue(checks);report["themes"]=rows;report["sourcePalettes"]=JsonValue.CreateNumberValue(11);report["pageVisits"]=JsonValue.CreateNumberValue(visits);report["popupStates"]=JsonValue.CreateNumberValue(popupStates);report["actualProfileTextSamples"]=JsonValue.CreateNumberValue(profileTexts);
    report["profileFixtureSyntheticData"]=JsonValue.CreateBooleanValue(true);report["actualUwpRenderingAndProgrammaticInput"]=JsonValue.CreateBooleanValue(true);report["ownerPhysicalInputTested"]=JsonValue.CreateBooleanValue(false);report["hardwareWritesRequested"]=JsonValue.CreateNumberValue(0);
   }
   finally
   {
    fixture.IsDropDownOpen=false;body.Children.Remove(fixtureCard);isGlassToggleInitialized=false;isThemeInitialized=false;
    if(lastOwnGlass.HasValue&&values.TryGetValue(OnexGlassSurfaces.Setting,out var now)&&now is bool current&&current==lastOwnGlass.Value)
    {if(hadGlass)values[OnexGlassSurfaces.Setting]=savedGlass;else values.Remove(OnexGlassSurfaces.Setting);}
    GlassEffectToggle.IsOn=IsGlassEffectEnabled();string fresh=values.TryGetValue("WidgetTheme",out var value)?value as string:OnexPalette.Name;
    ApplyTheme(fresh);ThemeComboBox.SelectedItem=ThemeComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(x=>x.Content as string==NormalizeOnexThemeName(fresh));ThemeContent.Visibility=appearance;isGlassToggleInitialized=glassReady;isThemeInitialized=themeReady;
    if(original?.Visibility==Visibility.Visible)original.IsChecked=true;
    report["restoredGlassSetting"]=JsonValue.CreateBooleanValue(hadGlass?values.TryGetValue(OnexGlassSurfaces.Setting,out var final)&&Equals(final,savedGlass):!values.ContainsKey(OnexGlassSurfaces.Setting));
   }
  }
 }
}
