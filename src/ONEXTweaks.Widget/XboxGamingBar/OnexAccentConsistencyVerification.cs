// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexAccentConsistencyAsync(JsonObject report)
  {
   var original=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);int frames=0;var tabs=new JsonArray();
   void Full(Border border)
   {
    if(border==null)return;var t=border.BorderThickness;
    if(t.Left<=0||t.Left!=t.Top||t.Top!=t.Right||t.Right!=t.Bottom)throw new InvalidOperationException("Incomplete section outline: "+border.Name);frames++;
   }
   try
   {
    if(!OnexBrandTheme)throw new InvalidOperationException("This verification targets the active ONEX brand theme");
    foreach(var nav in GetVisibleNavigationItems())
    {
     nav.IsChecked=true;await Task.Delay(170);ApplyOnexTabPalette();UpdateLayout();
     foreach(var item in GetVisibleNavigationItems())
     {
      var art=item.Content as FrameworkElement;double opacity=item.IsChecked==true?1:.65;
      if(art!=null&&Math.Abs(art.Opacity-opacity)>.001)throw new InvalidOperationException("Tab selection differs by artwork type");
      if(item.Content is IconElement icon&&(!(icon.Foreground is SolidColorBrush brush)||brush.Color!=Windows.UI.Color.FromArgb(255,240,244,250)))throw new InvalidOperationException("A tab icon still uses its own accent color");
     }
     var selected=FindDescendantByName(nav,"SelectionBackground") as Border;
     if(selected==null||!(selected.BorderBrush is SolidColorBrush stroke)||stroke.Color!=OnexNavigationAccent(nav.Tag as string))throw new InvalidOperationException("Selected tab outline does not match its page accent");
     if(ReferenceEquals(nav,_releasePowerNav))
     {
      Full(_onexPerformanceProfilePickerCard);if(_onexPerfSections!=null)foreach(var section in _onexPerfSections)Full(section.Detail);
      foreach(var form in new[]{_onexDcEditor,_onexAcEditor}.Where(value=>value!=null))foreach(var card in form.Cards)Full(card.Detail);
      await SaveOnexImage("onex-accent-tabs250.png",MainNavPanel,100);await SaveOnexImage("onex-accent-header250.png",_onexPerformanceProfilePickerCard,100);
     }
     if(ReferenceEquals(nav,DisplayNavItem)){Full(_onexScreenProfilePickerCard);Full(_onexHdrCard);}
     if(ReferenceEquals(nav,FanNavItem))Full(_onexFanProfilePickerCard);
     if(ReferenceEquals(nav,LegionNavItem))Full(_onexControllerPickerCard);
     if(ReferenceEquals(nav,_releaseOsdNav))Full(OnexOverlaySettingsCard);
     tabs.Add(JsonValue.CreateStringValue(nav.Tag.ToString()));
    }
    report["tabs"]=tabs;report["neutralIconSelectedOutlineConsistent"]=JsonValue.CreateBooleanValue(true);report["fullSectionFrames"]=JsonValue.CreateNumberValue(frames);report["hardwareSettingsWrites"]=JsonValue.CreateNumberValue(0);
   }
   finally{if(original!=null)original.IsChecked=true;}
  }
 }
}
