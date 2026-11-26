// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.System;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private OnexScopedProfilePicker _onexFanProfilePicker;
        private Border _onexFanProfilePickerCard;
        private JsonObject _onexFanProfiles;
        private string _onexFanSelectionError;
        private void RefreshOnexFanPageUi()
        {
            if(_onexFanProfiles==null)return;
            _onexFanProfilePicker?.Apply(_onexFanProfiles);RefreshOnexFanProfileEditor();_onexFanSavedProfiles?.Apply();
        }
        private void EnsureOnexFanProfiles()
        {
            if(_onexFanProfilePicker!=null||!(FanScrollViewer.Content is Panel body))return;
            foreach(var item in body.Children.OfType<FrameworkElement>())HideOnexUnusedLegacy(item);
            _onexFanProfilePicker=new OnexScopedProfilePicker(ReadOnexFanProfilesAsync,ChangeOnexFanProfilesAsync,()=>CreateOnexMenu(FlyoutPlacementMode.Bottom),DescribeOnexFanProfile,(Style)Resources["StandaloneButtonStyle"],tileTextBrush,control=>ApplyOnexTileWindowControlAccent(control)){IncludeRunningProcesses=true};
            _onexFanProfilePickerCard=OnexSharedProfileCard(_onexFanProfilePicker,"Fan",global::XboxGamingBar.OnexStrings.Get("Create a manual profile or choose a running app. Fan settings are saved automatically."));body.Children.Insert(0,BuildOnexFanLiveStats());body.Children.Insert(1,_onexFanProfilePickerCard);
            _onexFanDcForm=BuildOnexFanCurveEditor(false);_onexFanAcForm=BuildOnexFanCurveEditor(true);body.Children.Add(_onexFanDcForm.Root);body.Children.Add(_onexFanAcForm.Root);
            ToolTipService.SetToolTip(_onexFanProfilePicker.Split,global::XboxGamingBar.OnexStrings.Get("DC — настройки от батареи; AC — отдельный блок для зарядки. При выключении используется DC, настройки AC сохраняются."));
            _onexFanSavedProfiles=new OnexSavedCatalogSection(this,"Fan",()=>_onexFanProfiles,ChangeOnexFanProfilesAsync,DescribeOnexFanProfile);body.Children.Add(_onexFanSavedProfiles.Card);
        }
        private string DescribeOnexFanProfile(JsonObject values)
        {
            int mode=(int)values.GetNamedNumber("mode");if(mode!=3)return OnexStrings.Get("Auto · управление системой");
            var curve=values.GetNamedObject("curve",null);return global::XboxGamingBar.OnexStrings.Format("Custom · {0}",(curve==null?"—":string.Join(" / ",OnexFanPoints(curve).Select(point=>point+"%"))));
        }
        private void ApplyOnexFanProfilesState(string json)
        {
            if(!JsonObject.TryParse(json,out var state))return;EnsureOnexFanProfiles();_onexFanProfiles=state;
            if(FanScrollViewer.Visibility!=Visibility.Visible){RefreshOnexFanProfileEditor();ApplyOnexTileAvailability();RefreshOnexProfileSummaries();RefreshOnexSourceEditors();return;}
            if(!_onexNavigationPerfCapture){RefreshOnexFanPageUi();ApplyOnexTileAvailability();RefreshOnexProfileSummaries();RefreshOnexSourceEditors();return;}
            MeasureOnexNavigationWork("fan-picker",_onexFanProfilePickerCard,()=>_onexFanProfilePicker.Apply(state));
            MeasureOnexNavigationWork("fan-editor",_onexFanProfilePickerCard,RefreshOnexFanProfileEditor);
            MeasureOnexNavigationWork("fan-saved",_onexFanProfilePickerCard,()=>_onexFanSavedProfiles?.Apply());
            MeasureOnexNavigationWork("fan-home",_onexFanProfilePickerCard,ApplyOnexTileAvailability);
            MeasureOnexNavigationWork("fan-summaries",_onexFanProfilePickerCard,RefreshOnexProfileSummaries);
            MeasureOnexNavigationWork("fan-source-editors",_onexFanProfilePickerCard,RefreshOnexSourceEditors);
        }
        private Control[][] OnexFanRows()=>OnexProfileRows(_onexFanProfilePicker)
            .Concat(OnexFanEditorRows(_onexFanDcForm))
            .Concat(new[]{new Control[]{_onexFanProfilePicker?.Split}})
            .Concat(OnexFanEditorRows(_onexFanAcForm))
            .Concat(_onexFanSavedProfiles?.FocusRows()??Enumerable.Empty<Control[]>())
            .Select(row=>row.Where(control=>control!=null&&control.IsEnabled&&control.Visibility==Visibility.Visible).ToArray())
            .Where(row=>row.Length>0).ToArray();
        private static Control[][] OnexFanEditorRows(OnexFanCurveForm form)=>
            form?.Root.Visibility==Visibility.Visible
                ?new[]{new Control[]{form.Custom},form.Sliders.Cast<Control>().ToArray(),new Control[]{form.Readback,form.Reset}}
                :Array.Empty<Control[]>();
        private void WireOnexFanNavigation()
        {
            var rows=OnexFanRows();for(int row=0;row<rows.Length;row++)for(int column=0;column<rows[row].Length;column++){var control=rows[row][column];control.XYFocusLeft=rows[row][Math.Max(0,column-1)];control.XYFocusRight=rows[row][Math.Min(rows[row].Length-1,column+1)];control.XYFocusUp=row>0?rows[row-1][Math.Min(column,rows[row-1].Length-1)]:control;control.XYFocusDown=row+1<rows.Length?rows[row+1][Math.Min(column,rows[row+1].Length-1)]:control;}
        }
        private bool HandleOnexFanNavigation(KeyRoutedEventArgs args)
        {
            if(args.Handled||!OnexFrontend.UsesOwnBackend||FanNavItem?.IsChecked!=true||
                _onexDetailsDialog!=null||_onexOpenMenus.Any(menu=>menu.IsOpen))return false;
            var key=OnexInputGuard.SliderKey(args);
            int direction=OnexNavigationDirection(key);
            if(direction==0)return false;
            var current=FocusManager.GetFocusedElement() as Control;
            if(current==null||current is ComboBox combo&&combo.IsDropDownOpen)return false;
            var rows=OnexFanRows();
            for(int row=0;row<rows.Length;row++)
            {
                int column=Array.IndexOf(rows[row],current);
                if(column<0)continue;
                // Use logical rows. Slider heights differ with their thermal floors.
                int nextRow=Math.Max(0,Math.Min(rows.Length-1,row+(Math.Abs(direction)==2?Math.Sign(direction):0)));
                int nextColumn=Math.Max(0,Math.Min(rows[nextRow].Length-1,column+(Math.Abs(direction)==1?direction:0)));
                var target=rows[nextRow][nextColumn];
                args.Handled=true;
                if(!ReferenceEquals(target,current)&&target.Focus(FocusState.Keyboard))
                    target.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});
                return true;
            }
            return false;
        }
        private async Task<JsonObject> ReadOnexFanProfilesAsync()
        {
            var reply=await App.SendMessageAsync(new ValueSet{{"OnexFanProfilesGet",true}});if(reply==null||reply.ContainsKey("Error"))throw new InvalidOperationException("Fan profiles are unavailable");
            var state=JsonObject.Parse((string)reply["OnexFanProfilesState"]);if(reply.TryGetValue("OnexFanCandidates",out object candidates))state["candidates"]=JsonArray.Parse((string)candidates);ApplyOnexFanProfilesState(state.Stringify());
            if(reply.TryGetValue("OnexHardwareHomeState",out object hardware)&&hardware is string home)
                ApplyOnexHardwareHomeState(home);
            return state;
        }
        private async Task ChangeOnexFanProfilesAsync(JsonObject mutation)
        {
            var owner=App.PipeClient;
            if(OnexFanForms.Any(form=>form.Sliders.Any(OnexInputGuard.IsPreviewing)))throw new InvalidOperationException(global::XboxGamingBar.OnexStrings.Get("Подтверди кривую кнопкой A или отмени кнопкой B."));
            foreach(var form in OnexFanForms)if(form.Draft!=null)await FlushOnexFanCurveAsync(form);for(int n=0;n<100&&_onexFanCurveWriting;n++)await Task.Delay(30);
            RequireOnexProfileConnection(owner);
            if(_onexFanBusy||OnexFanCurvePending)throw new InvalidOperationException(global::XboxGamingBar.OnexStrings.Get("Дождись применения Fan."));
            bool selecting=mutation.GetNamedString("operation")=="select";
            var reply=await SendOnexHomeRequestAsync(new ValueSet{{"OnexFanProfilesSet",mutation.Stringify()}});
            reply.TryGetValue("OnexFanSelectionError",out object selectionError);
            if(selecting)
            {
                string priorError=_onexFanSelectionError;
                _onexFanSelectionError=selectionError==null?null:OnexStrings.Format("Could not switch Fan profile: {0}",selectionError);
                if(_onexFanSelectionError!=null||priorError!=null&&OnexDesktopStatus.Text==priorError)
                    ShowOnexDesktopStatus(_onexFanSelectionError);
            }
            ApplyOnexFanProfilesState((string)reply["OnexFanProfilesState"]);
            ApplyOnexHardwareHomeState((string)reply["OnexHardwareHomeState"]);
            if(selectionError!=null)throw new InvalidOperationException(_onexFanSelectionError);
            if(reply.TryGetValue("OnexFanApplyError",out object failure))
                throw new InvalidOperationException(OnexStrings.Format("Fan settings saved, but could not be applied: {0}",failure));
        }
    }
}
