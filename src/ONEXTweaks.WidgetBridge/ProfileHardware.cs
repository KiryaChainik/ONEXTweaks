// SPDX-License-Identifier: AGPL-3.0-only
using ONEXTweaks.Panel;
using ONEXTweaks.Power;
namespace ONEXTweaks.WidgetBridge;
internal static partial class Program
{
    private sealed record FactoryPowerRollback(string Token, string RawLimits, PreferredPower? Preferences);
    private sealed record CapturedProfileValues(ProfileValues Values, FactoryPowerRollback? Factory = null);

    private static async Task<string?> CaptureFactoryPowerToken(PowerChannel power, PowerState current)
    {
        if (!Pl2ControlPolicy.IsFactoryReadback(current.Pl1Watts, current.Pl2Watts)) return null;
        var capture = await power.CaptureFactoryPowerAsync(current.RawLimits);
        if (!capture.Ok || capture.FactoryPowerToken is not { Length: > 0 } token)
            throw new IOException(capture.Error ?? "Could not retain the original factory power state.");
        return token;
    }

    private static async Task<ProfileValues> CaptureProfileValues(PowerChannel power, FrameLimiter fps, RtssOverlay osd)
        => (await CaptureProfileValuesCore(power, fps, osd, false)).Values;

    private static async Task<PerformanceTransitionSnapshot> CaptureProfileTransition(PowerChannel power, FrameLimiter fps, RtssOverlay osd)
    {
        var captured = await CaptureProfileValuesCore(power, fps, osd, true);
        return new(captured.Values, captured.Factory is {} factory
            ? () => ApplyProfileValues(power, fps, osd, captured.Values, factory)
            : null);
    }

