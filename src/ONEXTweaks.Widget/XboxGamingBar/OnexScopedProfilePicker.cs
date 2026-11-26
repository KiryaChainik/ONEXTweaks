// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace XboxGamingBar
{
    // Shared Choose/Create/Keep/Delete/AC-DC UI for Display, Performance and Fan.
    internal sealed class OnexScopedProfilePicker
    {
        internal StackPanel Root{get;}=new StackPanel{Spacing=8};
        internal StackPanel Footer{get;}=new StackPanel{Spacing=8};
        internal TextBlock Description{get;}=new TextBlock{FontSize=12,Opacity=1,TextWrapping=TextWrapping.Wrap};
        internal Button Choose{get;}
        internal Button Create{get;}
        internal Button Remove{get;}
        internal Button Rename{get;}
        internal Func<string,string,Task> RenameRequested{get;set;}
        internal bool IncludeRunningProcesses{get;set;}
        internal Task RenameAsync(string id,string name)=>Change(new JsonObject{{"operation",JsonValue.CreateStringValue("rename")},{"id",JsonValue.CreateStringValue(id)},{"name",JsonValue.CreateStringValue(name)}});
        internal ToggleSwitch Split{get;}
        private readonly TextBlock title,summary,error,source;
        private readonly Func<Task<JsonObject>> refresh;
        private readonly Func<JsonObject,Task> mutate;
        private readonly Func<MenuFlyout> menu;
        private readonly Func<JsonObject,string> describe;
        private JsonObject state;
        private MenuFlyout removeMenu;
        private bool syncing,busy;
        internal string DiagnosticError=>error.Text;
        internal void ApplyAccent(Brush accent,Brush primary=null,Brush secondary=null){title.Foreground=primary??new SolidColorBrush(Windows.UI.Colors.White);summary.Foreground=secondary??new SolidColorBrush(Windows.UI.Color.FromArgb(255,180,186,195));Description.Foreground=summary.Foreground;Description.Opacity=summary.Opacity=1;foreach(var control in new Control[]{Choose,Create,Rename,Remove})control.BorderBrush=accent;foreach(string key in new[]{"ToggleSwitchFillOn","ToggleSwitchStrokeOn"}){if(Split.Resources.TryGetValue(key,out var existing)&&existing is SolidColorBrush current&&accent is SolidColorBrush desired)current.Color=desired.Color;else Split.Resources[key]=accent is SolidColorBrush brush?new SolidColorBrush(brush.Color):accent;}}
        private static string Text(JsonObject value,string key,string fallback="")=>value!=null&&value.TryGetValue(key,out var field)&&field.ValueType==JsonValueType.String?field.GetString():fallback;
        internal OnexScopedProfilePicker(Func<Task<JsonObject>> refresh,Func<JsonObject,Task> mutate,Func<MenuFlyout> menu,Func<JsonObject,string> describe,Style buttonStyle,Brush foreground,Action<Control> decorate)
        {
            this.refresh=refresh;this.mutate=mutate;this.menu=menu;this.describe=describe;
            Choose=new Button{Name="OnexChooseProfile",Content=global::XboxGamingBar.OnexStrings.Get("Choose Profile"),Style=buttonStyle,HorizontalAlignment=HorizontalAlignment.Stretch};
            Create=new Button{Name="OnexCreateProfile",Content=global::XboxGamingBar.OnexStrings.Get("+ Create Profile"),Style=buttonStyle,HorizontalAlignment=HorizontalAlignment.Stretch};
            var row=new Grid{ColumnSpacing=10};row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition());row.Children.Add(Choose);Grid.SetColumn(Create,1);row.Children.Add(Create);Root.Children.Add(row);
            var active=new Grid{ColumnSpacing=8};active.ColumnDefinitions.Add(new ColumnDefinition());active.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});active.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            title=new TextBlock{Text=global::XboxGamingBar.OnexStrings.Get("Global Profile"),FontSize=16,FontWeight=Windows.UI.Text.FontWeights.SemiBold,Foreground=foreground,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center};active.Children.Add(title);
            Rename=new Button{Name="OnexRenameProfile",Content=new FontIcon{Glyph="\uE70F",FontSize=15},Width=36,Height=34,Padding=new Thickness(0),Style=buttonStyle};Grid.SetColumn(Rename,1);active.Children.Add(Rename);
            Rename.Click+=async(a,b)=>{if(!Ready||RenameRequested==null)return;var entry=state.GetNamedArray("entries").Select(value=>value.GetObject()).First(value=>Text(value,"id")==Text(state,"activeId"));await RenameRequested(Text(entry,"id"),Text(entry,"name"));};
            ToolTipService.SetToolTip(Rename,global::XboxGamingBar.OnexStrings.Get("Переименовать профиль; настройки и привязки сохранятся."));
            Remove=new Button{Name="OnexRemoveProfile",Content="×",Width=36,Height=34,Padding=new Thickness(0),Style=buttonStyle};Grid.SetColumn(Remove,2);active.Children.Add(Remove);Root.Children.Add(active);
            summary=new TextBlock{FontSize=12,TextWrapping=TextWrapping.Wrap,Foreground=foreground,Opacity=1};Root.Children.Add(summary);
            var splitRow=new Grid{ColumnSpacing=8};splitRow.ColumnDefinitions.Add(new ColumnDefinition());splitRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            source=new TextBlock{FontSize=12,Foreground=foreground,VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap};splitRow.Children.Add(source);
            Split=new ToggleSwitch{Name="OnexScreenProfileSplit",OnContent="",OffContent="",MinWidth=0};OnexInputGuard.Wire(Split);decorate(Split);Grid.SetColumn(Split,1);splitRow.Children.Add(Split);Footer.Children.Add(splitRow);Footer.Children.Add(Description);
            error=new TextBlock{FontSize=12,TextWrapping=TextWrapping.Wrap,Foreground=foreground,Visibility=Visibility.Collapsed};Root.Children.Add(error);
            Choose.Click+=async(s,e)=>await OpenChooseAsync();Create.Click+=async(s,e)=>await OpenCreateAsync();
            Remove.Click+=(s,e)=>
            {
                if(!Ready||Immutable)return;
                string profileId=Text(state,"activeId");
                var popup=menu();
                removeMenu=popup;
                popup.Closed+=(a,b)=>{if(ReferenceEquals(removeMenu,popup))removeMenu=null;};
                foreach(string operation in new[]{"delete","keep"})
                {
                    var item=new MenuFlyoutItem{Text=OnexStrings.Get(operation=="delete"?"Delete":"Keep It")};
                    item.Click+=async(a,b)=>
                    {
                        if(profileId!=Text(state,"activeId"))return;
                        await Change(new JsonObject{{"operation",JsonValue.CreateStringValue(operation)},{"id",JsonValue.CreateStringValue(profileId)}});
                    };
                    popup.Items.Add(item);
                }
                popup.ShowAt(Remove);
            };
            Split.Toggled+=async(s,e)=>{if(!syncing&&OnexInputGuard.ConsumeToggleInput(Split))await Change(new JsonObject{{"operation",JsonValue.CreateStringValue("split")},{"enabled",JsonValue.CreateBooleanValue(Split.IsOn)}});};
            ToolTipService.SetToolTip(Remove,global::XboxGamingBar.OnexStrings.Get("Delete удаляет профиль. Keep It сохраняет его в списке и возвращает Global."));
            ToolTipService.SetToolTip(Split,global::XboxGamingBar.OnexStrings.Get("Отдельные значения при зарядке и от батареи. Изменения сохраняются для текущего источника питания."));
            UpdateEnabled();
        }
        private bool IsPreset=>state?.GetNamedBoolean("presetCatalog",false)==true;
        private string GlobalTitle=>OnexStrings.Get(IsPreset?"Default Preset":"Global Profile");
        private bool Immutable=>Text(state,"activeId")=="global"||Text(state,"activeId").StartsWith("firmware-",StringComparison.Ordinal);
        private bool Ready=>App.IsConnected&&state!=null&&!busy;
        internal void Apply(JsonObject value)
        {
            if(Text(state,"activeId")!=Text(value,"activeId"))removeMenu?.Hide();
            state=value;syncing=true;Choose.Content=IsPreset?global::XboxGamingBar.OnexStrings.Get("Choose Preset"):global::XboxGamingBar.OnexStrings.Get("Choose Profile");Create.Content=IsPreset?global::XboxGamingBar.OnexStrings.Get("+ Create Preset"):global::XboxGamingBar.OnexStrings.Get("+ Create Profile");
            try
            {
                var entry=state?.GetNamedArray("entries",null)?.Select(item=>item.GetObject()).FirstOrDefault(item=>Text(item,"id")==Text(state,"activeId"));
                title.Text=Text(entry,"id")=="global"?GlobalTitle:Text(entry,"id").StartsWith("firmware-",StringComparison.Ordinal)?OnexStrings.Get(Text(entry,"name")):Text(entry,"name",GlobalTitle);bool ac=state?.GetNamedBoolean("onAc",false)==true;
                Split.IsOn=entry?.GetNamedBoolean("split",false)==true;source.Text=global::XboxGamingBar.OnexStrings.Format("AC/DC отдельно · сейчас {0}{1}",(ac?global::XboxGamingBar.OnexStrings.Get("зарядка"):global::XboxGamingBar.OnexStrings.Get("батарея")),(state?.GetNamedBoolean("manualOverride",false)==true?global::XboxGamingBar.OnexStrings.Get(" · ручной выбор до перезапуска приложения"):""));
                summary.Text=entry==null?"":describe(entry.GetNamedObject(ac&&entry.GetNamedBoolean("split",false)?"effectiveAc":"effectiveDc",entry.GetNamedObject(ac&&entry.GetNamedBoolean("split",false)?"ac":"dc")));
                string message=Text(state,"error");error.Text=OnexStrings.Get(message);error.Visibility=string.IsNullOrEmpty(message)?Visibility.Collapsed:Visibility.Visible;
            }
            finally{syncing=false;UpdateEnabled();}
        }
        private void UpdateEnabled(){Choose.IsEnabled=Create.IsEnabled=Ready;Split.IsEnabled=Ready&&!Text(state,"activeId").StartsWith("firmware-",StringComparison.Ordinal);Rename.IsEnabled=Remove.IsEnabled=Ready&&!Immutable;Rename.Visibility=Immutable?Visibility.Collapsed:Visibility.Visible;}
        private async Task Change(JsonObject request)
        {
            if(!Ready)return;busy=true;UpdateEnabled();
            try{await mutate(request);}
            catch(Exception failure){error.Text=OnexStrings.Get(failure.Message);error.Visibility=Visibility.Visible;}
            finally{busy=false;UpdateEnabled();}
        }
        internal async Task OpenChooseAsync()
        {
            if(!Ready)return;
            try
            {
                Apply(await refresh());var popup=menu();string previousGroup=null;
                foreach(var entry in state.GetNamedArray("entries").Select(item=>item.GetObject()))
                {
                    string id=Text(entry,"id"),group=id=="global"?(IsPreset?"Default":"Global"):IsPreset?"Presets":Text(entry,"processPath")==""?"Manual":Text(state,"linkedGroupName","Per-game / Per-app");
                    if(group!=previousGroup){if(previousGroup!=null)popup.Items.Add(new MenuFlyoutSeparator());if(group!="Global")popup.Items.Add(new MenuFlyoutItem{Text=OnexStrings.Get(group),IsEnabled=false});previousGroup=group;}
                    var item=new MenuFlyoutItem{Text=id=="global"?GlobalTitle:id.StartsWith("firmware-",StringComparison.Ordinal)?OnexStrings.Get(Text(entry,"name")):Text(entry,"name"),Tag=id};if(id==Text(state,"activeId"))item.Icon=new SymbolIcon(Symbol.Accept);
                    item.Click+=async(s,e)=>await Change(new JsonObject{{"operation",JsonValue.CreateStringValue("select")},{"id",JsonValue.CreateStringValue(id)}});popup.Items.Add(item);
                }
                popup.ShowAt(Choose);
            }
            catch(Exception failure){error.Text=OnexStrings.Get(failure.Message);error.Visibility=Visibility.Visible;}
        }
        internal async Task OpenCreateAsync()
        {
            if(!Ready)return;
            try
            {
                Apply(await refresh());var popup=menu();var manual=new MenuFlyoutItem{Text=IsPreset?global::XboxGamingBar.OnexStrings.Get("+ New Preset"):global::XboxGamingBar.OnexStrings.Get("+ Manual Profile"),Tag="manual"};manual.Click+=async(s,e)=>await Change(new JsonObject{{"operation",JsonValue.CreateStringValue("create")}});popup.Items.Add(manual);if(IsPreset){popup.ShowAt(Create);return;}popup.Items.Add(new MenuFlyoutSeparator());
                var parents=state.GetNamedArray("creationParents",null);
                if(parents!=null)
                {
                    if(IncludeRunningProcesses)
                    {
                        var reply=await App.SendMessageAsync(new ValueSet{{"OnexLinkedProcessCandidatesGet",true}});
                        popup.Items.Add(new MenuFlyoutItem{Text=OnexStrings.Get("Running apps"),IsEnabled=false});
                        if(reply!=null&&reply.TryGetValue("OnexLinkedProcessCandidates",out object payload)&&payload is string json)
                        {
                            var running=JsonArray.Parse(json);
                            foreach(var value in running)AddProcess(popup,value.GetObject());
                            if(running.Count==0)popup.Items.Add(new MenuFlyoutItem{Text=OnexStrings.Get("Нет подходящих запущенных приложений"),IsEnabled=false});
                        }
                        else popup.Items.Add(new MenuFlyoutItem{Text=OnexStrings.Get("Running apps unavailable"),IsEnabled=false});
                        popup.Items.Add(new MenuFlyoutSeparator());
                    }
                    popup.Items.Add(new MenuFlyoutItem{Text=global::XboxGamingBar.OnexStrings.Get("Performance Profiles"),IsEnabled=false});
                    foreach(var value in parents){var parent=value.GetObject();string id=Text(parent,"id");var item=new MenuFlyoutItem{Text=id=="global"?OnexStrings.Get("Global Profile"):Text(parent,"name"),Tag=id};item.Click+=async(a,b)=>await Change(new JsonObject{{"operation",JsonValue.CreateStringValue("create")},{"performanceProfileId",JsonValue.CreateStringValue(id)}});popup.Items.Add(item);}
                    if(parents.Count==0)popup.Items.Add(new MenuFlyoutItem{Text=global::XboxGamingBar.OnexStrings.Get("Нет профилей производительности"),IsEnabled=false});popup.ShowAt(Create);return;
                }
                var candidates=state.GetNamedArray("candidates",null);
                if(candidates==null||candidates.Count==0)popup.Items.Add(new MenuFlyoutItem{Text=global::XboxGamingBar.OnexStrings.Get("Нет подходящих запущенных приложений"),IsEnabled=false});
                else foreach(var value in candidates)AddProcess(popup,value.GetObject());
                popup.ShowAt(Create);
            }
            catch(Exception failure){error.Text=OnexStrings.Get(failure.Message);error.Visibility=Visibility.Visible;}
        }
        private void AddProcess(MenuFlyout popup,JsonObject candidate)
        {
            var process=candidate.GetNamedObject("session");
            var item=new MenuFlyoutItem{Text=Text(process,"name")+OnexStrings.Get(candidate.GetNamedBoolean("game")?" · Game":" · App")};
            item.Click+=async(s,e)=>await Change(new JsonObject{{"operation",JsonValue.CreateStringValue("create")},{"processId",process["processId"]},{"startedUtcTicks",process["startedUtcTicks"]}});
            popup.Items.Add(item);
        }
    }
}
