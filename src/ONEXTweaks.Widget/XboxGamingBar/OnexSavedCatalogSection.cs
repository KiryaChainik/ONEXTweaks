// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
namespace XboxGamingBar
{
 public sealed partial class GamingWidget
 {
  private sealed class OnexSavedCatalogSection
  {
   internal sealed class Row
   {internal string Id,KindKey;internal bool Editable,Active,Editing;internal TextBlock Name,Kind;internal Button Choose,Rename,Remove;internal Border Card;internal TextBlock Details;}
   private readonly GamingWidget owner;private readonly string scope;private readonly Func<JsonObject> state;private readonly Func<JsonObject,Task> change;private readonly Func<JsonObject,string> describe;
   internal readonly Dictionary<string,Row> Rows=new Dictionary<string,Row>();internal readonly List<string> Ids=new List<string>();
   internal Border Card;internal Button Header;internal ComboBox Sort;internal StackPanel Body,List;private TextBlock title;private FontIcon arrow;private string signature;private bool busy;private Brush accent;private readonly bool selectionOnly;private readonly bool presets;
   internal OnexSavedCatalogSection(GamingWidget owner,string scope,Func<JsonObject> state,Func<JsonObject,Task> change,Func<JsonObject,string> describe,bool selectionOnly=false)
   {
    this.owner=owner;this.scope=scope;this.state=state;this.change=change;this.describe=describe;this.selectionOnly=selectionOnly;presets=scope=="OverlayPreset"||scope=="OverlayPresetTile";
    var root=new StackPanel{Spacing=12};title=new TextBlock{Text=presets?global::XboxGamingBar.OnexStrings.Get("Saved Presets"):global::XboxGamingBar.OnexStrings.Get("Saved Profiles"),FontSize=18,FontWeight=Windows.UI.Text.FontWeights.SemiBold,Foreground=new SolidColorBrush(Windows.UI.Colors.White),VerticalAlignment=VerticalAlignment.Center};arrow=new FontIcon{Glyph="\uE70D",FontSize=13,HorizontalAlignment=HorizontalAlignment.Right};var caption=new Grid{ColumnSpacing=10};caption.ColumnDefinitions.Add(new ColumnDefinition());caption.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});caption.Children.Add(title);Grid.SetColumn(arrow,1);caption.Children.Add(arrow);
    Header=new Button{Name="Onex"+scope+"SavedExpand",Content=caption,Style=owner.OnexSettingsButtonStyle(false),HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch,BorderThickness=new Thickness(0),Background=new SolidColorBrush(Windows.UI.Colors.Transparent),Padding=new Thickness(12,10,12,10)};root.Children.Add(Header);
    Body=new StackPanel{Spacing=12,Visibility=Visibility.Collapsed};root.Children.Add(Body);Sort=owner.OnexSettingsChoice(global::XboxGamingBar.OnexStrings.Get("Sort by"));Sort.Name="Onex"+scope+"SavedSort";foreach(string mode in new[]{"Name (A–Z)","Recently updated","Oldest first"})Sort.Items.Add(OnexStrings.Get(mode));Sort.SelectedIndex=0;OnexInputGuard.Wire(Sort);Sort.SelectionChanged+=(s,e)=>Apply(true);Body.Children.Add(Sort);List=new StackPanel{Spacing=10};Body.Children.Add(List);
    Header.Click+=(s,e)=>SetExpanded(Body.Visibility!=Visibility.Visible);Card=owner.OnexSettingsCard(root);Card.Name="Onex"+scope+"SavedProfiles";Card.BorderThickness=new Thickness(1);
   }
   private static string Text(JsonObject value,string key,string fallback="")
   {
    return value!=null&&value.TryGetValue(key,out var field)&&field.ValueType==JsonValueType.String?field.GetString():fallback;
   }
   internal int EntryCount=>state()?.GetNamedArray("entries",null)?.Count??0;
   internal void SetExpanded(bool open)
   {
    if(!open&&FocusRows().Skip(1).SelectMany(row=>row).Contains(FocusManager.GetFocusedElement() as Control))Header.Focus(FocusState.Keyboard);
    Body.Visibility=open?Visibility.Visible:Visibility.Collapsed;arrow.Glyph=open?"\uE70E":"\uE70D";Apply();if(!open)owner.OnexSavedCatalogNavigationChanged(this);
   }
   internal IEnumerable<Control[]> FocusRows()
   {
    yield return new Control[]{Header};if(Body.Visibility!=Visibility.Visible)yield break;
    yield return new Control[]{Sort};foreach(string id in Ids)if(Rows.TryGetValue(id,out var row))yield return new Control[]{row.Choose,row.Rename,row.Remove}.Where(control=>control.IsEnabled&&control.IsTabStop&&control.Visibility==Visibility.Visible).ToArray();
   }
   private static bool Editable(string id)=>id!="global"&&!id.StartsWith("firmware-",StringComparison.Ordinal);
   private int Rank(JsonObject entry){if(presets)return OnexOverlayPresetRank(entry);string id=Text(entry,"id");if(scope=="FanTile")return OnexFanTileRank(id);return id=="global"?0:id.StartsWith("firmware-",StringComparison.Ordinal)?1:selectionOnly&&Text(entry,"processPath",Text(entry,"path"))!=""?3:2;}
   internal void Accent(Brush accent){this.accent=accent;Card.BorderBrush=accent;Enabled();}
   private void Enabled()
   {
    var current=state();foreach(var row in Rows.Values)
    {
     row.Active=row.Id==Text(current,"appliedId",Text(current,"activeId"));row.Editing=row.Id==Text(current,"activeId");
     string kind=OnexStrings.Get(row.KindKey)+OnexStrings.Get(row.Active?" · ✓ Active":row.Editing?" · Editing":"");if(row.Kind.Text!=kind)row.Kind.Text=kind;
     row.Choose.IsEnabled=App.IsConnected&&!busy&&(!selectionOnly||!row.Active);row.Rename.IsEnabled=row.Remove.IsEnabled=App.IsConnected&&!busy&&row.Editable;
     owner.PaintOnexProfileChoiceRow(row.Choose,row.Name,row.Kind,row.Details,row.Card,row.Active,row.Editing,accent);
    }
   }
   internal void Apply(bool force=false)
   {
    var value=state();if(value==null){Enabled();return;}
    title.Text=OnexStrings.Format(presets?"Saved Presets · {0}":"Saved Profiles · {0}",value.GetNamedArray("entries").Count);
    if(Body.Visibility!=Visibility.Visible){signature=null;Rows.Clear();Ids.Clear();List.Children.Clear();return;}
    string current=value.GetNamedArray("entries").Stringify()+"|"+Sort.SelectedIndex+"|"+owner.currentThemeName+"|"+owner._onexThemeEpoch+"|"+owner._onexUiFontEpoch+"|"+OnexStrings.Language;
    if(current==signature)
    {
     // Forced repaint is not a request to recreate identical rows. Sorting
     // already changed the signature in SelectionChanged. Keep focus and avoid
     // another Style/Control graph when the caller also requests a repaint.
     if(force)foreach(var item in value.GetNamedArray("entries")){var entry=item.GetObject();if(Rows.TryGetValue(Text(entry,"id"),out var row)){string details=Summary(entry);if(row.Details.Text!=details)row.Details.Text=details;}}
     Enabled();return;
    }
    var focused=FocusManager.GetFocusedElement() as Control;var prior=Rows.Values.FirstOrDefault(row=>ReferenceEquals(row.Choose,focused)||ReferenceEquals(row.Rename,focused)||ReferenceEquals(row.Remove,focused));
    int role=prior==null?0:ReferenceEquals(prior.Rename,focused)?1:ReferenceEquals(prior.Remove,focused)?2:0;var focusState=focused?.FocusState??FocusState.Unfocused;
    signature=current;Rows.Clear();Ids.Clear();List.Children.Clear();
    var entries=value.GetNamedArray("entries").Select(item=>item.GetObject()).ToArray();IOrderedEnumerable<JsonObject> ordered=entries.OrderBy(entry=>Rank(entry));
    ordered=Sort.SelectedIndex==1?ordered.ThenByDescending(entry=>Text(entry,"modifiedUtc")).ThenBy(entry=>OnexCatalogEntryName(entry,presets),StringComparer.CurrentCultureIgnoreCase):Sort.SelectedIndex==2?ordered.ThenBy(entry=>Text(entry,"modifiedUtc")).ThenBy(entry=>OnexCatalogEntryName(entry,presets),StringComparer.CurrentCultureIgnoreCase):ordered.ThenBy(entry=>OnexCatalogEntryName(entry,presets),StringComparer.CurrentCultureIgnoreCase);
    var rowStyle=owner.OnexSettingsButtonStyle(false);
    int previous=-1;foreach(var entry in ordered)
    {
     string id=Text(entry,"id"),name=OnexCatalogEntryName(entry,presets);int group=Rank(entry);
     if(previous>=0&&previous!=group)List.Children.Add(new Border{Height=1,Background=(Brush)owner.Resources["CardBorderBrush"],Margin=new Thickness(0,3,0,3)});previous=group;
     bool active=id==Text(value,"appliedId",Text(value,"activeId")),editing=id==Text(value,"activeId");string kind=id=="global"?"Global":id.StartsWith("firmware-",StringComparison.Ordinal)?"System preset":Text(entry,"performanceProfileId")!=""?"Performance-linked":Text(entry,"processPath",Text(entry,"path"))!=""?"Per-game / Per-app":"Manual";
     var row=new Row{Id=id,KindKey=kind,Editable=Editable(id),Active=active,Editing=editing};row.Name=new TextBlock{Text=name,FontSize=16,FontWeight=Windows.UI.Text.FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap};row.Kind=new TextBlock{Text=OnexStrings.Get(kind)+OnexStrings.Get(active?" · ✓ Active":editing?" · Editing":""),FontSize=12,TextWrapping=TextWrapping.Wrap};var heading=new StackPanel{Spacing=3};heading.Children.Add(row.Name);heading.Children.Add(row.Kind);
     row.Choose=new Button{Name="Onex"+scope+"SavedChoose_"+id,Tag=id,Content=heading,Style=rowStyle,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch};row.Choose.Click+=async(s,e)=>await Run(()=>change(new JsonObject{{"operation",JsonValue.CreateStringValue("select")},{"id",JsonValue.CreateStringValue(id)}}));
     row.Rename=new Button{Name="Onex"+scope+"SavedRename_"+id,Content=new FontIcon{Glyph="\uE70F",FontSize=16},Width=36,Height=36,MinHeight=36,Padding=new Thickness(0),Style=rowStyle};row.Remove=new Button{Name="Onex"+scope+"SavedDelete_"+id,Content="×",Width=36,Height=36,MinHeight=36,Padding=new Thickness(0),Style=rowStyle};
     row.Rename.Click+=async(s,e)=>await Rename(entry);row.Remove.Click+=async(s,e)=>await Delete(entry);ToolTipService.SetToolTip(row.Rename,global::XboxGamingBar.OnexStrings.Get("Переименовать профиль"));ToolTipService.SetToolTip(row.Remove,global::XboxGamingBar.OnexStrings.Get("Удалить с подтверждением"));
     var actions=new Grid{ColumnSpacing=8};actions.ColumnDefinitions.Add(new ColumnDefinition());actions.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});actions.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});actions.Children.Add(row.Choose);Grid.SetColumn(row.Rename,1);actions.Children.Add(row.Rename);if(selectionOnly)row.Rename.Visibility=Visibility.Collapsed;Grid.SetColumn(row.Remove,2);actions.Children.Add(row.Remove);if(selectionOnly)row.Remove.Visibility=Visibility.Collapsed;
     row.Details=new TextBlock{Text=Summary(entry),FontSize=12,TextWrapping=TextWrapping.Wrap};var content=new StackPanel{Spacing=8};content.Children.Add(actions);content.Children.Add(row.Details);row.Card=owner.OnexSettingsCard(content);row.Card.BorderThickness=new Thickness(1);row.Card.BorderBrush=(Brush)owner.Resources["CardBorderBrush"];List.Children.Add(row.Card);Rows.Add(id,row);Ids.Add(id);
    }
    title.Text=OnexStrings.Format(presets?"Saved Presets · {0}":"Saved Profiles · {0}",entries.Length);Enabled();owner.OnexSavedCatalogNavigationChanged(this);
    if(prior!=null&&Rows.TryGetValue(prior.Id,out var replacement))
    {var target=role==1?replacement.Rename:role==2?replacement.Remove:replacement.Choose;if(target.IsEnabled)
     {
      var restoreState=focusState==FocusState.Unfocused?FocusState.Programmatic:focusState;
      if(!target.Focus(restoreState))
      {
       string restoreSignature=signature;
       _=owner.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,()=>
       {
        var now=FocusManager.GetFocusedElement() as Control;
        if(signature==restoreSignature&&Body.Visibility==Visibility.Visible&&target.IsLoaded&&target.IsEnabled&&(now==null||ReferenceEquals(now,focused)||!now.IsLoaded))target.Focus(restoreState);
       });
      }
     }}

   }
   private string Summary(JsonObject entry)
   {
    var dc=(scope=="Controller"||scope=="ControllerTile")?entry.GetNamedObject("effectiveDc",entry.GetNamedObject("dc",null)):entry.GetNamedObject("dc",null);var ac=(scope=="Controller"||scope=="ControllerTile")?entry.GetNamedObject("effectiveAc",entry.GetNamedObject("ac",null)):entry.GetNamedObject("ac",null);if(dc==null)return "—";
    return entry.GetNamedBoolean("split",false)&&ac!=null?global::XboxGamingBar.OnexStrings.Format("DC · {0}\nAC · {1}",describe(dc),describe(ac)):describe(dc);
   }
   private async Task Run(Func<Task> action)
   {
    if(busy||!App.IsConnected)return;busy=true;Enabled();try{await action();}catch(Exception error){await owner.ShowOnexProfileOperationErrorAsync(error.Message);}finally{busy=false;Apply();}
   }
   private async Task Rename(JsonObject entry)
   {
    if(selectionOnly)return;
    if(!Editable(Text(entry,"id"))||busy)return;string name=await owner.PromptOnexProfileNameAsync(Text(entry,"name"),presets,OnexProfileNameLimit(scope));if(name!=null)await RenameAsync(Text(entry,"id"),name);
   }
   internal Task RenameAsync(string id,string name)=>Run(()=>change(new JsonObject{{"operation",JsonValue.CreateStringValue("rename")},{"id",JsonValue.CreateStringValue(id)},{"name",JsonValue.CreateStringValue(name)}}));
   private async Task Delete(JsonObject entry)
   {
    string id=Text(entry,"id");if(selectionOnly||!Editable(id)||busy||owner._onexDetailsDialog!=null)return;
    var dialog=owner.CreateOnexSettingsDialog(presets?global::XboxGamingBar.OnexStrings.Get("Delete Preset?"):global::XboxGamingBar.OnexStrings.Get("Delete Profile?"),presets?global::XboxGamingBar.OnexStrings.Get("Настройки этого пресета будут удалены."):global::XboxGamingBar.OnexStrings.Get("Настройки этого профиля будут удалены."),new TextBlock{Text=Text(entry,"name"),TextWrapping=TextWrapping.Wrap});dialog.PrimaryButtonText=global::XboxGamingBar.OnexStrings.Get("Delete");dialog.CloseButtonText=global::XboxGamingBar.OnexStrings.Get("Keep It");dialog.DefaultButton=ContentDialogButton.Close;owner._onexDetailsDialog=dialog;ContentDialogResult result;
    try{result=await dialog.ShowAsync();}finally{if(ReferenceEquals(owner._onexDetailsDialog,dialog))owner._onexDetailsDialog=null;}
    if(result==ContentDialogResult.Primary)await DeleteAsync(id);
   }
   internal Task DeleteAsync(string id)=>Run(()=>change(new JsonObject{{"operation",JsonValue.CreateStringValue("delete")},{"id",JsonValue.CreateStringValue(id)}}));
  }
  private void OnexSavedCatalogNavigationChanged(OnexSavedCatalogSection section)
  {
   if(ReferenceEquals(section,_onexControllerSavedProfiles))WireOnexControllerNavigation();
   else if(ReferenceEquals(section,_onexScreenSavedProfiles))WireOnexDisplayNavigation();
   else if(ReferenceEquals(section,_onexFanSavedProfiles))WireOnexFanNavigation();
  }
  private OnexSavedCatalogSection _onexFanSavedProfiles,_onexScreenSavedProfiles;
 }
}
