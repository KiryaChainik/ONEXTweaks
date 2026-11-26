// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.UI.Xaml;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private bool _onexChargeKnown, _onexChargeOn, _onexChargeBusy;
        private int _onexChargePercent=90, _onexChargeDesired;
        private int? _onexChargeAcknowledgedPercent;
        private DispatcherTimer _onexChargeTimer;
        private void ApplyOnexChargeState(string payload)
        {
            if (_onexChargeBusy || (_onexChargeTimer?.IsEnabled ?? false)) return;
            if (!JsonObject.TryParse(payload,out var c)) {_onexChargeKnown=false;_onexChargeAcknowledgedPercent=null;ApplyOnexChargeAvailability();return;}
            double reported=c.GetNamedNumber("percent",double.NaN);
            _onexChargeKnown=c.GetNamedBoolean("available",false)&&!double.IsNaN(reported)&&!double.IsInfinity(reported)&&reported>=30&&reported<=100&&reported==Math.Truncate(reported);
            _onexChargeAcknowledgedPercent=_onexChargeKnown?(int?)reported:null;
            if(!_onexChargeKnown){ApplyOnexChargeAvailability();ApplyOnexTileAvailability();return;}
            int actual=_onexChargeAcknowledgedPercent.Value;
            _onexChargeOn=actual<100;
            if (_onexChargeOn) _onexChargePercent=actual;
            else if (ApplicationData.Current.LocalSettings.Values.TryGetValue("OnexChargeTarget",out var saved) && saved is int value && value>=30 && value<100) _onexChargePercent=value;
            _msiChargeLimitLoading=true;
            try
            {
                MsiChargeLimitToggle.IsOn=_onexChargeOn;
                MsiChargeLimitSlider.Value=_onexChargePercent;
                MsiChargeLimitValue.Text=_onexChargePercent+"%";
                MsiChargeLimitPercentPanel.Visibility=_onexChargeOn?Visibility.Visible:Visibility.Collapsed;
            }
            finally {_msiChargeLimitLoading=false;}
            SetChargeStatus(_onexChargeOn?OnexStrings.Format("Live limit: {0}% ✓ verified",actual):OnexStrings.Get("Live limit: 100% (disabled)"),false);
            ApplyOnexChargeAvailability();
            ApplyOnexTileAvailability();
        }
        private void ApplyOnexChargeAvailability()
        {
            UpdateOnexEnergySaverIndicators();
            if (MsiChargeLimitCard==null) return;
            MsiChargeLimitCard.Visibility=Visibility.Visible;
            bool ready=App.IsConnected&&_onexChargeKnown&&!_onexChargeBusy;
            MsiChargeLimitToggle.IsEnabled=ready;
            MsiChargeLimitSlider.IsEnabled=ready;
            MsiChargeLimitVerifyButton.IsEnabled=App.IsConnected&&!_onexChargeBusy;
            if (!_onexChargeKnown) SetChargeStatus(OnexStrings.Get("Charge controller unavailable"),true);
        }
        private async Task QueryOnexChargeAsync()
        {
            try
            {
                var reply=await App.SendMessageAsync(new ValueSet{["OnexChargeGet"]=true});
                if(reply!=null&&reply.TryGetValue("OnexChargeState",out var payload))ApplyOnexChargeState((string)payload);
                else throw new InvalidOperationException("Charge controller disconnected");
            }
            catch(Exception e){_onexChargeKnown=false;_onexChargeAcknowledgedPercent=null;ApplyOnexChargeAvailability();SetChargeStatus(e.Message,true);}
        }
        private void QueueOnexChargeSlider(int percent)
        {
            // 100% disables limiting. Preserve the last enabled target so the next tap
            // can turn the limiter back on instead of writing 100% again.
            if(percent<100)
            {
                _onexChargePercent=percent;
                ApplicationData.Current.LocalSettings.Values["OnexChargeTarget"]=percent;
            }
            if(!_onexChargeOn)return;
            _onexChargeDesired=percent;
            if(_onexChargeTimer==null)
            {
                _onexChargeTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(500)};
                _onexChargeTimer.Tick+=(s,e)=>{_onexChargeTimer.Stop();_=SetOnexChargeAsync(_onexChargeDesired);};
            }
            _onexChargeTimer.Stop();_onexChargeTimer.Start();
        }
        private async Task SetOnexChargeAsync(int percent)
        {
            if(_onexChargeBusy)return;
            _onexChargeTimer?.Stop();
            _onexChargeBusy=true;ApplyOnexChargeAvailability();ApplyOnexTileAvailability();
            string payload=null,error=null;
            try
            {
                var reply=await App.SendMessageAsync(new ValueSet{["OnexChargeSet"]=percent,["OnexUserInput"]=true});
                if(reply==null)throw new InvalidOperationException("Charge controller disconnected");
                if(reply.TryGetValue("Error",out var failure))throw new InvalidOperationException(failure.ToString());
                payload=(string)reply["OnexChargeState"];
                if(percent<100)ApplicationData.Current.LocalSettings.Values["OnexChargeTarget"]=percent;
            }
            catch(Exception e){error=e.Message;}
            finally{_onexChargeBusy=false;}
            if(payload!=null)ApplyOnexChargeState(payload);else await QueryOnexChargeAsync();
            if(error!=null)SetChargeStatus(error,true);
            ApplyOnexChargeAvailability();
        }
        private async Task VerifyOnexChargeAsync()
        {
            var folder=ApplicationData.Current.LocalFolder;
            await folder.GetFileAsync("onex-charge-verification-armed.json");
            var report=new JsonObject();
            var beforeReply=await App.SendMessageAsync(new ValueSet{["OnexChargeGet"]=true});
            var before=JsonObject.Parse((string)beforeReply["OnexChargeState"]);
            int original=(int)before.GetNamedNumber("percent");
            bool hadTarget=ApplicationData.Current.LocalSettings.Values.TryGetValue("OnexChargeTarget",out var savedTarget);
            int previousTarget=_onexChargePercent;
            int test=original==85?80:85;
            async Task Check(int expected)
            {
                for(int i=0;i<25;i++)
                {
                    await Task.Delay(200);
                    var r=await App.SendMessageAsync(new ValueSet{["OnexChargeGet"]=true});
                    var c=JsonObject.Parse((string)r["OnexChargeState"]);
                    if((int)c.GetNamedNumber("percent")==expected&&!_onexChargeBusy)
                    {
                        ApplyOnexChargeState(c.Stringify());
                        if(!qsTileMap.TryGetValue("ChargeLimiter",out var tile)||!tile.IsActionable||tile.StateText.Text=="Pending")throw new InvalidOperationException("Charge tile remained Pending");
                        if(!MsiChargeLimitToggle.IsEnabled||!MsiChargeLimitSlider.IsEnabled)throw new InvalidOperationException("Charge controls disabled");
                        return;
                    }
                }
                throw new InvalidOperationException("EC charge readback mismatch: "+expected);
            }
            try
            {
                report["isGameBar"]=JsonValue.CreateBooleanValue(widget!=null);
                report["before"]=before;
                foreach(var request in new[]{new ValueSet{["OnexChargeSet"]=85},new ValueSet{["OnexChargeSet"]=101,["OnexUserInput"]=true}})
                {
                    var rejected=await App.SendMessageAsync(request);
                    if(rejected==null||!rejected.ContainsKey("Error"))throw new InvalidOperationException("Invalid charge write accepted");
                }
                report["invalidWritesRejected"]=JsonValue.CreateBooleanValue(true);
                if(original==100){MsiChargeLimitToggle.IsOn=true;await Check(_onexChargePercent);}
                MsiChargeLimitSlider.Value=test;
                await Check(test);report["sliderReadback"]=JsonValue.CreateNumberValue(test);
                QuickSettingsTile_Click(new Windows.UI.Xaml.Controls.Button{Tag="ChargeLimiter"},new RoutedEventArgs());
                await Check(100);report["tileOffReadback"]=JsonValue.CreateNumberValue(100);
                QuickSettingsTile_Click(new Windows.UI.Xaml.Controls.Button{Tag="ChargeLimiter"},new RoutedEventArgs());
                await Check(test);report["tileOnReadback"]=JsonValue.CreateNumberValue(test);
                report["passed"]=JsonValue.CreateBooleanValue(true);
            }
            catch(Exception e){report["passed"]=JsonValue.CreateBooleanValue(false);report["error"]=JsonValue.CreateStringValue(e.ToString());}
            finally
            {
                await SetOnexChargeAsync(original);await Check(original);
                if(hadTarget)ApplicationData.Current.LocalSettings.Values["OnexChargeTarget"]=savedTarget;else ApplicationData.Current.LocalSettings.Values.Remove("OnexChargeTarget");
                if(original==100)_onexChargePercent=previousTarget;
                report["restored"]=JsonValue.CreateBooleanValue(true);
                report["finalTileText"]=JsonValue.CreateStringValue(qsTileMap["ChargeLimiter"].StateText.Text);
                report["toggleEnabled"]=JsonValue.CreateBooleanValue(MsiChargeLimitToggle.IsEnabled);
                report["sliderEnabled"]=JsonValue.CreateBooleanValue(MsiChargeLimitSlider.IsEnabled);
                var f=await folder.CreateFileAsync("onex-charge-verification.json",CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(f,report.Stringify());
                await (await folder.GetFileAsync("onex-charge-verification-armed.json")).DeleteAsync();
            }
        }
    }
}
