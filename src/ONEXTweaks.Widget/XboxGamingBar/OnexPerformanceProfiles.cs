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
        private OnexScopedProfilePicker _onexPerformanceProfilePicker;
        private Border _onexPerformanceProfilePickerCard;
        private void EnsureOnexPerformanceProfilePicker()
        {
            if(_onexPerformanceProfilePicker!=null||!(PerformanceScrollViewer.Content is Panel body))return;
            _onexPerformanceProfilePicker=new OnexScopedProfilePicker(ReadOnexPerformanceProfilesAsync,ChangeOnexPerformanceProfileAsync,
                ()=>CreateOnexMenu(FlyoutPlacementMode.Bottom),DescribeOnexPerformanceProfile,(Style)Resources["StandaloneButtonStyle"],tileTextBrush,control=>ApplyOnexTileWindowControlAccent(control));
            _onexPerformanceProfilePickerCard=OnexSharedProfileCard(_onexPerformanceProfilePicker,"Performance",global::XboxGamingBar.OnexStrings.Get("Настройки сохраняются в выбранный профиль при применении. Профиль приложения включается автоматически; ручной выбор действует до перезапуска этого приложения."));
            body.Children.Insert(0,_onexPerformanceProfilePickerCard);
            if(_onexProfilesState!=null)_onexPerformanceProfilePicker.Apply(_onexProfilesState);
        }
        private string DescribeOnexPerformanceProfile(JsonObject values)
        {
            var fps=values.GetNamedObject("fps");int mode=(int)values.GetNamedNumber("powerMode");
            return global::XboxGamingBar.OnexStrings.Format("PL1 {0} W · PL2 {1} · Boost {2} · {3} · FPS {4}",values.GetNamedNumber("pl1"),(OnexProfileSeparatePl2(values)?values.GetNamedNumber("pl2")+" W":global::XboxGamingBar.OnexStrings.Get("Off")),(values.GetNamedNumber("boost")>0?global::XboxGamingBar.OnexStrings.Get("On"):global::XboxGamingBar.OnexStrings.Get("Off")),new[]{"Efficiency","Balanced","Performance"}[mode],(fps.GetNamedBoolean("enabled")?fps.GetNamedNumber("value")+" "+fps.GetNamedString("provider"):global::XboxGamingBar.OnexStrings.Get("Off")));
        }
        private async Task<JsonObject> ReadOnexPerformanceProfilesAsync()
        {
            var reply=await App.SendMessageAsync(new ValueSet{{"OnexProfilesGet",true},{"OnexProfilesCandidatesGet",true}});
            if(reply==null)throw new InvalidOperationException("ONEXTweaks disconnected");
            if(reply.TryGetValue("Error",out object failure))throw new InvalidOperationException(failure.ToString());
            var state=JsonObject.Parse((string)reply["OnexProfilesState"]);
            if(reply.TryGetValue("OnexProfilesCandidates",out object candidates))state["candidates"]=JsonArray.Parse((string)candidates);
            ApplyOnexProfilesState(state.Stringify());return state;
        }
        private async Task ChangeOnexPerformanceProfileAsync(JsonObject mutation)
        {
            string pending=OnexPerformancePendingDetails();if(pending.Length>0){Logger.Warn("[ProfilePicker] Waiting: "+pending);throw new InvalidOperationException(global::XboxGamingBar.OnexStrings.Get("Дождись применения настройки и повтори выбор профиля."));}
            _onexProfilesBusy=true;ApplyOnexProfileAvailability();
            try
            {
                var reply=await SendOnexHomeRequestAsync(new ValueSet{{"OnexProfilesSet",mutation.Stringify()}});
                ApplyOnexProfilesState((string)reply["OnexProfilesState"]);
            }
            finally{_onexProfilesBusy=false;ApplyOnexProfileAvailability();}
        }
        private string OnexPerformancePendingDetails()=>string.Join(",",new[]{OnexSourceEditorsPending?"source-editor":null,OnexFanCurvePending?"fan-curve":null,_onexPerformanceCpuApplying?"cpu":null,_onexPerformanceCpuLoading?"cpu-loading":null,_onexPerformanceCpuForm?.Pending?.Invoke()==true?"cpu-draft":null,_onexBoostBusy?"boost":null,_onexPowerModeBusy?"mode":null,_onexProfilesBusy?"profile":null,_onexPerformancePl2Busy?"pl2":null,_onexPerformancePl2ValuePending?"pl2-preview":null,_onexPerformancePl2ValueBusy?"pl2-value":null,_onexPerformancePl1Pending?"pl1-preview":null,_onexPerformancePl1Busy?"pl1-value":null,_releaseHome?.HasPendingPowerInput==true?"home-power":null,_onexFpsBusy?"fps":null,_onexFanBusy?"fan":null,_onexDisplayBusy?"display":null,_onexFpsTimer?.IsEnabled==true?"fps-delay":null,tdp?.HasPendingUserValue==true?"tdp-delay":null}.Where(value=>value!=null));
        private void OnexPerformanceScopeChanged(string next,bool onAc)
        {
            if(OnexFrontend.ProfileId==next&&(_onexProfilesState==null||_onexProfilesState.GetNamedBoolean("onAc",false)==onAc))return;
            CancelOnexPerformanceScopeEdits();
            OnexFrontend.ProfileId=next;
        }
        private void CancelOnexPerformanceScopeEdits()
        {
            CancelOnexPerformancePl2Value();CancelOnexPerformancePl1Value();_releaseHome?.CancelPendingPower();
            tdp?.StopDebounceTimer();
            OnexFrontend.CancelPendingPowerInput();
            _onexFpsTimer?.Stop();
            _onexPerformanceCpuForm?.Cancel?.Invoke();
            OnexInputGuard.CancelInteraction(TDPSlider);
            OnexInputGuard.CancelInteraction(TDPBoostFPPTSliderCard);
            OnexInputGuard.CancelInteraction(TDPBoostFPPTSlider);
            OnexInputGuard.CancelInteraction(_releaseHome?.PowerInput);
            OnexInputGuard.CancelInteraction(FPSLimitSlider);
        }
    }
}
