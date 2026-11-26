// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private JsonObject _onexStartupState;
        private bool _onexStartupLoading,_onexStartupBusy;
        private void ApplyOnexStartupState(string json)
        {
            if(JsonObject.TryParse(json,out var state))_onexStartupState=state;
            UpdateOnexStartupControl();
        }
        private void UpdateOnexStartupControl()
        {
            if(OnexStartupToggle==null)return;
            bool ready=App.IsConnected&&_onexStartupState?.GetNamedBoolean("available",false)==true;
            _onexStartupLoading=true;
            try{if(!_onexStartupBusy)OnexStartupToggle.IsOn=_onexStartupState?.GetNamedBoolean("enabled",false)==true;OnexStartupToggle.IsEnabled=ready&&!_onexStartupBusy;}
            finally{_onexStartupLoading=false;}
            OnexStartupStatus.Text=_onexStartupBusy?global::XboxGamingBar.OnexStrings.Get("Сохранение…"):!ready?global::XboxGamingBar.OnexStrings.Get("Автозапуск недоступен. Проверьте установку ONEXTweaks."):OnexStartupToggle.IsOn?global::XboxGamingBar.OnexStrings.Get("Виджет и помощник запускаются вместе при входе в Windows."):global::XboxGamingBar.OnexStrings.Get("Автозапуск выключен. При ручном открытии виджета помощник запустится вместе с ним.");
        }
        private async Task QueryOnexStartupAsync()
        {
            try{var reply=await App.SendMessageAsync(new ValueSet{{"OnexStartupGet",true}});if(reply!=null&&reply.TryGetValue("OnexStartupState",out var json))ApplyOnexStartupState((string)json);}
            catch{_onexStartupState=null;UpdateOnexStartupControl();}
        }
        private async void OnexStartupToggle_Toggled(object sender,Windows.UI.Xaml.RoutedEventArgs args)
        {
            if(_onexStartupLoading||_onexStartupBusy||!OnexFrontend.UsesOwnBackend)return;
            bool enabled=OnexStartupToggle.IsOn;_onexStartupBusy=true;UpdateOnexStartupControl();string error=null;
            try{var reply=await SendOnexHomeRequestAsync(new ValueSet{{"OnexStartupSet",enabled}});ApplyOnexStartupState((string)reply["OnexStartupState"]);}
            catch(Exception failure){error=failure.Message;await QueryOnexStartupAsync();}
            finally{_onexStartupBusy=false;UpdateOnexStartupControl();}
            if(error!=null)OnexStartupStatus.Text=error;
        }
    }
}
