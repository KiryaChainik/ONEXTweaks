using System;
using System.Collections.Generic;
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
  private async Task VerifyOnexCheckboxStabilityAsync(JsonObject report)
  {
   var values=ApplicationData.Current.LocalSettings.Values;string saved=values.TryGetValue("WidgetTheme",out var raw)?raw as string:null;
   bool initialized=isThemeInitialized;var original=GetVisibleNavigationItems().FirstOrDefault(x=>x.IsChecked==true);var visibility=_onexTabsBody.Visibility;
   string tabs=ReadOnexTabValue(OnexTabsKey);int focusMoves=0,themes=0,colourWrites=0,templateSwaps=0;bool audit=false;
   var unhook=new List<Action>();var result=new JsonArray();report["themes"]=result;
   void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
   try
   {
    Check(_onexDetailsDialog==null,"Protect owner dialog");isThemeInitialized=false;
    var boxes=_onexTabRows.Values.Select(x=>x.Visible).ToArray();var checkedBefore=boxes.Select(x=>x.IsChecked).ToArray();
    foreach(var box in boxes)
    {
     foreach(var property in new[]{Control.BackgroundProperty,Control.BorderBrushProperty,Control.ForegroundProperty,Control.TemplateProperty})
     {
      var current=property;long token=box.RegisterPropertyChangedCallback(current,(s,p)=>{if(!audit)return;if(current==Control.TemplateProperty)templateSwaps++;else colourWrites++;});
      unhook.Add(()=>box.UnregisterPropertyChangedCallback(current,token));
     }
    }
    foreach(string name in OnexAllowedThemeNames)
    {
     ApplyTheme(name);SystemNavItem.IsChecked=true;_onexTabsBody.Visibility=Visibility.Visible;ApplyOnexSettingsDesign();ApplyOnexPageFocus();UpdateLayout();await Task.Delay(180);
     var ink=boxes.Select(x=>(x.Background as SolidColorBrush)?.Color).ToArray();var templates=boxes.Select(x=>x.Template).ToArray();
     audit=true;
     for(int lap=0;lap<6;lap++)foreach(var row in _onexTabRows.Values)
     {
      foreach(var control in new Control[]{row.Visible,row.Up,row.Down}.Where(x=>x.IsEnabled))
      {
       control.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});Check(control.Focus(FocusState.Keyboard),"Cannot focus tab setting");await Task.Delay(8);focusMoves++;
      }
      if(row.Visible.IsEnabled){VisualStateManager.GoToState(row.Visible,"PointerOver",false);VisualStateManager.GoToState(row.Visible,"Normal",false);}
     }
     audit=false;
     for(int i=0;i<boxes.Length;i++)
     {
      Check(boxes[i].IsChecked==checkedBefore[i],"Focus changed actual tab visibility");
      Check((boxes[i].Background as SolidColorBrush)?.Color==ink[i],"Focus changed checkbox colour in "+name);
      Check(ReferenceEquals(boxes[i].Template,templates[i]),"Focus replaced checkbox template in "+name);
     }
     if(name==OnexPalette.Name||name=="Dracula"||name=="Windows 11 Light")await SaveOnexImage("onex-checkbox383-"+(name==OnexPalette.Name?"base":name=="Dracula"?"dark":"light")+".png",_onexTabsRows,40);
     themes++;result.Add(new JsonObject{{"name",JsonValue.CreateStringValue(name)},{"stableColours",JsonValue.CreateBooleanValue(true)},{"stableCheckedState",JsonValue.CreateBooleanValue(true)}});
    }
    Check(colourWrites==0&&templateSwaps==0,"Focus wrote checkbox colours/templates: "+colourWrites+"/"+templateSwaps);
    Check(ReadOnexTabValue(OnexTabsKey)==tabs,"Checkbox focus test changed tab preferences");
    Check((values.TryGetValue("WidgetTheme",out var fresh)?fresh as string:null)==saved,"Checkbox test changed owner theme");
    report["renderedThemes"]=JsonValue.CreateNumberValue(themes);report["actualFocusMoves"]=JsonValue.CreateNumberValue(focusMoves);report["colourWritesDuringFocus"]=JsonValue.CreateNumberValue(colourWrites);report["templateSwapsDuringFocus"]=JsonValue.CreateNumberValue(templateSwaps);
    report["hoverStatesProgrammatic"]=JsonValue.CreateBooleanValue(true);report["ownerPhysicalInputTested"]=JsonValue.CreateBooleanValue(false);report["settingsWrites"]=JsonValue.CreateNumberValue(0);
   }
   finally
   {
    audit=false;foreach(var remove in unhook)remove();_onexTabsBody.Visibility=visibility;
    isThemeInitialized=false;ApplyTheme(values.TryGetValue("WidgetTheme",out var fresh)?fresh as string:OnexPalette.Name);isThemeInitialized=initialized;
    if(original!=null&&original.Visibility==Visibility.Visible)original.IsChecked=true;
   }
  }
 }
}
