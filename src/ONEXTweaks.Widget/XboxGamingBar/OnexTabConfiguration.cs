// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;

namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private const string OnexTabsKey="OnexTabsV1",OnexTabsQaKey="OnexTabsQa353";
  private static readonly string[] OnexTabIds={"Performance","OnexPower","Legion","KeyboardMouse","Display","OnexOsd","Fan","Lighting","System"};
  private static readonly string[] OnexTabNames={"Home","Performance","Controller","Keyboard / Mouse","Screen Colors","Overlay","Fan","Lighting","Settings"};
  private sealed class OnexTabsPreference
  {
   internal readonly List<string> Order=new List<string>(OnexTabIds);
   internal readonly HashSet<string> Hidden=new HashSet<string>();
   internal string Open="";
   internal string Serialize()=>new JsonObject{{"order",JsonArray.Parse("["+string.Join(",",Order.Select(x=>"\""+x+"\""))+"]")},{"hidden",JsonArray.Parse("["+string.Join(",",Hidden.OrderBy(x=>x).Select(x=>"\""+x+"\""))+"]")},{"open",JsonValue.CreateStringValue(Open)}}.Stringify();
  }
  private sealed class OnexTabRow{internal Grid Root;internal CheckBox Visible;internal Button Up,Down;}
  private bool _onexTabsUiLoading,_onexTabsQaStore,_onexTabsOpenRefreshPending;
  private string _onexTabsUiVersion,_onexFirstRevealTab;
  private Border _onexTabsCard;
  private StackPanel _onexTabsBody,_onexTabsRows;
  private ToggleButton _onexTabsHeader;
  private Button _onexTabsRestore;
  private ComboBox _onexTabsOpen;
  private readonly Dictionary<string,OnexTabRow> _onexTabRows=new Dictionary<string,OnexTabRow>();
  private static bool OnexRequiredTab(string id)=>id=="Performance"||id=="System";
  private static string OnexTabName(string id){int n=Array.IndexOf(OnexTabIds,id);return n<0?id:OnexStrings.Get(OnexTabNames[n]);}
  private string OnexTabsActiveKey=>_onexTabsQaStore?OnexTabsQaKey:OnexTabsKey;
  private string ReadOnexTabValue(string key)=>ApplicationData.Current.LocalSettings.Values.TryGetValue(key,out var value)?value as string:null;
  private OnexTabsPreference LoadOnexTabs()
  {
   var result=new OnexTabsPreference();var raw=ReadOnexTabValue(OnexTabsActiveKey);
   if(!JsonObject.TryParse(raw??"",out var json))return result;
   var order=json.GetNamedValue("order",JsonValue.CreateNullValue());
   if(order.ValueType==JsonValueType.Array)
   {
    var list=order.GetArray().Where(x=>x.ValueType==JsonValueType.String).Select(x=>x.GetString()).Where(x=>OnexTabIds.Contains(x)&&!OnexRequiredTab(x)).Distinct().ToList();
    foreach(var id in OnexTabIds.Where(x=>!OnexRequiredTab(x)&&x!="Lighting"))if(!list.Contains(id))list.Add(id);
    if(!list.Contains("Lighting"))list.Insert(list.IndexOf("Fan")+1,"Lighting");
    result.Order.Clear();result.Order.Add("Performance");result.Order.AddRange(list);result.Order.Add("System");
   }
   var hidden=json.GetNamedValue("hidden",JsonValue.CreateNullValue());if(hidden.ValueType==JsonValueType.Array)foreach(var item in hidden.GetArray().Where(x=>x.ValueType==JsonValueType.String)){string id=item.GetString();if(OnexTabIds.Contains(id)&&!OnexRequiredTab(id))result.Hidden.Add(id);}
   var open=json.GetNamedValue("open",JsonValue.CreateNullValue());if(open.ValueType==JsonValueType.String&&OnexTabIds.Contains(open.GetString())&&!result.Hidden.Contains(open.GetString()))result.Open=open.GetString();return result;
  }
  private RadioButton[] OnexOrderedNavigation(OnexTabsPreference preferences)
  {
   var all=new[]{PerformanceNavItem,_releasePowerNav,LegionNavItem,_onexKeyboardMouseNav,DisplayNavItem,_releaseOsdNav,FanNavItem,_onexLightingNav,SystemNavItem};
   return preferences.Order.Select(id=>all.FirstOrDefault(item=>item?.Tag as string==id)).Where(item=>item!=null).ToArray();
  }
  private void SaveOnexTabs(OnexTabsPreference preferences)
  {
   ApplicationData.Current.LocalSettings.Values[OnexTabsActiveKey]=preferences.Serialize();
   EnsureOnexReleaseLayout();RefreshOnexTabSettings();
  }
  private bool ApplyOnexDefaultTab()
  {
   if(RestoreOnexControllerDiagnosticReturn())return true;
   if(_releaseHome==null)return false;var preferences=LoadOnexTabs();var target=OnexOrderedNavigation(preferences).FirstOrDefault(item=>item.Tag as string==preferences.Open&&item.Visibility==Visibility.Visible);
   if(target==null)return false;if(target.IsChecked!=true)target.IsChecked=true;return true;
  }
  private void RevealOnexRequestedTab(RadioButton tab)
  {
   if(_releaseLayoutUpdating||_releaseHome==null||!OnexTabIds.Contains(tab.Tag as string))return;
   var preferences=LoadOnexTabs();if(preferences.Hidden.Remove(tab.Tag as string)){tab.Visibility=Visibility.Visible;SaveOnexTabs(preferences);}
  }
  private Button CreateOnexTabMove(string id,bool up)
  {
   var button=new Button{Name="OnexTab"+(up?"Up":"Down")+id,Content=new FontIcon{Glyph=up?"\uE70E":"\uE70D",FontSize=13},Width=36,Height=36,MinWidth=0,MinHeight=0,Padding=new Thickness(0),CornerRadius=new CornerRadius(7)};
   Windows.UI.Xaml.Automation.AutomationProperties.SetName(button,(up?global::XboxGamingBar.OnexStrings.Get("Move up: "):global::XboxGamingBar.OnexStrings.Get("Move down: "))+OnexTabName(id));ToolTipService.SetToolTip(button,up?global::XboxGamingBar.OnexStrings.Get("Move up"):global::XboxGamingBar.OnexStrings.Get("Move down"));OnexInputGuard.Wire(button);
   button.Click+=(s,e)=>{var preferences=LoadOnexTabs();int index=preferences.Order.IndexOf(id),next=index+(up?-1:1);if(OnexRequiredTab(id)||next<1||next>=preferences.Order.Count-1)return;preferences.Order.RemoveAt(index);preferences.Order.Insert(next,id);SaveOnexTabs(preferences);var row=_onexTabRows[id];(button.IsEnabled?(Control)button:row.Visible).Focus(FocusState.Keyboard);};return button;
  }
  private void EnsureOnexTabSettings()
  {
   if(!(SystemScrollViewer.Content is Panel host))return;
   if(_onexTabsCard==null)
   {
    OnexLegacyTabSettingsCard.Visibility=Visibility.Collapsed;
    var content=new StackPanel{Spacing=0};_onexTabsHeader=new ToggleButton{Name="OnexTabsHeader",Content=global::XboxGamingBar.OnexStrings.Get("Tabs"),HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Left,MinHeight=42};OnexInputGuard.Wire(_onexTabsHeader);content.Children.Add(_onexTabsHeader);
    _onexTabsBody=new StackPanel{Spacing=12,Margin=new Thickness(0,12,0,0),Visibility=Visibility.Collapsed};content.Children.Add(_onexTabsBody);
    _onexTabsBody.Children.Add(new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("Show the tabs you use and arrange their order. Home stays first and Settings stays last. Hidden tabs are skipped by LT / RT."),TextWrapping=TextWrapping.Wrap,FontSize=13,Foreground=tileTextBrush});
    _onexTabsRows=new StackPanel{Spacing=6};_onexTabsBody.Children.Add(_onexTabsRows);
    foreach(string tag in OnexTabIds)
    {
     string id=tag;var row=new Grid{ColumnSpacing=6,MinHeight=42};row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
     var visible=new CheckBox{Name="OnexTabVisible"+id,Content=OnexTabName(id)+(OnexRequiredTab(id)?global::XboxGamingBar.OnexStrings.Get(" · always shown"):""),IsEnabled=!OnexRequiredTab(id),VerticalAlignment=VerticalAlignment.Center,Foreground=tileTextBrush};OnexInputGuard.Wire(visible);row.Children.Add(visible);
     var up=CreateOnexTabMove(id,true);var down=CreateOnexTabMove(id,false);Grid.SetColumn(up,1);Grid.SetColumn(down,2);row.Children.Add(up);row.Children.Add(down);_onexTabRows[id]=new OnexTabRow{Root=row,Visible=visible,Up=up,Down=down};
     RoutedEventHandler change=(s,e)=>{if(_onexTabsUiLoading||OnexRequiredTab(id))return;var preferences=LoadOnexTabs();if(visible.IsChecked==true)preferences.Hidden.Remove(id);else{preferences.Hidden.Add(id);if(preferences.Open==id)preferences.Open="";}SaveOnexTabs(preferences);};visible.Checked+=change;visible.Unchecked+=change;
    }
    _onexTabsOpen=new ComboBox{Name="OnexTabOnOpen",Header=global::XboxGamingBar.OnexStrings.Get("When the widget opens"),HorizontalAlignment=HorizontalAlignment.Stretch,MinHeight=38};OnexInputGuard.Wire(_onexTabsOpen,null);_onexTabsOpen.SelectionChanged+=(s,e)=>{
     if(_onexTabsUiLoading||!(_onexTabsOpen.SelectedItem is ComboBoxItem option))return;
     // The open-tab preference does not change navigation order/visibility.
     // Never rebuild this ComboBox or its owner tree inside SelectionChanged.
     if(!(option.Tag is string selected))return;
     var preferences=LoadOnexTabs();
     if(selected.Length!=0&&(!preferences.Order.Contains(selected)||preferences.Hidden.Contains(selected)))return;
     preferences.Open=selected;
     ApplicationData.Current.LocalSettings.Values[OnexTabsActiveKey]=preferences.Serialize();
     _onexTabsUiVersion=OnexTabsActiveKey+preferences.Serialize();
    };
    _onexTabsOpen.DropDownClosed+=async(s,e)=>{
     if(!_onexTabsOpenRefreshPending)return;
     await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low,()=>{
      if(!_onexTabsOpenRefreshPending||_onexTabsOpen.IsDropDownOpen)return;
      RefreshOnexTabSettings();
     });
    };_onexTabsBody.Children.Add(_onexTabsOpen);
    _onexTabsRestore=new Button{Name="OnexTabsRestoreDefault",Content=global::XboxGamingBar.OnexStrings.Get("Restore Default"),HorizontalAlignment=HorizontalAlignment.Right};OnexInputGuard.Wire(_onexTabsRestore);_onexTabsRestore.Click+=(s,e)=>{SaveOnexTabs(new OnexTabsPreference());_onexTabsRestore.Focus(FocusState.Keyboard);};_onexTabsBody.Children.Add(_onexTabsRestore);
    _onexTabsHeader.Checked+=(s,e)=>{_onexTabsBody.Visibility=Visibility.Visible;UpdateOnexTabsHeader();};_onexTabsHeader.Unchecked+=(s,e)=>{_onexTabsBody.Visibility=Visibility.Collapsed;UpdateOnexTabsHeader();};
    UpdateOnexTabsHeader();_onexTabsCard=OnexSettingsCard(content);_onexTabsCard.Name="OnexTabsConfiguration";host.Children.Insert(Math.Min(1,host.Children.Count),_onexTabsCard);
   }
   RefreshOnexTabSettings();if(SystemScrollViewer.Visibility==Visibility.Visible)ApplyOnexSettingsDesign();
  }
  private void RefreshOnexTabSettings()
  {
   if(_onexTabsCard==null)return;var preferences=LoadOnexTabs();string version=OnexTabsActiveKey+preferences.Serialize();if(version==_onexTabsUiVersion&&(!_onexTabsOpenRefreshPending||_onexTabsOpen.IsDropDownOpen))return;
   _onexTabsUiVersion=version;_onexTabsUiLoading=true;
   try
   {
    var desired=preferences.Order.Select(id=>_onexTabRows[id].Root).ToArray();if(!_onexTabsRows.Children.SequenceEqual(desired)){_onexTabsRows.Children.Clear();foreach(var row in desired)_onexTabsRows.Children.Add(row);}
    for(int n=0;n<preferences.Order.Count;n++){string id=preferences.Order[n];var row=_onexTabRows[id];row.Visible.IsChecked=!preferences.Hidden.Contains(id);row.Up.IsEnabled=!OnexRequiredTab(id)&&n>1;row.Down.IsEnabled=!OnexRequiredTab(id)&&n<preferences.Order.Count-2;}
    if(_onexTabsOpen.IsDropDownOpen){_onexTabsOpenRefreshPending=true;return;}
    var choices=new[]{""}.Concat(preferences.Order.Where(id=>!preferences.Hidden.Contains(id))).ToArray();
    if(!_onexTabsOpen.Items.OfType<ComboBoxItem>().Select(item=>item.Tag as string).SequenceEqual(choices)){
     _onexTabsOpen.Items.Clear();
     foreach(string id in choices)_onexTabsOpen.Items.Add(new ComboBoxItem{Tag=id,Content=id==""?OnexStrings.Get("Last used tab"):OnexTabName(id)});
    }
    var selected=_onexTabsOpen.Items.OfType<ComboBoxItem>().First(item=>item.Tag as string==preferences.Open);
    if(!ReferenceEquals(_onexTabsOpen.SelectedItem,selected))_onexTabsOpen.SelectedItem=selected;
    _onexTabsOpenRefreshPending=false;
   }
   finally{_onexTabsUiLoading=false;}
  }
 }
}