    private static async Task<CapturedProfileValues> CaptureProfileValuesCore(PowerChannel power, FrameLimiter fps, RtssOverlay osd, bool forTransition)
    {
        await powerGate.WaitAsync();
        try
        {
            var actual=await power.ReadAsync();if(!actual.Ok||actual.State==null)throw new IOException(actual.Error??"Power sensor unavailable");state=actual.State;
            var home=await power.ReadHomeAsync();if(!home.Ok||home.Home==null)throw new IOException(home.Error??"Device settings unavailable");homeHardware=home.Home;
            var win=WindowsPowerSettings.Read();if(!win.Available||win.Cpu==null)throw new IOException(win.Error);var limit=fps.Read();
            var selection=PowerSelectionFor(state);
            int pl1 = (int)state.Pl1Watts;
            bool factory = Pl2ControlPolicy.IsFactoryReadback(state.Pl1Watts, state.Pl2Watts);
            int pl2 = factory ? Pl2ControlPolicy.Automatic(pl1)
                : selection?.ManualPl2Watts ?? (int)state.Pl2Watts;
            if (selection?.SeparatePl2 == false)
                pl2 = Pl2ControlPolicy.AdjustManual(pl1, pl2);
            bool separate = !factory && (selection?.SeparatePl2 ?? state.Pl2Watts > state.Pl1Watts);
            var values = new ProfileValues(pl1, pl2, (int)(win.OnAc ? win.Cpu.BoostAc : win.Cpu.BoostDc),
                (int)(win.OnAc ? win.Cpu.SchedulingAc : win.Cpu.SchedulingDc), homeHardware["cpuClockMhz"]?.GetValue<int>() ?? 0,
                win.OnAc ? win.ModeAc : win.ModeDc, new(limit.Provider, limit.Enabled, limit.Value),
                0, osd.Preferences.Level, separate,
                displayControl?.Capture(), null, cpuTuning?.CapturePolicy());
            string? token = forTransition ? await CaptureFactoryPowerToken(power, state) : null;
            return new(values, token == null ? null : new(token, state.RawLimits, powerPreferences.Load()));
        }
        finally{powerGate.Release();}
    }
    private static async Task ApplyProfileValues(PowerChannel power, FrameLimiter fps, RtssOverlay osd, ProfileValues desired,
        FactoryPowerRollback? factory = null)
    {
        desired.Validate();await powerGate.WaitAsync();
        PreferredPower? preferencesAtRollback = null;
        try
        {
            if (factory != null) preferencesAtRollback = powerPreferences.Load();
            bool hadKnownSelection=powerPreferences.Load()?.SeparatePl2.HasValue==true;
            var win=WindowsPowerSettings.Read();if(!win.Available||win.Cpu==null)throw new IOException(win.Error);bool planChanged=false;
            if((win.OnAc?win.Cpu.BoostAc:win.Cpu.BoostDc)!=desired.Boost||(win.OnAc?win.Cpu.SchedulingAc:win.Cpu.SchedulingDc)!=desired.Scheduling)
            {var cpu=win.Cpu;WindowsPowerSettings.SetCpu(win.OnAc?cpu with{BoostAc=(uint)desired.Boost,SchedulingAc=(uint)desired.Scheduling,ShortSchedulingAc=(uint)desired.Scheduling}:cpu with{BoostDc=(uint)desired.Boost,SchedulingDc=(uint)desired.Scheduling,ShortSchedulingDc=(uint)desired.Scheduling});planChanged=true;}
            if((win.OnAc?win.ModeAc:win.ModeDc)!=desired.PowerMode){WindowsPowerSettings.SetMode(desired.PowerMode);planChanged=true;}
            var home=await power.ReadHomeAsync();if(!home.Ok||home.Home==null)throw new IOException(home.Error);homeHardware=home.Home;
            if((homeHardware["cpuClockMhz"]?.GetValue<int>()??0)!=desired.ClockMhz){var result=await power.SetCpuClockAsync(desired.ClockMhz);if(!result.Ok)throw new IOException(result.Error);homeHardware=result.Home;planChanged=true;}
            if(cpuTuning?.HasLease==true||desired.CpuTuning!=null)
            {
                var cpuBaseline=await power.ReadAsync();if(!cpuBaseline.Ok||cpuBaseline.State==null)throw new IOException("Cannot preserve TDP before applying CPU profile");
                try{cpuTuning?.Apply(desired.CpuTuning);}
                finally{if(cpuBaseline.State.Pl1Watts==desired.Pl1&&cpuBaseline.State.Pl2Watts==desired.EffectivePl2)await PreserveWindowsPowerLimit(power,cpuBaseline.State);}
            }
            var current=fps.Read();if(current.Provider!=desired.Fps.Provider||current.Enabled!=desired.Fps.Enabled||current.Value!=desired.Fps.Value)fps.Set(desired.Fps);
            if(osd.Preferences.Level!=desired.Overlay)osd.SetLevel(desired.Overlay);
            if (displayControl?.ApplyProfile(desired.Display, preserveImage: true) == true)
                screenProfiles?.ApplyAfterGraphicsChange();
            if (factory != null) return;
            var actual=await power.ReadAsync();if(!actual.Ok||actual.State==null)throw new IOException(actual.Error);state=actual.State;
            // Windows plan changes can reset RAPL; power is applied last and verified by PowerAgent.
            if(planChanged||state.Pl1Watts!=desired.Pl1||state.Pl2Watts!=desired.EffectivePl2){var applied=await power.SetAsync(desired.Pl1,desired.EffectivePl2);if(!applied.Ok||applied.State==null)throw new IOException(applied.Error);state=applied.State;AuditPower("profile-transition",state);}
            if(desired.SeparatePl2.HasValue||hadKnownSelection)powerPreferences.Save((int)state.Pl1Watts,(int)state.Pl2Watts,desired.SeparateEnabled,desired.SeparateEnabled?(int)state.Pl2Watts:desired.Pl2);
            else powerPreferences.Save((int)state.Pl1Watts,(int)state.Pl2Watts); // Keep a legacy active mode unknown until the widget imports its existing selector. 
        }
        finally
        {
            try
            {
                if (factory != null)
                {
                    var restored = await power.RestoreFactoryPowerAsync(factory.Token);
                    if (!restored.Ok || restored.State?.RawLimits != factory.RawLimits)
                        throw new IOException(restored.Error ?? "Factory power rollback did not match the original readback.");
                    state = restored.State;
                    powerPreferences.RestoreAfterFactoryRollback(factory.Preferences, preferencesAtRollback);
                }
            }
            finally { powerGate.Release(); }
        }
    }
    private static void ApplyFanProfile(PowerChannel power,FanPreferences value,bool powerGateOwned,bool recoverOnly=false)
    {
        value.Validate();if(!powerGateOwned)powerGate.Wait();
        try
        {
            var live=power.ReadHomeAsync().GetAwaiter().GetResult();if(!live.Ok||live.Home==null)throw new IOException(live.Error);homeHardware=live.Home;
            if(recoverOnly&&!FanConnectionRecovery.CanRecoverFrom(homeHardware["fan"]))return;
            if(CanReuseFanState(homeHardware["fan"],value))return;
            var result=(value.Mode==3?power.SetFanCurveAsync(value.Curve!.Points()):power.SetFanAsync(value.Mode)).GetAwaiter().GetResult();
            if(!result.Ok||result.Home==null)
            {
                // A rejected command can send the fan back to Auto without a Home payload.
                homeHardware=null;
                try
                {
                    var readback=power.ReadHomeAsync().GetAwaiter().GetResult();
                    if(readback.Ok)homeHardware=readback.Home;
                }
                catch(Exception readFailure){Log(readFailure);}
                throw new IOException(result.Error??"Could not confirm fan settings");
            }
            homeHardware=result.Home;
            if(!FanStateMatches(homeHardware["fan"],value))throw new IOException("Could not confirm fan settings");
        }
        finally{if(!powerGateOwned)powerGate.Release();}
    }
    // The Agent checks live duty even when the requested curve does not need another write.
    internal static bool CanReuseFanState(System.Text.Json.Nodes.JsonNode? fan,FanPreferences expected)
        =>expected.Mode==0&&FanStateMatches(fan,expected);

    internal static bool FanStateMatches(System.Text.Json.Nodes.JsonNode? fan,FanPreferences expected)
    {
        if(fan?["available"]?.GetValue<bool>()!=true||fan["ecControl"]?.GetValue<int>() is not int control
            ||(control&1)!=(expected.Mode==0?0:1))return false;
        var actual=FanPreferences.Capture(fan);
        return actual.Mode==expected.Mode&&(expected.Mode!=3||actual.Curve!.Points().SequenceEqual(expected.Curve!.Points()));
    }
    private static void ApplyScreenProfile(PowerChannel power,DisplayController display,DisplayPreferences desired)
    {
        // Catalog callbacks run on the Bridge worker, never on the UWP thread.
        // Use the existing live PowerAgent connection and preserve the fresh
        // pre-change limits; a screen profile must not carry its own TDP policy.
        powerGate.Wait();
        try
        {
            var before=power.ReadAsync().GetAwaiter().GetResult();if(!before.Ok||before.State==null)throw new IOException(before.Error);
            string? factoryToken = CaptureFactoryPowerToken(power, before.State).GetAwaiter().GetResult();
            try{display.ApplyProfile(desired);}
            finally{PreserveWindowsPowerLimit(power, before.State, factoryToken, "screen-profile-preserved").GetAwaiter().GetResult();}
        }
        finally{powerGate.Release();}
    }
}
