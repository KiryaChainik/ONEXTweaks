// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private JsonObject _onexScreenProfiles;
        private OnexScopedProfilePicker _onexScreenProfilePicker;
        private Border _onexScreenProfilePickerCard;
        private string _onexDisplayPendingScreenProfile;
        private bool _onexDisplayProfileTransition;
        private int _onexDisplayProfileRevision;
        private string OnexScreenProfileId=>_onexScreenProfiles?.GetNamedString("activeId","global")??"global";
        private JsonObject OnexSelectedScreenProfile=>_onexScreenProfiles?.GetNamedArray("entries",null)?.Select(value=>value.GetObject()).FirstOrDefault(value=>value.GetNamedString("id")==OnexScreenProfileId);
        private string OnexScreenProfileCaption=>OnexSelectedScreenProfile==null||OnexSelectedScreenProfile.GetNamedString("id","global")=="global"?OnexStrings.Get("Global Profile"):OnexSelectedScreenProfile.GetNamedString("name");
        private void EnsureOnexScreenProfilePicker()
        {
            if(_onexScreenProfilePicker!=null)return;
            _onexScreenProfilePicker=new OnexScopedProfilePicker(ReadOnexScreenProfilesAsync,MutateOnexScreenProfilesAsync,()=>CreateOnexMenu(FlyoutPlacementMode.Bottom),DescribeOnexScreenProfile,(Style)Resources["StandaloneButtonStyle"],tileTextBrush,control=>ApplyOnexTileWindowControlAccent(control));
            _onexScreenProfilePickerCard=OnexSharedProfileCard(_onexScreenProfilePicker,"Screen",global::XboxGamingBar.OnexStrings.Get("Профиль сохраняет цвет, резкость и HDR автоматически при каждом изменении. Игровой профиль включается для выбранного процесса; ручной выбор действует до его перезапуска."));
            _onexScreenProfilePicker.Split.Visibility=Visibility.Collapsed;_onexScreenProfilePicker.Split.IsTabStop=false;
            _onexScreenProfilePicker.Footer.Visibility=Visibility.Collapsed;
            DisplayTopGroup.Children.Insert(0,_onexScreenProfilePickerCard);
            _onexScreenSavedProfiles=new OnexSavedCatalogSection(this,"Screen",()=>_onexScreenProfiles,MutateOnexScreenProfilesAsync,DescribeOnexScreenProfile);DisplayMainStack.Children.Add(_onexScreenSavedProfiles.Card);
        }
        private string DescribeOnexScreenProfile(JsonObject values)
        {
            var colors=OnexDisplayObject(values,"colors");var hdr=OnexDisplayObject(values,"hdr");
            string number(JsonObject value,string key,double fallback=0)=>value.GetNamedNumber(key,fallback).ToString("0.##",System.Globalization.CultureInfo.InvariantCulture);
            string color=colors==null?global::XboxGamingBar.OnexStrings.Get("Цвет Windows"):"Saturation "+number(colors,"saturation")+" · Contrast "+number(colors,"contrast")+" · Brightness "+number(colors,"brightness")+" · Hue "+number(colors,"hue")+"° · Gamma "+(colors.GetNamedNumber("gamma",100)/100).ToString("0.00",System.Globalization.CultureInfo.InvariantCulture);
            var sharp=values.GetNamedValue("sharpness",JsonValue.CreateNullValue());if(sharp.ValueType==JsonValueType.Number)color+=" · Sharpness "+sharp.GetNumber();
            return color+global::XboxGamingBar.OnexStrings.Get(" · HDR ")+(hdr?.GetNamedBoolean("enabled",false)==true?global::XboxGamingBar.OnexStrings.Get("On"):global::XboxGamingBar.OnexStrings.Get("Off"))+global::XboxGamingBar.OnexStrings.Get(" · Auto HDR ")+(hdr?.GetNamedBoolean("autoHdr",false)==true?global::XboxGamingBar.OnexStrings.Get("On"):global::XboxGamingBar.OnexStrings.Get("Off"));
        }
        private bool ApplyOnexScreenProfilesState(string json)
        {
            if(!JsonObject.TryParse(json,out var state))return false;
            if(OnexScreenProfileId!=state.GetNamedString("activeId","global"))
                CancelOnexDisplayProfileEdits();
            EnsureOnexScreenProfilePicker();_onexScreenProfiles=state;_onexScreenProfilePicker.Apply(state);_onexScreenSavedProfiles?.Apply();UpdateDisplayProfileBadge();ApplyOnexTileAvailability();WireOnexDisplayNavigation();RefreshOnexProfileSummaries();
            return true;
        }

        private void CancelOnexDisplayProfileEdits()
        {
            _onexDisplayProfileTransition=true;
            _onexDisplayProfileRevision++;
            _onexDisplayDebounce?.Stop();
            _onexDisplayPending.Clear();
            _onexDisplayPendingScreenProfile=null;
            _onexDisplayDragging.Clear();
            foreach(var slider in _onexDisplaySliders.Keys)OnexInputGuard.CancelInteraction(slider);
            OnexInputGuard.CancelInteraction(_onexSdrBrightness);
        }

        private void ApplyOnexScreenProfilesPush(string json,IPC.NamedPipeClient owner)
        {
            string previous=OnexScreenProfileId;
            if(!ApplyOnexScreenProfilesState(json)||previous==OnexScreenProfileId||!_onexDisplayProfileTransition)return;
            UpdateOnexDisplayAvailability();
            _=RefreshOnexDisplayForConnectionAsync(owner);
        }
        private async Task<JsonObject> ReadOnexScreenProfilesAsync()
        {
            var reply=await App.SendMessageAsync(new ValueSet{{"OnexDisplayProfilesGet",true}});
            return ApplyOnexScreenProfilesReply(reply);
        }

        private JsonObject ApplyOnexScreenProfilesReply(ValueSet reply)
        {
            if(reply.ContainsKey("Error"))throw new InvalidOperationException((string)reply["Error"]);
            var state=JsonObject.Parse((string)reply["OnexDisplayProfilesState"]);
            if(reply.TryGetValue("OnexDisplayCandidates",out object candidates))state["candidates"]=JsonArray.Parse((string)candidates);
            reply["OnexDisplayProfilesState"]=state.Stringify();
            ApplyOnexDisplayReadReply(reply);
            return state;
        }
        private async Task MutateOnexScreenProfilesAsync(JsonObject mutation)
        {
            var owner=App.PipeClient;
            if(_onexDisplayPending.Count>0)await FlushOnexDisplaySlidersAsync();
            RequireOnexProfileConnection(owner);
            if(_onexDisplayBusy||_onexDisplayPending.Count>0)throw new InvalidOperationException(global::XboxGamingBar.OnexStrings.Get("Дождись применения ползунка и повтори выбор профиля."));
            var reply=await SendOnexHomeRequestAsync(new ValueSet{{"OnexDisplayProfilesSet",mutation.Stringify()}});
            ApplyOnexDisplayReadReply(reply);
        }
    }
}
