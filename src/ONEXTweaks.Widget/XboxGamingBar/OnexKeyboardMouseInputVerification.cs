// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Graphics.Display;
using Windows.Foundation;
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
  private async Task VerifyOnexKeyboardMouseInputAsync(JsonObject report,bool keyboardOnly=false){
   var tab=GetVisibleNavigationItems().FirstOrDefault(item=>item.IsChecked==true);LegionNavItem.IsChecked=true;EnsureOnexControllerFrontend();SetOnexControllerCard(null);var host=(Panel)LegionScrollViewer.Content;var folder=ApplicationData.Current.LocalFolder;string run=Guid.NewGuid().ToString("N");var events=new List<string>();int step=0,checks=0;bool passed=false,hovered=false;int pointerMoves=0;
   var receiver=new Button{Name="OnexKeyboardMouseOwnedReceiver",Content=new TextBlock{Text="Temporary keyboard & mouse verification\nNo settings are changed by this control.",TextWrapping=TextWrapping.Wrap},MinHeight=210,HorizontalAlignment=HorizontalAlignment.Stretch,Style=(Style)Resources["StandaloneButtonStyle"]};
   receiver.PreviewKeyDown+=(s,e)=>{events.Add("kd:"+(int)e.Key);e.Handled=true;};receiver.PreviewKeyUp+=(s,e)=>{events.Add("ku:"+(int)e.Key);e.Handled=true;};receiver.RightTapped+=(s,e)=>e.Handled=true;
   receiver.AddHandler(UIElement.PointerPressedEvent,new PointerEventHandler((s,e)=>{var p=e.GetCurrentPoint(receiver).Properties;events.Add("pd:"+(p.IsLeftButtonPressed?"left":p.IsRightButtonPressed?"right":p.IsMiddleButtonPressed?"middle":"other"));e.Handled=true;}),true);
   receiver.AddHandler(UIElement.PointerReleasedEvent,new PointerEventHandler((s,e)=>{events.Add("pu:"+e.GetCurrentPoint(receiver).Properties.PointerUpdateKind+":"+e.KeyModifiers);e.Handled=true;}),true);
   receiver.AddHandler(UIElement.PointerWheelChangedEvent,new PointerEventHandler((s,e)=>{var p=e.GetCurrentPoint(receiver).Properties;events.Add((p.IsHorizontalMouseWheel?"wh:":"wv:")+p.MouseWheelDelta);e.Handled=true;}),true);
   receiver.PointerEntered+=(s,e)=>hovered=true;receiver.PointerExited+=(s,e)=>hovered=false;receiver.PointerMoved+=(s,e)=>{var p=e.GetCurrentPoint(receiver).Position;hovered=p.X>=0&&p.Y>=0&&p.X<=receiver.ActualWidth&&p.Y<=receiver.ActualHeight;pointerMoves++;};
   var card=OnexSettingsCard(receiver);host.Children.Insert(0,card);
   void Check(bool ok,string why){checks++;if(!ok)throw new InvalidOperationException(why+"; events="+string.Join(",",events));}
   bool Key(IEnumerable<string> rows,string prefix,int a,int b=0)=>rows.Contains(prefix+a)||b!=0&&rows.Contains(prefix+b);
   async Task WriteState(string value){for(int retry=0;;retry++){try{await FileIO.WriteTextAsync(await folder.CreateFileAsync("onex-kbm348-qa-state.json",CreationCollisionOption.OpenIfExists),value);return;}catch(Exception error)when(retry<40&&(error.HResult==unchecked((int)0x80070020)||error.HResult==unchecked((int)0x80070021)||error.HResult==unchecked((int)0x80070005))){await Task.Delay(25);}}}
   async Task Input(string kind,Func<string[],bool> expected){
    string id=run+":"+(++step);int before=events.Count;bool done=false;
    for(int n=0;n<100;n++){
     var box=new ButtonAutomationPeer(receiver).GetBoundingRectangle();Check(box.Width>180&&box.Height>80,"Own keyboard/mouse receiver is not rendered");
     var pointer=Window.Current.CoreWindow.PointerPosition;var center=receiver.TransformToVisual(Window.Current.Content).TransformPoint(new Point(receiver.ActualWidth/2,receiver.ActualHeight/2));
     var state=new JsonObject{{"hovered",JsonValue.CreateBooleanValue(hovered)},{"pointerMoves",JsonValue.CreateNumberValue(pointerMoves)},{"pointerLocalX",JsonValue.CreateNumberValue(pointer.X)},{"pointerLocalY",JsonValue.CreateNumberValue(pointer.Y)},{"targetLocalX",JsonValue.CreateNumberValue(center.X)},{"targetLocalY",JsonValue.CreateNumberValue(center.Y)},{"scale",JsonValue.CreateNumberValue(DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel)},{"active",JsonValue.CreateBooleanValue(true)},{"visible",JsonValue.CreateBooleanValue(receiver.IsLoaded)},{"focused",JsonValue.CreateBooleanValue(ReferenceEquals(FocusManager.GetFocusedElement(),receiver))},{"run",JsonValue.CreateStringValue(run)},{"id",JsonValue.CreateStringValue(id)},{"kind",JsonValue.CreateStringValue(kind)},{"utc",JsonValue.CreateStringValue(DateTimeOffset.UtcNow.ToString("o"))},{"x",JsonValue.CreateNumberValue(Math.Round(box.X+box.Width/2))},{"y",JsonValue.CreateNumberValue(Math.Round(box.Y+box.Height/2))}};
     await WriteState(state.Stringify());
     try{var ack=JsonObject.Parse(await FileIO.ReadTextAsync(await folder.GetFileAsync("onex-kbm348-qa-ack.json")));if(ack.GetNamedString("id","")==id&&ack.GetNamedBoolean("passed",false)&&expected(events.Skip(before).ToArray())){done=true;break;}}catch{}
     await Task.Delay(100);
    }
    Check(done,"Own keyboard/mouse action not received: "+kind);
   }
   try{
    LegionScrollViewer.ChangeView(null,0,null,true);await Task.Delay(160);Check(receiver.Focus(FocusState.Keyboard),"Own receiver cannot focus");
    await Input("space",v=>Key(v,"kd:",32)&&Key(v,"ku:",32));
    await Input("shift",v=>Key(v,"kd:",16,160)&&Key(v,"ku:",16,160));
    await Input("control",v=>Key(v,"kd:",17,162)&&Key(v,"ku:",17,162));
    if(!keyboardOnly){
    await Input("left",v=>v.Contains("pd:left")&&v.Any(x=>x.StartsWith("pu:LeftButtonReleased:")));
    await Input("right",v=>v.Contains("pd:right")&&v.Any(x=>x.StartsWith("pu:RightButtonReleased:")));
    await Input("middle",v=>v.Contains("pd:middle")&&v.Any(x=>x.StartsWith("pu:MiddleButtonReleased:")));
    await Input("ctrlClick",v=>{int up=Array.FindIndex(v,x=>x.StartsWith("pu:LeftButtonReleased:")&&x.Contains("Control")),keyUp=Array.FindIndex(v,x=>x=="ku:17"||x=="ku:162");return up>=0&&keyUp>up;});
    await Input("wheelV",v=>v.Any(x=>x.StartsWith("wv:")&&x!="wv:0"));
    await Input("wheelH",v=>v.Any(x=>x.StartsWith("wh:")&&x!="wh:0"));
    }
    await Input("fn",v=>Key(v,"kd:",67)&&Key(v,"ku:",67)&&Key(v,"kd:",17,162)&&Key(v,"ku:",17,162)&&!Key(v,"kd:",32));
    await Input("journal",v=>Key(v,"kd:",17,162)&&Key(v,"ku:",17,162));
    passed=true;report["scope"]=JsonValue.CreateStringValue(keyboardOnly?"keyboard-modifiers-FN-journal":"full-pointer-keyboard");report["pointerMoves"]=JsonValue.CreateNumberValue(pointerMoves);report["steps"]=JsonValue.CreateNumberValue(step);report["checks"]=JsonValue.CreateNumberValue(checks);report["actualOwnUwpKeyboardEvents"]=JsonValue.CreateBooleanValue(true);report["actualOwnUwpPointerEvents"]=JsonValue.CreateBooleanValue(!keyboardOnly);report["actualModifierMouseReleaseOrder"]=JsonValue.CreateBooleanValue(!keyboardOnly);report["actualBothWheels"]=JsonValue.CreateBooleanValue(!keyboardOnly);report["actualFnKeys"]=JsonValue.CreateBooleanValue(true);report["profileWrites"]=JsonValue.CreateNumberValue(0);report["ownerPhysicalInputTested"]=JsonValue.CreateBooleanValue(false);report["events"]=JsonValue.CreateStringValue(string.Join(",",events));
   }finally{host.Children.Remove(card);if(tab!=null)tab.IsChecked=true;await WriteState(new JsonObject{{"finished",JsonValue.CreateBooleanValue(true)},{"passed",JsonValue.CreateBooleanValue(passed)},{"run",JsonValue.CreateStringValue(run)}}.Stringify());report["fixtureRemoved"]=JsonValue.CreateBooleanValue(true);}
  }
 }
}
