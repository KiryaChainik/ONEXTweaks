// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private async Task VerifyOnexDs4NavigationAsync(JsonObject report)
  {
   var original=GetVisibleNavigationItems().FirstOrDefault(item=>item.IsChecked==true);LegionNavItem.IsChecked=true;EnsureOnexControllerFrontend();SetOnexControllerCard(null);var host=(Panel)LegionScrollViewer.Content;
   int commits=0,clicks=0,context=0,step=0;string run=Guid.NewGuid().ToString("N");var keys=new List<string>();var folder=ApplicationData.Current.LocalFolder;
   var slider=new Slider{Name="OnexDs4NavigationSlider",Minimum=0,Maximum=100,Value=50,Height=44};OnexInputGuard.ConfigurePercentage(slider);OnexInputGuard.SetCommit(slider,()=>commits++);
   var button=new Button{Name="OnexDs4NavigationButton",Content="Temporary navigation test",Height=42,HorizontalAlignment=HorizontalAlignment.Stretch,Style=(Style)Resources["StandaloneButtonStyle"]};button.Click+=(s,e)=>clicks++;
   button.PreviewKeyDown+=(s,e)=>{keys.Add(e.Key.ToString());if(e.Key==VirtualKey.GamepadX||e.Key==VirtualKey.Application){context++;e.Handled=true;}};
   slider.PreviewKeyDown+=(s,e)=>keys.Add(e.Key.ToString());
   var panel=new StackPanel{Spacing=12,Margin=new Thickness(12)};panel.Children.Add(slider);panel.Children.Add(button);var card=OnexSettingsCard(panel);host.Children.Insert(0,card);
   void Check(bool ok,string why){if(!ok)throw new InvalidOperationException(why+"; received="+string.Join(",",keys));}
   async Task Input(string key,Func<bool> expected){
    string id=run+":"+(++step);var box=new RadioButtonAutomationPeer(LegionNavItem).GetBoundingRectangle();var state=new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"button",JsonValue.CreateStringValue(key)},{"x",JsonValue.CreateNumberValue(box.X+box.Width/2)},{"y",JsonValue.CreateNumberValue(box.Y+box.Height/2)}};
    await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-gamepad-qa-state.json",CreationCollisionOption.ReplaceExisting),state.Stringify());bool ok=false;
    for(int n=0;n<50;n++){await Task.Delay(100);try{if(await FileIO.ReadTextAsync(await folder.GetFileAsync("onex-gamepad-qa-ack.txt"))==id&&expected()){ok=true;break;}}catch{}}
    Check(ok,"DS4 input not received: "+key+" step "+step);await Task.Delay(100);
   }
   try{
    LegionScrollViewer.ChangeView(null,0,null,true);await Task.Delay(180);Check(slider.Focus(FocusState.Keyboard),"Temporary slider cannot focus");
    await Input("a",()=>OnexInputGuard.IsPreviewing(slider));await Input("right",()=>Math.Abs(slider.Value-55)<.01);await Input("b",()=>!OnexInputGuard.IsPreviewing(slider)&&Math.Abs(slider.Value-50)<.01);Check(commits==0,"Cancelled fixture applied a value");
    await Input("a",()=>OnexInputGuard.IsPreviewing(slider));await Input("left",()=>Math.Abs(slider.Value-45)<.01);await Input("a",()=>!OnexInputGuard.IsPreviewing(slider)&&commits==1);Check(ReferenceEquals(FocusManager.GetFocusedElement(),slider),"A moved focus after confirmation");
    await Input("down",()=>ReferenceEquals(FocusManager.GetFocusedElement(),button));await Input("a",()=>clicks==1);await Input("x",()=>context==1);await Input("up",()=>ReferenceEquals(FocusManager.GetFocusedElement(),slider));
    await Input("rt",()=>_onexKeyboardMouseNav.IsChecked==true);await Input("lt",()=>LegionNavItem.IsChecked==true);
    report["actualDs4CommandsReceived"]=JsonValue.CreateNumberValue(step);report["fixtureAConfirmBCancel"]=JsonValue.CreateBooleanValue(true);report["directionalFocusAndX"]=JsonValue.CreateBooleanValue(true);report["triggersSwitchTabs"]=JsonValue.CreateBooleanValue(true);report["hardwareWritesByFixture"]=JsonValue.CreateNumberValue(0);report["pointerInjectionUsed"]=JsonValue.CreateBooleanValue(false);report["receivedKeys"]=JsonValue.CreateStringValue(string.Join(",",keys));
   }
   finally{
    OnexInputGuard.Cancel(slider);host.Children.Remove(card);if(original!=null)original.IsChecked=true;await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-gamepad-qa-state.json",CreationCollisionOption.ReplaceExisting),"{\"finished\":true}");report["fixtureRemoved"]=JsonValue.CreateBooleanValue(true);
   }
  }
 }
}
