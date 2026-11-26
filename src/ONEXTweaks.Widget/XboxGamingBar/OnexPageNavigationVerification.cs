// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Automation.Provider;
using Windows.UI.Xaml.Input;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private bool _onexNavigationAuditHoldingState;
  private async Task VerifyOnexPageNavigationAsync(JsonObject report)
  {
   var folder=ApplicationData.Current.LocalFolder;string run=Guid.NewGuid().ToString("N");int step=0;var results=new JsonArray();report["tabs"]=results;
   var original=GetVisibleNavigationItems().FirstOrDefault(nav=>nav.IsChecked==true);var power=await ReadOnexStateAsync();
   void Check(bool ok,string text){if(!ok)throw new InvalidOperationException(text);}
   async Task Focus(Control control){control.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});await Task.Delay(60);Check(control.Focus(FocusState.Keyboard),"Cannot focus visible control: "+control.Name);await Task.Delay(30);}
   async Task Publish(string value){for(int attempt=0;;attempt++){try{File.WriteAllText(Path.Combine(folder.Path,"onex-gamepad-qa-state.json"),value);return;}catch(IOException){if(attempt>=40)throw;await Task.Delay(25);}}}
   async Task Input(string key,Func<bool> ready)
   {
    string id=run+":"+(++step);var tab=GetVisibleNavigationItems().First(nav=>nav.IsChecked==true);var rect=new RadioButtonAutomationPeer(tab).GetBoundingRectangle();
    report["injectedControllerSteps"]=JsonValue.CreateNumberValue(step);report["lastRequestedButton"]=JsonValue.CreateStringValue(key);await Publish(new JsonObject{{"id",JsonValue.CreateStringValue(id)},{"button",JsonValue.CreateStringValue(key)},{"x",JsonValue.CreateNumberValue(rect.X+rect.Width/2)},{"y",JsonValue.CreateNumberValue(rect.Y+rect.Height/2)}}.Stringify());
    bool accepted=false;for(int n=0;n<80;n++){await Task.Delay(60);try{accepted=File.ReadAllText(Path.Combine(folder.Path,"onex-gamepad-qa-ack.txt"))==id&&ready();}catch{}if(accepted)break;}Check(accepted,"Navigation input failed: "+key+" step="+step+" focus="+(FocusManager.GetFocusedElement() as FrameworkElement)?.Name);await Task.Delay(60);
   }
   async Task Edge(Control control,int direction)
   {await Focus(control);var rows=OnexPageNavigationRows();var target=OnexPageNavigationTarget(rows,control,direction);Check(target!=null,"No navigation target");await Input(direction==-2?"up":direction==2?"down":direction<0?"left":"right",()=>ReferenceEquals(FocusManager.GetFocusedElement(),target));}
   try
   {
    Check(OnexPerformancePendingDetails().Length==0,"Owner pending edit protected; defer navigation QA");
    PerformanceNavItem.IsChecked=true;await Task.Delay(150);await Input("focus",()=>true);
    foreach(var nav in GetVisibleNavigationItems())
    {
     nav.IsChecked=true;await Task.Delay(180);
     if(ReferenceEquals(nav,_releaseOsdNav)){for(int n=0;n<100&&_onexOverlayOpening;n++)await Task.Delay(50);await OpenOnexOverlaySettingsAsync();Check(OSDCustomizeContent.Visibility==Visibility.Visible,"Detailed Overlay did not open");await Task.Delay(100);}
     var rows=OnexPageNavigationRows();Check(rows.Count>0,"Empty focus graph: "+nav.Tag);
     var cells=rows.SelectMany(row=>row).ToArray();if(ReferenceEquals(nav,_releaseOsdNav))Check(cells.Length>6,"Overlay focus graph contains only its collapsed header");Check(cells.All(cell=>!(cell.Control is RadioButton tabControl&&GetVisibleNavigationItems().Contains(tabControl))),"Top tabs leaked into content focus graph");
     foreach(var cell in cells)foreach(int direction in new[]{-2,2,-1,1})Check(cells.Any(target=>ReferenceEquals(target.Control,OnexPageNavigationTarget(rows,cell.Control,direction))),"Focus edge leaves visible page");
     await Edge(rows[0][0].Control,2);await Edge(rows[Math.Min(1,rows.Count-1)][0].Control,-2);await Edge(rows.Last()[0].Control,-2);await Edge(rows[0][0].Control,-2);
     var wide=rows.FirstOrDefault(row=>row.Count>1);if(wide!=null){await Edge(wide[0].Control,1);await Edge(wide[1].Control,-1);}
     results.Add(new JsonObject{{"tab",JsonValue.CreateStringValue(nav.Tag.ToString())},{"visibleControls",JsonValue.CreateNumberValue(cells.Length)},{"rows",JsonValue.CreateNumberValue(rows.Count)},{"allEdgesStayOnPage",JsonValue.CreateBooleanValue(true)}});
    }
    var tabs=GetVisibleNavigationItems().ToArray();
    foreach(string key in new[]{"rt","lt"})for(int turn=0;turn<tabs.Length;turn++)
    {int index=Array.FindIndex(tabs,tab=>tab.IsChecked==true);var expected=tabs[(index+(key=="rt"?1:tabs.Length-1))%tabs.Length];await Input(key,()=>expected.IsChecked==true);Check(!(FocusManager.GetFocusedElement() is RadioButton top&&tabs.Contains(top)),"Triggers left focus in top tabs");}
    report["fullLtRtCycle"]=JsonValue.CreateBooleanValue(true);
    _releasePowerNav.IsChecked=true;await ReadOnexPerformanceProfilesAsync();
    // Read-only UI fixture: show both production source editors if the owner's current profile is unified.
    // No split/profile mutations or settings writes are sent to the helper.
    _onexNavigationAuditHoldingState=true;_onexProfilesState=JsonObject.Parse(_onexProfilesState.Stringify());OnexActiveProfile["split"]=JsonValue.CreateBooleanValue(true);RefreshOnexSourceEditors();await Task.Delay(150);
    await Edge(_onexAcEditor.Cards[0].Header,-2);Check(ReferenceEquals(FocusManager.GetFocusedElement(),_onexPerformanceProfilePicker.Split),"AC first row must return to split footer");
    await Edge(_onexPerformanceProfilePicker.Split,-2);Check(!ReferenceEquals(FocusManager.GetFocusedElement(),ProfileSettingsExpandToggle),"Split returns to hidden legacy header");
    await Edge(_onexPerformanceProfilePicker.Split,2);Check(ReferenceEquals(FocusManager.GetFocusedElement(),_onexAcEditor.Cards[0].Header)||ReferenceEquals(FocusManager.GetFocusedElement(),_onexAcEditor.Cards[1].Header),"Split cannot enter AC");
    foreach(var form in new[]{_onexDcEditor,_onexAcEditor})foreach(var card in form.Cards)
    {
     await Focus(card.Header);await Input("a",()=>ReferenceEquals(form.Open,card));await Edge(card.Header,2);await Edge(card.Close,2);await Focus(card.Close);await Input("a",()=>form.Open==null);
    }
    await SaveOnexImage("onex-navigation-ac248.png",PerformanceScrollViewer,100);
    Check((await ReadOnexStateAsync()).GetNamedString("rawLimits")==power.GetNamedString("rawLimits"),"Navigation changed actual TDP");
    report["tabs"]=results;report["allVisibleTabs"]=JsonValue.CreateBooleanValue(results.Count==GetVisibleNavigationItems().Count);report["acUpAndFooterReturn"]=JsonValue.CreateBooleanValue(true);report["all14SourceCardsOpenedAndNavigated"]=JsonValue.CreateBooleanValue(true);report["splitFixtureOnlyNoBackendMutation"]=JsonValue.CreateBooleanValue(true);report["injectedControllerSteps"]=JsonValue.CreateNumberValue(step);report["physicalOwnerInputTested"]=JsonValue.CreateBooleanValue(false);report["settingsWritesByFixture"]=JsonValue.CreateNumberValue(0);
   }
   finally
   {
    _onexNavigationAuditHoldingState=false;foreach(var form in new[]{_onexDcEditor,_onexAcEditor})if(form?.Open!=null){form.Open.Detail.Visibility=Visibility.Collapsed;form.Open=null;}
    await ReadOnexPerformanceProfilesAsync();if(original!=null)original.IsChecked=true;
    await Publish("{\"finished\":true}");
   }
  }
 }
}
