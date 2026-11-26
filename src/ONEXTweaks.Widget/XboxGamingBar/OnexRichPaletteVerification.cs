using System;
using System.Collections.Generic;
using System.Diagnostics;
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
  private async Task VerifyOnexRichPalettesAsync(JsonObject report)
  {
   int checks=0,headers=0,focusMoves=0;void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why);}
   var values=ApplicationData.Current.LocalSettings.Values;string saved=values.TryGetValue("WidgetTheme",out var raw)?raw as string:null;
   bool initialized=isThemeInitialized;var original=GetVisibleNavigationItems().FirstOrDefault(x=>x.IsChecked==true);var rows=new JsonArray();var times=new JsonArray();
   try
   {
    Check(_onexDetailsDialog==null&&!OnexSourceEditorsPending&&!_kbmBusy&&_kbmTuning.Count==0,"Protect pending owner edit");isThemeInitialized=false;
    Check(OnexAllowedThemeNames.Length==20&&ThemeComboBox.Items.Count==20,"Theme choices changed");int index=0;
    foreach(string name in OnexAllowedThemeNames)
    {
     SystemNavItem.IsChecked=true;var watch=Stopwatch.StartNew();ApplyTheme(name);UpdateLayout();times.Add(JsonValue.CreateNumberValue(watch.Elapsed.TotalMilliseconds));
     var theme=_onexAppliedTheme;var roles=OnexRoles(theme);Check(roles.Accent.Distinct().Count()==4,"Theme still collapses all accents: "+name);
     for(int role=0;role<4;role++)
     {
      foreach(var surface in new[]{theme.CardBackground,theme.ButtonBackground})
      {Check(OnexContrast(OnexOver(roles.Accent[role],surface,.85),surface)>=3,"Graphic role contrast: "+name);Check(OnexContrast(roles.Ink[role],surface)>=4.5,"Text role contrast: "+name);}
      rows.Add(new JsonObject{{"theme",JsonValue.CreateStringValue(name)},{"role",JsonValue.CreateNumberValue(role)},{"graphic",JsonValue.CreateStringValue(roles.Accent[role].ToString())},{"ink",JsonValue.CreateStringValue(roles.Ink[role].ToString())}});
     }
     _releasePowerNav.IsChecked=true;UpdateLayout();
     var title=OnexOwnedContent(_onexPerformanceProfilePickerCard).OfType<TextBlock>().FirstOrDefault(text=>text.FontSize==16&&text.FontWeight.Weight>=600);
     Check(title!=null&&title.Foreground is SolidColorBrush titleInk&&titleInk.Color==theme.TextPrimary,"Profile name lost readable neutral ink: "+name);
     var actual=new List<Tuple<string,Button>>();
     if(OnexPerfCardGrid.Visibility==Visibility.Visible)actual.AddRange(_onexPerfSections.Select(card=>Tuple.Create(card.Id,card.Header)));
     else if(_onexDcEditor?.Root.Visibility==Visibility.Visible)actual.AddRange(_onexDcEditor.Cards.Select(card=>Tuple.Create(card.Id,card.Header)));
     foreach(var card in actual.Where(card=>new[]{"tdp","cpu","mode","fps"}.Contains(card.Item1)))
     {
      var icon=OnexOwnedContent(card.Item2.Content as DependencyObject).OfType<FontIcon>().FirstOrDefault();
      Check(icon!=null&&icon.Foreground is SolidColorBrush colour&&colour.Color==OnexPerformanceAccent(card.Item1),"Actual performance header lost its semantic accent: "+name+"/"+card.Item1);
      Check(card.Item2.ActualWidth>80&&card.Item2.ActualHeight>30,"Actual performance header not laid out");headers++;
      card.Item2.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});UpdateLayout();await SaveOnexImage("onex-roles391-perf-"+index+"-"+card.Item1+".png",card.Item2,30);
     }
     Check(actual.Count>=4,"Performance palette checks had no actual cards");
     _onexKeyboardMouseNav.IsChecked=true;UpdateLayout();foreach(var slider in new[]{_kbmMouseSpeed,_kbmScrollSpeed,_kbmDeadzone,_kbmHoldAcceleration,_kbmNudgeStep})
      Check(slider.Foreground is SolidColorBrush fill&&fill.Color==OnexRoleAccent(1),"KBM slider lost device accent: "+name);
     Check(RailBrightnessSlider.Foreground is SolidColorBrush media&&media.Color==OnexRoleAccent(1),"Media rail uses mismatched accent: "+name);
     SystemNavItem.IsChecked=true;UpdateLayout();var boxes=OnexOwnedContent(SystemScrollViewer).OfType<CheckBox>().Where(box=>box.IsEnabled&&box.IsLoaded).Take(8).ToArray();
     foreach(var box in boxes)
     {
      var before=box.Background;var foreground=box.Foreground;var template=box.Template;var flag=box.IsChecked;
      box.Focus(FocusState.Keyboard);focusMoves++;Check(ReferenceEquals(before,box.Background)&&ReferenceEquals(foreground,box.Foreground)&&ReferenceEquals(template,box.Template)&&flag==box.IsChecked,"Theme checkbox changes while focus moves: "+name);
     }
     var panel=MainNavPanel as Panel;var old=panel.Background;panel.Background=new SolidColorBrush(theme.PageBackground);
     try{await SaveOnexImage("onex-roles391-nav-"+index+".png",panel,30);}finally{panel.Background=old;}
     foreach(var badge in new[]{OnexLtHint,OnexRtHint})if(badge.Child is TextBlock text&&text.Foreground is SolidColorBrush ink&&badge.Background is SolidColorBrush bg)
      Check(OnexContrast(ink.Color,bg.Color)>=4.5,"LT/RT badge lost contrast: "+name);
     index++;report["completedThemes"]=JsonValue.CreateNumberValue(index);var progress=await ApplicationData.Current.LocalFolder.CreateFileAsync("onex-rich-palettes-progress.json",CreationCollisionOption.ReplaceExisting);await FileIO.WriteTextAsync(progress,report.Stringify());
    }
    Check(headers==80,"Expected four actual performance headers in every theme");
    Check((values.TryGetValue("WidgetTheme",out var fresh)?fresh as string:null)==saved,"Palette verification wrote owner theme");
    report["checks"]=JsonValue.CreateNumberValue(checks);report["themes"]=JsonValue.CreateNumberValue(20);report["actualPerformanceHeaders"]=JsonValue.CreateNumberValue(headers);report["actualFocusMoves"]=JsonValue.CreateNumberValue(focusMoves);report["roles"]=rows;report["themeApplyAndLayoutMs"]=times;
    report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);report["hardwareWritesRequested"]=JsonValue.CreateNumberValue(0);
   }
   finally
   {
    isThemeInitialized=false;string fresh=values.TryGetValue("WidgetTheme",out var current)?current as string:OnexPalette.Name;ApplyTheme(fresh);isThemeInitialized=initialized;
    if(original!=null&&original.Visibility==Visibility.Visible)original.IsChecked=true;
   }
  }
 }
}
