// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        private bool _onexBoostKnown,_onexBoostOn,_onexBoostBusy;
        internal bool OnexBoostUpdating {get;private set;}
        private void ApplyOnexBoostState(string payload)
        {
            if(_onexBoostBusy)return;
            if(!JsonObject.TryParse(payload,out var state)){_onexBoostKnown=false;ApplyOnexAvailability();return;}
            _onexBoostKnown=state.GetNamedBoolean("available",false);
            _onexBoostOn=state.GetNamedBoolean("enabled",false);
            SyncOnexCpuFormFromBoost(state);
            OnexBoostUpdating=true;
            try{cpuBoost.SetValueSilent(_onexBoostOn);CPUBoostToggle.IsOn=_onexBoostOn;}
            finally{OnexBoostUpdating=false;}
            CPUBoostToggle.IsEnabled=App.IsConnected&&_onexBoostKnown&&!_onexPerformanceCpuApplying;
            ToolTipService.SetToolTip(CPUBoostToggle,global::XboxGamingBar.OnexStrings.Format("Windows boost policy · AC {0} · Battery {1}",state.GetNamedNumber("acMode"),state.GetNamedNumber("dcMode")));
            _releaseHome?.ApplyBoost(_onexBoostOn,App.IsConnected&&_onexBoostKnown&&!_onexBoostBusy);
            ApplyOnexTileAvailability();
        }
        internal void OnexBoostToggleChanged(bool enabled)
        {
            if(OnexBoostUpdating||!_onexBoostKnown||_onexBoostBusy)return;
            _=SetOnexBoostAsync(enabled);
        }
        private async Task SetOnexBoostAsync(bool enabled)
        {
            if(_onexBoostBusy)return;
            _onexBoostBusy=true;CPUBoostToggle.IsEnabled=false;ApplyOnexTileAvailability();
            string payload=null,error=null;
            try
            {
                var reply=await App.SendMessageAsync(new ValueSet{["OnexBoostSet"]=enabled,["OnexUserInput"]=true});
                if(reply==null)throw new InvalidOperationException("Boost controller disconnected");
                if(reply.TryGetValue("Error",out var e))throw new InvalidOperationException(e.ToString());
                payload=(string)reply["OnexBoostState"];
            }
            catch(Exception e){error=e.Message;}
            finally{_onexBoostBusy=false;}
            if(payload==null)
            {
                var reply=await App.SendMessageAsync(new ValueSet{["OnexBoostGet"]=true});
                if(reply!=null&&reply.TryGetValue("OnexBoostState",out var value))payload=(string)value;
            }
            if(payload!=null)ApplyOnexBoostState(payload);
            if(error!=null)ToolTipService.SetToolTip(CPUBoostToggle,error);
        }
        private async Task VerifyOnexBoostAsync()
        {
            var folder=ApplicationData.Current.LocalFolder;
            await folder.GetFileAsync("onex-boost-verification-armed.json");
            var r=await App.SendMessageAsync(new ValueSet{["OnexBoostGet"]=true});
            var before=JsonObject.Parse((string)r["OnexBoostState"]);
            ApplyOnexBoostState(before.Stringify());
            var report=new JsonObject{["before"]=before,["isGameBar"]=JsonValue.CreateBooleanValue(widget!=null)};
            int ac=(int)before.GetNamedNumber("acMode"),dc=(int)before.GetNamedNumber("dcMode");
            async Task<JsonObject> Check(int expectedAc,int expectedDc)
            {
                for(int i=0;i<30;i++)
                {
                    await Task.Delay(200);
                    var reply=await App.SendMessageAsync(new ValueSet{["OnexBoostGet"]=true});
                    var state=JsonObject.Parse((string)reply["OnexBoostState"]);
                    if((int)state.GetNamedNumber("acMode")==expectedAc&&(int)state.GetNamedNumber("dcMode")==expectedDc&&!_onexBoostBusy)
                    {
                        ApplyOnexBoostState(state.Stringify());
                        if(!CPUBoostToggle.IsEnabled||!qsTileMap["CPUBoost"].IsActionable||qsTileMap["CPUBoost"].StateText.Text=="Pending")throw new InvalidOperationException("Boost UI unavailable");
                        return state;
                    }
                }
                throw new InvalidOperationException("Boost readback mismatch");
            }
            try
            {
                if(ac==0||dc==0)throw new InvalidOperationException("Verification expects an initially enabled AC/DC policy");
                var invalid=await App.SendMessageAsync(new ValueSet{["OnexBoostSet"]=false});
                if(invalid==null||!invalid.ContainsKey("Error"))throw new InvalidOperationException("Unmarked boost write accepted");
                QuickSettingsTile_Click(new Button{Tag="CPUBoost"},new RoutedEventArgs());
                report["tileOff"]=await Check(0,0);
                QuickSettingsTile_Click(new Button{Tag="CPUBoost"},new RoutedEventArgs());
                report["tileOn"]=await Check(ac,dc);
                report["passed"]=JsonValue.CreateBooleanValue(true);
            }
            catch(Exception e){report["passed"]=JsonValue.CreateBooleanValue(false);report["error"]=JsonValue.CreateStringValue(e.ToString());}
            finally
            {
                if(ac>0&&dc>0){await SetOnexBoostAsync(true);await Check(ac,dc);}
                report["restored"]=JsonValue.CreateBooleanValue(true);
                report["tileText"]=JsonValue.CreateStringValue(qsTileMap["CPUBoost"].StateText.Text);
                var f=await folder.CreateFileAsync("onex-boost-verification.json",CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(f,report.Stringify());
                await (await folder.GetFileAsync("onex-boost-verification-armed.json")).DeleteAsync();
            }
        }
    }
}
