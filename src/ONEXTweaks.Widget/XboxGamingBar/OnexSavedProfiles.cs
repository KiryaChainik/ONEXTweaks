// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        // A protected name/delete action does not make profile selection unavailable.
        // Use primary text for choices; mute only a genuinely disabled choice.
        private void PaintOnexProfileChoiceRow(Button choose,TextBlock name,TextBlock kind,TextBlock details,Border card,bool active,bool editing=false,Windows.UI.Xaml.Media.Brush accent=null)
        {
            var theme=_onexAppliedTheme??OnexPalette.CreateTheme();var muted=OnexSolidBrush(OnexOver(theme.TextPrimary,theme.CardBackground,.5));var primary=OnexSolidBrush(theme.TextPrimary);var secondary=OnexSolidBrush(OnexSettingsCaptionColour(theme));
            var activeInk=OnexSolidBrush(OnexReadableAccent(theme,(accent as Windows.UI.Xaml.Media.SolidColorBrush)?.Color??theme.EffectiveAccent(),true));
            name.Foreground=choose.IsEnabled?primary:muted;name.Opacity=1;
            kind.Foreground=active||editing?activeInk:choose.IsEnabled?secondary:muted;kind.Opacity=1;
            details.Foreground=choose.IsEnabled?secondary:muted;details.Opacity=1;
            card.BorderBrush=active||editing?(accent??OnexDialogAccent):(Windows.UI.Xaml.Media.Brush)Resources["CardBorderBrush"];
            ToolTipService.SetToolTip(choose,choose.IsEnabled?global::XboxGamingBar.OnexStrings.Get("Выбрать профиль"):active?global::XboxGamingBar.OnexStrings.Get("Этот профиль уже активен"):global::XboxGamingBar.OnexStrings.Get("Выбор временно недоступен"));
        }
        private readonly List<Control[]> _onexSavedProfileRows=new List<Control[]>();
        private readonly List<string> _onexSavedProfileIds=new List<string>();
        private string _onexSavedProfilesSignature;
        private TextBox _onexProfileRenameInput;
        private static int OnexProfileNameLimit(string scope)=>scope=="Controller"?80:160;
        private static bool OnexValidProfileName(string value,int maxLength)
        {
            string name=(value??string.Empty).Trim();
            return name.Length>0&&name.Length<=maxLength&&!name.Any(char.IsControl);
        }
        private async Task<string> PromptOnexProfileNameAsync(string current,bool preset=false,int maxNameLength=160)
        {
            if(_onexDetailsDialog!=null)return null;
            var input=new TextBox{Name="OnexProfileRenameInput",Text=current,MaxLength=maxNameLength,Header=preset?global::XboxGamingBar.OnexStrings.Get("Название пресета"):global::XboxGamingBar.OnexStrings.Get("Название профиля"),HorizontalAlignment=HorizontalAlignment.Stretch};
            _onexProfileRenameInput=input;
            var error=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=12,Visibility=Visibility.Collapsed,Foreground=OnexDialogAccent};
            var body=new StackPanel{Spacing=10};body.Children.Add(input);body.Children.Add(error);
            var dialog=CreateOnexSettingsDialog(preset?global::XboxGamingBar.OnexStrings.Get("Rename Preset"):global::XboxGamingBar.OnexStrings.Get("Rename Profile"),global::XboxGamingBar.OnexStrings.Get("Меняется только название. Настройки и связи сохраняются."),body);
            dialog.PrimaryButtonText=global::XboxGamingBar.OnexStrings.Get("Rename");dialog.CloseButtonText=global::XboxGamingBar.OnexStrings.Get("Cancel");dialog.DefaultButton=ContentDialogButton.Primary;
            dialog.PrimaryButtonClick+=(s,e)=>{string name=input.Text.Trim();if(!OnexValidProfileName(name,maxNameLength)){e.Cancel=true;error.Text=name.Length>maxNameLength?string.Format(global::XboxGamingBar.OnexStrings.Get("Название должно содержать не более {0} символов."),maxNameLength):global::XboxGamingBar.OnexStrings.Get("Введи непустое название без управляющих символов.");error.Visibility=Visibility.Visible;}};
            dialog.Opened+=(s,e)=>{input.Focus(FocusState.Programmatic);input.SelectAll();};
            _onexDetailsDialog=dialog;
            try{return await dialog.ShowAsync()==ContentDialogResult.Primary?input.Text.Trim():null;}
            finally{if(ReferenceEquals(_onexDetailsDialog,dialog))_onexDetailsDialog=null;_onexProfileRenameInput=null;}
        }
        private async Task RenameOnexSavedProfileAsync(JsonObject entry)
        {
            string name=await PromptOnexProfileNameAsync(entry.GetNamedString("name"));if(name==null)return;
            try{await ChangeOnexPerformanceProfileAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("rename")},{"id",entry["id"]},{"name",JsonValue.CreateStringValue(name)}});}
            catch(Exception error){await ShowOnexProfileOperationErrorAsync(error.Message);}
        }
        private async Task ShowOnexProfileOperationErrorAsync(string message)
        {
            message=OnexStrings.Get(message);
            if(_onexDetailsDialog!=null)return;
            var dialog=CreateOnexSettingsDialog(global::XboxGamingBar.OnexStrings.Get("Profile"),message,new TextBlock{Text=message,TextWrapping=TextWrapping.Wrap});dialog.PrimaryButtonText="";dialog.CloseButtonText=global::XboxGamingBar.OnexStrings.Get("Close");
            _onexDetailsDialog=dialog;try{await dialog.ShowAsync();}finally{if(ReferenceEquals(_onexDetailsDialog,dialog))_onexDetailsDialog=null;}
        }
        private async Task DeleteOnexSavedProfileAsync(JsonObject entry)
        {
            if(entry.GetNamedString("id")=="global"||_onexDetailsDialog!=null)return;
            var dialog=CreateOnexSettingsDialog(global::XboxGamingBar.OnexStrings.Get("Delete Profile?"),global::XboxGamingBar.OnexStrings.Get("Настройки этого профиля будут удалены."),new TextBlock{Text=entry.GetNamedString("name"),TextWrapping=TextWrapping.Wrap});
            dialog.PrimaryButtonText=global::XboxGamingBar.OnexStrings.Get("Delete");dialog.CloseButtonText=global::XboxGamingBar.OnexStrings.Get("Keep It");dialog.DefaultButton=ContentDialogButton.Close;
            _onexDetailsDialog=dialog;ContentDialogResult result;
            try{result=await dialog.ShowAsync();}finally{if(ReferenceEquals(_onexDetailsDialog,dialog))_onexDetailsDialog=null;}
            if(result!=ContentDialogResult.Primary)return;
            try{await ChangeOnexPerformanceProfileAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("delete")},{"id",entry["id"]}});}
            catch(Exception error){await ShowOnexProfileOperationErrorAsync(error.Message);}
        }
        private JsonObject OnexEffectiveSavedEntry(JsonObject entry)
        {
            var copy=JsonObject.Parse(entry.Stringify());
            if(entry.TryGetValue("effectiveAc",out var ac))copy["ac"]=ac;
            if(entry.TryGetValue("effectiveDc",out var dc))copy["dc"]=dc;
            copy["useSharedPolicy"]=JsonValue.CreateBooleanValue(false);return copy;
        }
        private void RenderOnexSavedProfiles()
        {
            if(_onexProfilesState==null)return;
            string sort=(ProfileSortComboBox.SelectedItem as ComboBoxItem)?.Tag as string??"name";
            string signature=_onexProfilesState.GetNamedArray("entries").Stringify()+"|"+_onexProfilesState.GetNamedString("activeId")+"|"+sort;
            if(signature==_onexSavedProfilesSignature)return;
            _onexSavedProfilesSignature=signature;_profileGroupExpanders.Clear();_onexSavedProfileRows.Clear();_onexSavedProfileIds.Clear();AllGameProfilesContainer.Children.Clear();
            IEnumerable<JsonObject> entries=OnexProfiles.Where(entry=>entry.GetNamedString("id")!="global");
            if(sort=="modified")entries=entries.OrderByDescending(entry=>entry.GetNamedString("modifiedUtc", ""));
            else if(sort=="tdp")entries=entries.OrderByDescending(entry=>Math.Max(entry.GetNamedObject("ac").GetNamedNumber("pl1"),entry.GetNamedObject("dc").GetNamedNumber("pl1")));
            else entries=entries.OrderBy(entry=>entry.GetNamedString("name"),StringComparer.CurrentCultureIgnoreCase);
            entries=entries.Prepend(OnexProfiles.Single(entry=>entry.GetNamedString("id")=="global"));
            var rowStyle=OnexSettingsButtonStyle(false);
            foreach(var raw in entries)
            {
                var entry=OnexEffectiveSavedEntry(raw);string id=entry.GetNamedString("id"),name=id=="global"?OnexStrings.Get("Global Profile"):entry.GetNamedString("name");bool active=id==_onexProfilesState.GetNamedString("activeId");
                _onexSavedProfileIds.Add(id);
                var nameText=new TextBlock{Text=name,FontSize=16,FontWeight=Windows.UI.Text.FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap};var title=new StackPanel{Spacing=3};title.Children.Add(nameText);
                string kind=id=="global"?"Global":string.IsNullOrEmpty(entry.GetNamedString("path",""))?"Manual":"Per-game / Per-app";
                var kindText=new TextBlock{Text=OnexStrings.Get(kind)+(active?global::XboxGamingBar.OnexStrings.Get(" · ✓ Active"):""),FontSize=12,TextWrapping=TextWrapping.Wrap};title.Children.Add(kindText);
                var choose=new Button{Name="OnexSavedChoose_"+id,Tag=id,Content=title,Style=rowStyle,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch};
                choose.Click+=async(s,e)=>{try{await ChangeOnexPerformanceProfileAsync(new JsonObject{{"operation",JsonValue.CreateStringValue("select")},{"id",JsonValue.CreateStringValue(id)}});}catch(Exception error){await ShowOnexProfileOperationErrorAsync(error.Message);}};
                ToolTipService.SetToolTip(choose,global::XboxGamingBar.OnexStrings.Get("Выбрать профиль для редактирования и применения."));
                var rename=new Button{Name="OnexSavedRename_"+id,Content=new FontIcon{Glyph="\uE70F",FontSize=16},Width=36,Height=36,MinHeight=36,Padding=new Thickness(0),Style=rowStyle,IsEnabled=id!="global"};
                var remove=new Button{Name="OnexSavedDelete_"+id,Content="×",Width=36,Height=36,MinHeight=36,Padding=new Thickness(0),Style=rowStyle,IsEnabled=id!="global"};
                rename.Click+=async(s,e)=>await RenameOnexSavedProfileAsync(raw);remove.Click+=async(s,e)=>await DeleteOnexSavedProfileAsync(raw);
                ToolTipService.SetToolTip(rename,global::XboxGamingBar.OnexStrings.Get("Переименовать"));ToolTipService.SetToolTip(remove,id=="global"?global::XboxGamingBar.OnexStrings.Get("Global Profile нельзя удалить"):global::XboxGamingBar.OnexStrings.Get("Удалить с подтверждением"));
                var header=new Grid{ColumnSpacing=8};header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});header.Children.Add(choose);Grid.SetColumn(rename,1);header.Children.Add(rename);Grid.SetColumn(remove,2);header.Children.Add(remove);
                var body=new StackPanel{Spacing=8};body.Children.Add(header);
                bool onAc=_onexProfilesState.GetNamedBoolean("onAc",false);var summary=new TextBlock{Text=DescribeOnexPerformanceProfile(entry.GetNamedObject(onAc?"ac":"dc")),FontSize=12,TextWrapping=TextWrapping.Wrap};body.Children.Add(summary);
                var details=new Border{Visibility=Visibility.Collapsed};var show=new Button{Name="OnexSavedDetails_"+id,Content=global::XboxGamingBar.OnexStrings.Get("Details"),Style=rowStyle,HorizontalAlignment=HorizontalAlignment.Left};
                show.Click+=(s,e)=>{bool open=details.Visibility!=Visibility.Visible;if(open&&details.Child==null)details.Child=RenderOnexProfileValues(entry);details.Visibility=open?Visibility.Visible:Visibility.Collapsed;show.Content=open?global::XboxGamingBar.OnexStrings.Get("Hide Details"):global::XboxGamingBar.OnexStrings.Get("Details");};body.Children.Add(show);body.Children.Add(details);
                var card=OnexSettingsCard(body);card.BorderBrush=active?OnexDialogAccent:(Windows.UI.Xaml.Media.Brush)Resources["CardBorderBrush"];AllGameProfilesContainer.Children.Add(card);PaintOnexProfileChoiceRow(choose,nameText,kindText,summary,card,active);
                _onexSavedProfileRows.Add(new Control[]{choose,rename,remove});_onexSavedProfileRows.Add(new Control[]{show});
                foreach(var control in new Control[]{choose,rename,remove,show}){control.KeyDown+=OnexSavedProfile_KeyDown;control.IsTabStop=true;}
            }
        }
        private void OnexSavedProfile_KeyDown(object sender,KeyRoutedEventArgs args)
        {
            if(args.Handled||_onexDetailsDialog!=null||!IsOnexProfileVertical(args.Key))return;
            var rows=_onexSavedProfileRows.Select(row=>row.Where(control=>control.IsEnabled).ToArray()).ToArray();
            for(int r=0;r<rows.Length;r++)
            {int c=Array.IndexOf(rows[r],sender);if(c<0)continue;int next=r+(IsOnexProfileDown(args.Key)?1:-1);Control target=next<0?ProfileSortComboBox:next>=rows.Length?_onexPerformanceProfilePicker?.Split:rows[next][Math.Min(c,rows[next].Length-1)];if(target!=null){args.Handled=true;_=Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,()=>{target.Focus(FocusState.Keyboard);target.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});});}return;}
        }
    }
}
