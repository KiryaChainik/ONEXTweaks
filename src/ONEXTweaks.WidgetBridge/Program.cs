// ClawTweaks frontend protocol adapter; ONEXPLAYER hardware stays in our protected PowerAgent.
// SPDX-License-Identifier: AGPL-3.0-only
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ONEXTweaks.Backend;
using ONEXTweaks.Panel;
using ONEXTweaks.Power;
using Shared.Enums;

namespace ONEXTweaks.WidgetBridge;

internal static partial class Program
{
    [DllImport("user32.dll",EntryPoint="GetWindowThreadProcessId")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint DiagnosticWindowPid(nint window,out uint pid);
    [DllImport("user32.dll",EntryPoint="EnumChildWindows")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool DiagnosticChildWindows(nint window,DiagnosticChildCallback callback,nint parameter);
    private delegate bool DiagnosticChildCallback(nint window,nint parameter);
    internal static bool DiagnosticForegroundBelongsToClient(nint window,uint client)
    {
        if(window==0||client==0)return false;
        DiagnosticWindowPid(window,out uint pid);if(pid==client)return true;
        // ApplicationFrameHost can own the top-level HWND while the UWP content is a child.
        bool found=false;
        DiagnosticChildWindows(window,(child,unused)=>{DiagnosticWindowPid(child,out uint childPid);found=childPid==client;return !found;},0);
        return found;
    }
    private static readonly string ReportDirectory = BridgeInstallationLayout.Reports;
    private static readonly SemaphoreSlim powerGate = new(1, 1);
    private static PowerState? state;
    private static ChargeState? charge;
    private static BoostState? boost;
    private static int writes;
    private static long requests;
    private static RtssOverlay? overlay;
    private static DeviceMetrics? liveTelemetry;
    private static DisplayController? displayControl;
    private static DisplayProfiles? screenProfiles;
    private static CpuTuningService? cpuTuning;
    private static System.Text.Json.Nodes.JsonObject? homeHardware;
    private static readonly PowerPreferenceStore powerPreferences = new(Path.Combine(ReportDirectory,"..","power-preferences.json"));

    private static async Task<int> Main(string[] args)
    {
        if(args.Length==3&&args[0]=="--controller-gameinput-enumeration-probe")return GameInputEnumerationProbe.Run(args[1],args[2]);
        if(args.Length==4&&args[0]=="--controller-gameinput-api-probe")return GameInputBackgroundProbe.Run(args[1],args[2],args[3]);
        if(args.Length==2&&args[0]=="--controller-background-api-probe")return ControllerBackgroundInputProbe.Run(args[1]);
        if(args.Length==1&&args[0]==ControllerLoginRecovery.Argument)return await ControllerLoginRecovery.Run(ReportDirectory);
        if(args.Length==2&&args[0]=="--pl2-range-model-verify")return await Pl2RangeVerification.Run(args[1]);
        if(args.Length==2&&args[0]=="--game-profile-regression-verify")return await GameProfileRegressionVerification.Run(args[1]);
        if(args.Length==2&&args[0]=="--end-task-native-verify")return DesktopActions.VerifyNativeClose(args[1]);
        if(args.Length==3&&args[0]=="--intel-fps-render-verify")return await IntelFpsRenderVerification.Run(args[1],args[2]);
        if(args.Length==2&&args[0]=="--availability-recovery-model")return AvailabilityRecoveryVerification.Run(args[1]);
        if(args.Length==2&&args[0]=="--controller-login-recovery-model")return await ControllerVerification.LoginModels(args[1]);
        if(args.Length==2&&args[0]=="--controller-session-end-model")return await ControllerCaptureLease.VerifySessionEnd(args[1]);
        if(args.Length==2&&args[0]=="--controller-login-recovery-verify")return await ControllerVerification.LoginNative(args[1]);
        if(args.Length==2&&args[0]=="--controller-cloak-verify")return await ControllerVerification.Native(args[1],cloakOnly:true);
        if(args.Length==2&&args[0]=="--controller-native-verify")return await ControllerVerification.Native(args[1]);
        if(args.Length==2&&args[0]=="--controller-marker-lease-probe"){return await ControllerVerification.MarkerLease(args[1]);}
        if(args.Length==2&&args[0]=="--controller-input-lease-worker"){try{return await ControllerVerification.InputLeaseWorker(args[1]);}catch(Exception e){await File.WriteAllTextAsync(args[1]+".error.json",JsonSerializer.Serialize(new{type=e.GetType().Name,error=e.ToString().Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"<user>",StringComparison.OrdinalIgnoreCase)},Backend.Program.Json));return 3;}}
        if(args.Length==2&&args[0]=="--controller-lease-worker")return await ControllerVerification.LeaseWorker(args[1]);
        if(args.Length==3&&args[0]=="--controller-watchdog")return await ControllerCaptureLease.Watch(args[1],args[2]);
        if(args.Length is 2 or 3&&args[0]=="--controller-visibility-probe"){try{bool whitelisted;using(var hide=new ControllerHidHide())whitelisted=hide.Read().Applications.Contains(ControllerHidHide.ImagePath(Environment.ProcessPath!),StringComparer.OrdinalIgnoreCase);int[] slots=ControllerNative.Slots();bool challenge=args.Length==3&&args[2]=="--challenge-owned-virtual"&&!whitelisted&&slots.Length==1;if(challenge){try{ControllerNative.Rumble(slots[0],1,0);await Task.Delay(180);}finally{ControllerNative.Rumble(slots[0],0,0);}}await File.WriteAllTextAsync(args[1],JsonSerializer.Serialize(new{slots,frames=slots.Select(slot=>{ControllerNative.Read(slot,out var frame);return new{slot,frame};}).ToArray(),image=Path.GetFileName(Environment.ProcessPath),pid=Environment.ProcessId,whitelisted,challenge},Backend.Program.Json));return 0;}catch(Exception error){await File.WriteAllTextAsync(args[1],JsonSerializer.Serialize(new{failed=true,kind=error.GetType().Name,nativeCode=(error as System.ComponentModel.Win32Exception)?.NativeErrorCode},Backend.Program.Json));return 2;}}
        if(args.Length==2&&args[0]=="--controller-gesture-native-verify")return await ControllerOemGestureNativeVerification.Run(args[1]);
        if(args.Length==2&&args[0]=="--controller-gesture-model-verify")return await ControllerOemGestureVerification.Model(args[1]);
        if(args.Length==2&&args[0]=="--controller-ds4-production-navigation-probe")return await ControllerDs4NavigationProbe.Run(args[1],false,true);
        if(args.Length==2&&args[0]=="--controller-dualsense-navigation-probe")return await ControllerDualSenseNavigationProbe.Run(args[1]);
        if(args.Length==2&&args[0]=="--controller-dualsenseedge-navigation-probe")return await ControllerDualSenseNavigationProbe.Run(args[1],true);
        if(args.Length==2&&args[0]=="--controller-input-health-model-verify")return await ControllerInputHealthVerification.Models(args[1]);
        if(args.Length==2&&args[0]=="--controller-input-health-native-verify")return await ControllerInputHealthVerification.Native(args[1]);
        if(args.Length==2&&args[0]=="--controller-capture-groups-native-verify")return await ControllerProfileGroupsVerification.Native(args[1]);
        if(args.Length==2&&args[0]=="--controller-capture-groups-model-verify")return await ControllerProfileGroupsVerification.Run(args[1]);
        if(args.Length==2&&args[0]=="--controller-advanced-target-model-verify")return await ControllerAdvancedTargetVerification.Models(args[1]);
        if(args.Length==2&&args[0]=="--controller-advanced-target-native-verify")return await ControllerAdvancedTargetVerification.Native(args[1]);
        if(args.Length==2&&args[0]=="--controller-game-lighting-native-verify")return await ControllerGameLightingVerification.Native(args[1]);
        if(args.Length==2&&args[0]=="--controller-game-lighting-model-verify")return await ControllerGameLightingVerification.Models(args[1]);
        if(args.Length==2&&args[0]=="--controller-ds4-aux-model-verify")return await ControllerDs4AuxVerification.Models(args[1]);
        if(args.Length==2&&args[0]=="--controller-ds4-aux-overlay-verify")return await ControllerDs4AuxVerification.Transport(args[1],true);
        if(args.Length==2&&args[0]=="--controller-ds4-aux-native-verify")return await ControllerDs4AuxVerification.Transport(args[1]);
        if(args.Length==2&&args[0]=="--controller-ds4-motion-native-verify")return await ControllerDs4AuxVerification.Motion(args[1]);
        if(args.Length==2&&args[0]=="--controller-target-native-verify")return await ControllerTargetVerification.Native(args[1]);
        if(args.Length==2&&args[0]=="--controller-target-model-verify")return await ControllerTargetVerification.Models(args[1]);
        if(args.Length==2&&args[0]=="--controller-ds4-context-navigation-probe")return await ControllerDs4NavigationProbe.Run(args[1],true);
        if(args.Length==2&&args[0]=="--controller-ds4-navigation-probe")return await ControllerDs4NavigationProbe.Run(args[1]);
        if(args.Length==2&&args[0]=="--controller-ds4-capture-native-verify")return await ControllerDs4CaptureVerification.Native(args[1]);
        if(args.Length==2&&args[0]=="--controller-ds4-capture-worker")return await ControllerDs4CaptureVerification.Hold(args[1]);
        if(args.Length==3&&args[0]=="--controller-ds4-visibility-probe")return await ControllerDs4Observer.Run(args[1],args[2]);
        if(args.Length==2&&args[0]=="--controller-ds4-model-verify")return await ControllerDs4Verification.Models(args[1]);
        if(args.Length==2&&args[0]=="--controller-ds4-native-verify")return await ControllerDs4Verification.Native(args[1]);
        if(args.Length==2&&args[0]=="--controller-gyro-conversion-model-verify")return await ControllerGyroConversionVerification.Models(args[1]);
        if(args.Length==2&&args[0]=="--controller-gyro-conversion-native-verify")return await ControllerGyroConversionVerification.Native(args[1]);
        if(args.Length==2&&args[0]=="--controller-gyro-stickcurve-model-verify")return await ControllerGyroStickCurveVerification.Models(args[1]);
        if(args.Length==2&&args[0]=="--controller-gyro-stickcurve-native-verify")return await ControllerGyroStickCurveVerification.Native(args[1]);
        if(args.Length==2&&args[0]=="--controller-gyro-processing-model-verify")return await ControllerGyroProcessingVerification.Models(args[1]);
        if(args.Length==2&&args[0]=="--controller-gyro-processing-native-verify")return await ControllerGyroProcessingVerification.Native(args[1]);
        if(args.Length==2&&args[0]=="--controller-live-model-verify")return await ControllerDiagnosticsVerification.Models(args[1]);
        if(args.Length==2&&args[0]=="--controller-live-native-verify")return await ControllerDiagnosticsVerification.Native(args[1]);
        if(args.Length==2&&args[0]=="--controller-gyro-model-verify")return await ControllerGyroVerification.Models(args[1]);
        if(args.Length==2&&args[0]=="--controller-gyro-native-verify")return await ControllerGyroVerification.Native(args[1]);
        if(args.Length==2&&args[0]=="--controller-motion-model-verify")return await ControllerMotionVerification.Models(args[1]);
        if(args.Length==2&&args[0]=="--controller-motion-native-verify")return await ControllerMotionVerification.Native(args[1]);
        if(args.Length==2&&args[0]=="--controller-rumble-profile-model-verify")return await ControllerRumbleProfileVerification.Models(args[1]);
        if(args.Length==2&&args[0]=="--controller-rumble-profile-native-verify")return await ControllerRumbleProfileVerification.Native(args[1]);
        if(args.Length==2&&args[0]=="--controller-layout-model-verify")return await ControllerLayoutVerification.Models(args[1]);
        if(args.Length==2&&args[0]=="--controller-layout-native-verify")return await ControllerLayoutVerification.Native(args[1]);
        if(args.Length==2&&args[0]=="--controller-trigger-model-verify")return await ControllerTriggerVerification.Models(args[1]);
        if(args.Length==2&&args[0]=="--controller-trigger-native-verify")return await ControllerTriggerVerification.Native(args[1]);
        if(args.Length==2&&args[0]=="--keyboard-mouse-precision-model-verify")return await KeyboardMousePrecisionVerification.Models(args[1]);
        if(args.Length==2&&args[0]=="--keyboard-mouse-cursor-model-verify")return await KeyboardMouseCursorVerification.Models(args[1]);
        if(args.Length==2&&args[0]=="--keyboard-mouse-cursor-probe")return await KeyboardMouseCursorVerification.Probe(args[1]);
        if(args.Length==2&&args[0]=="--keyboard-mouse-precision-native-verify")return await KeyboardMousePrecisionVerification.Native(args[1]);
        if(args.Length==2&&args[0]=="--keyboard-mouse-taskmgr-verify")return await KeyboardMouseNativeVerification.TaskManager(args[1]);
        if(args.Length==2&&args[0]=="--keyboard-mouse-native-verify")return await KeyboardMouseNativeVerification.Run(args[1]);
        if(args.Length==2&&args[0]=="--controller-macro-native-verify")return await ControllerMacroVerification.Native(args[1]);
  if(args.Length==2&&args[0]=="--controller-macro-model-verify")return await ControllerMacroVerification.Run(args[1]);
  if(args.Length==2&&args[0]=="--controller-split-model-verify")return await ControllerSplitVerification.Run(args[1]);
        if(args.Length==2&&args[0]=="--keyboard-mouse-model-verify")return await KeyboardMouseVerification.Models(args[1]);
        if(args.Length==2&&args[0]=="--controller-steam-model-verify")return await ControllerSteamVerification.Model(args[1]);
        if(args.Length==2&&args[0]=="--controller-steam-native-verify")return await ControllerSteamVerification.Native(args[1]);
        if(args.Length==2&&args[0]=="--controller-actions-model-verify")return await ControllerActionsVerification.Model(args[1]);
        if(args.Length==2&&args[0]=="--controller-actions-native-verify")return await ControllerActionsVerification.Native(args[1]);
        if(args.Length==2&&args[0]=="--controller-vibration-model-verify")return await ControllerVibrationVerification.Model(args[1]);
        if(args.Length==2&&args[0]=="--controller-vibration-native-verify")return await ControllerVibrationVerification.Native(args[1]);
        if(args.Length==2&&args[0]=="--controller-groups-verify")return await ControllerGroupsVerification.Verify(args[1]);
        if(args.Length==2&&args[0]=="--controller-mode-model-verify")return await ControllerProfileModeVerification.Model(args[1]);
        if(args.Length==2&&args[0]=="--controller-profile-mode-native-verify")return await ControllerProfileModeVerification.Native(args[1]);
        if(args.Length==2&&args[0]=="--controller-profile-model-verify")return await ControllerVerification.Profiles(args[1]);
        if(args.Length==2&&args[0]=="--controller-oem-preset-read-check")
        {
            var preset=ControllerOemPresetReader.Read();
            await File.WriteAllTextAsync(args[1],JsonSerializer.Serialize(new{passed=true,readOnly=true,source="selected OEM controller preset",mapping1Count=preset.Mapping1.Count,mapping2Count=preset.Mapping2.Count,controllerFirmwareWrites=0,unrelatedColumnsRead=false},Backend.Program.Json));return 0;
        }
        if(args.Length==2&&args[0]=="--controller-policy-verify")return await ControllerVerification.Policy(args[1]);
        if(args.Length==2&&args[0]=="--controller-stack-probe")return await ControllerVerification.Probe(args[1]);
        if(args.Length==2&&args[0]=="--display-layout-native-verify")return await DisplayLayoutNativeVerification.Run(args[1]);
        if(args.Length==2&&args[0]=="--display-layout-read")
        {await File.WriteAllTextAsync(args[1],JsonSerializer.Serialize(new{readOnly=true,hardwareWrites=0,system=new WindowsDisplaySystem().Read(),modes=new WindowsDisplayModes().Read()},Backend.Program.Json));return 0;}
        if(args.Length==2&&args[0]=="--display-sync-state-read")
        {using var native=new IntelDisplayNative();await File.WriteAllTextAsync(args[1],JsonSerializer.Serialize(new{frameSync=native.Read3D(9),scope=native.Read3D(14),arcProfile=BitConverter.ToInt32(native.ReadArcSync(),8),hardwareWrites=0},Backend.Program.Json));return 0;}
        if(args.Length==2&&args[0]=="--display-sync-native-verify")return await DisplaySyncVerification.Run(args[1]);
        if(args.Length==2&&args[0]=="--overlay-preset-model-verify")return await OverlayPresetVerification.Run(args[1]);
        if(args.Length==2&&args[0]=="--home-profile-override-model-verify")return await HomeProfileOverrideVerification.Run(args[1]);
        if(args.Length==2&&args[0]=="--profile-sources-model-verify")return await ProfileSourcesVerification.Run(args[1]);
        if(args.Length==2&&args[0]=="--profile-metadata-model-verify")return await ProfileMetadataVerification.Run(args[1]);
        if(args.Length==2&&args[0]=="--independent-screen-model-verify")return await IndependentScreenVerification.Run(args[1]);
        if(args.Length==2&&args[0]=="--scoped-profile-model-verify")return await ScopedProfileVerification.Run(args[1]);
        if(args.Length==2&&args[0]=="--fan-source-model-verify")return await FanSourceVerification.Run(args[1]);
        if(args.Length==2&&args[0]=="--fan-editor-model-verify")return await FanEditorVerification.Run(args[1]);
        if(args.Length==2&&args[0]=="--fan-curve-values-model-verify")return await FanPreferencesVerification.Run(args[1]);
        if(args.Length==2&&args[0]=="--fan-profile-model-verify")return await FanProfileVerification.Run(args[1]);
        if(args.Length==2&&args[0]=="--profile-link-model-verify")return await ProfileLinkVerification.Run(args[1]);
        if(args.Length==2&&args[0]=="--cpu-tuning-native-read"){await File.WriteAllTextAsync(args[1],JsonSerializer.Serialize(new CpuPowerPoliciesNative().Read(),Backend.Program.Json));return 0;}
        if(args.Length==2&&args[0]=="--performance-profile-model-verify")return await PerformanceManagerVerification.Run(args[1]);
        if(args.Length==2&&args[0]=="--sharpness-probe")
        {using var display=new IntelDisplayNative();await File.WriteAllTextAsync(args[1],JsonSerializer.Serialize(display.ProbeSharpness(),Backend.Program.Json));return 0;}
        if(args.Length==2&&args[0]=="--sharpness-hdr-verify")return await SharpnessNativeVerification.Run(args[1]);
        if(args.Length==3&&args[0]=="--sharpness-native-watchdog")return await SharpnessNativeVerification.Watchdog(args[1],args[2]);
        if(args.Length==2&&args[0]=="--scaling-mode-verify")return await ScalingNativeVerification.Run(args[1]);
        if(args.Length==2&&args[0]=="--scaling-method-verify")return await ScalingNativeVerification.Run(args[1],methods:true);
        if(args.Length==3&&args[0]=="--scaling-native-watchdog")return await ScalingNativeVerification.Watchdog(args[1],args[2]);
        if(args.Length==2&&args[0]=="--scaling-native-restore")return await ScalingNativeVerification.RestoreFile(args[1]);
        if(args.Length==3&&args[0]=="--scaling-mode-ui-verify")return await ScalingNativeVerification.Run(args[1],uiFolder:args[2]);
        if(args.Length==3&&args[0]=="--scaling-method-ui-verify")return await ScalingNativeVerification.Run(args[1],methods:true,uiFolder:args[2]);
        if(args.Length==2&&args[0]=="--hdr-native-snapshot")
        {await File.WriteAllTextAsync(args[1],JsonSerializer.Serialize(new WindowsHdrDisplay().Snapshot(),Backend.Program.Json));return 0;}
        if(args.Length==2&&args[0]=="--hdr-native-verify")return await HdrNativeVerification.Run(args[1]);
        if(args.Length==2&&args[0]=="--hdr-native-restore")return await HdrNativeVerification.Restore(args[1]);
        if(args.Length==3&&args[0]=="--hdr-native-watchdog")return await HdrNativeVerification.Watchdog(args[1],args[2]);
        if(args.Length==2&&args[0]=="--hdr-probe")
        {await File.WriteAllTextAsync(args[1],JsonSerializer.Serialize(new WindowsHdrDisplay().Read(),Backend.Program.Json));return 0;}
        if(args.Length==2&&args[0]=="--hdr-profile-diagnostics")
        {await File.WriteAllTextAsync(args[1],JsonSerializer.Serialize(new WindowsHdrDisplay().ProfileDiagnostics(),Backend.Program.Json));return 0;}
        if(args.Length==2&&args[0]=="--awake-native-verify")return await AwakeNativeVerification.Run(args[1]);
        if(args.Length==2&&args[0]=="--startup-verify")return await StartupVerification.Run(args[1]);
        if(args.Length==2&&args[0]=="--startup-probe")
        {await File.WriteAllTextAsync(args[1],JsonSerializer.Serialize(StartupController.Read(),Backend.Program.Json));return 0;}
        if(args.Length==2&&args[0]=="--display-native-restore"){await DisplayNativeVerification.Restore(args[1],File.Exists(args[1]+".colors-started"));return 0;}
        if(args.Length==3&&args[0]=="--display-native-watchdog")return await DisplayNativeVerification.Watchdog(args[1],args[2]);
        if(args.Length==2&&args[0]=="--display-profile-verify")return await DisplayProfileVerification.Run(args[1]);
        if(args.Length==2&&args[0]=="--display-native-verify")return await DisplayNativeVerification.Run(args[1]);
        if (args.Length == 2 && args[0] == "--display-color-readback")
        {
            var modes = new WindowsDisplayModes().Read();
            using var display = new DisplayController(Path.Combine(ReportDirectory, "..", "display-settings.json"));
            var state = display.Read();
            var blocks = new List<object>();
            string? readError = null;
            try
            {
                using var native = new IntelDisplayNative();
                native.RefreshOutput();
                foreach (var block in native.ReadPixelCaps().Where(b => b.Type == 3 ||
                    b.Type == 1 && b.Sampling == 0 && b.Channels == 3 && b.Samples is >= 2 and <= 8192))
                {
                    var read = native.ReadPixel(block);
                    blocks.Add(new { block, read.Result, matrixLength = read.Matrix?.Length, samplesLength = read.Samples?.Length,
                        finite = (read.Matrix ?? read.Samples)?.All(double.IsFinite) });
                }
            }
            catch (Exception error) { readError = error.Message; }
            await File.WriteAllTextAsync(args[1], JsonSerializer.Serialize(new { readOnly = true, hardwareWrites = 0,
                modes, state, blocks, readError }, Backend.Program.Json));
            return 0;
        }
        if(args.Length==2&&args[0]=="--display-probe")
        {using var display=new DisplayController(Path.Combine(ReportDirectory,"..","display-settings.json"));await File.WriteAllTextAsync(args[1],JsonSerializer.Serialize(display.Read(),Backend.Program.Json));return 0;}
        if(args.Length==2&&args[0]=="--pl2-selection-verify")return await PowerSelectionVerification.Run(args[1]);
        if(args.Length==2&&args[0]=="--audio-input-probe")
        {await File.WriteAllTextAsync(args[1],JsonSerializer.Serialize(new AudioDevicesController(1).Read(),Backend.Program.Json));return 0;}
        if(args.Length==2&&args[0]=="--audio-input-same-defaults-verify")
        {await File.WriteAllTextAsync(args[1],JsonSerializer.Serialize(new AudioDevicesController(1).VerifySameDefaults(),Backend.Program.Json));return 0;}
        if(args.Length==2&&args[0]=="--audio-same-defaults-verify")
        {await File.WriteAllTextAsync(args[1],JsonSerializer.Serialize(new AudioDevicesController().VerifySameDefaults(),Backend.Program.Json));return 0;}
        if(args.Length==2&&args[0]=="--audio-devices-probe")
        {await File.WriteAllTextAsync(args[1],JsonSerializer.Serialize(new AudioDevicesController().Read(),Backend.Program.Json));return 0;}
        if(args.Length==2&&args[0]=="--airplane-probe")
        {await File.WriteAllTextAsync(args[1],JsonSerializer.Serialize(new AirplaneController().Read(),Backend.Program.Json));return 0;}
        if(args.Length==2&&args[0]=="--bluetooth-transport-probe")
        {await File.WriteAllTextAsync(args[1],JsonSerializer.Serialize(BluetoothConnections.Probe(),Backend.Program.Json));return 0;}
        if(args.Length==2&&args[0]=="--entry-queue-verify")return await EntryQueueVerification.Run(args[1]);
        if(args.Length==2&&args[0]=="--touch-corner-probe")
        {
            using var probe = new ControllerOemRawInput(() => {});
            await Task.Delay(600);
            await File.WriteAllTextAsync(args[1], JsonSerializer.Serialize(probe.Read(), Backend.Program.Json));
            return 0;
        }
        if(args.Length==2&&args[0]=="--controller-actions-verify")return await DesktopControllerVerification.Verify(args[1]);
        if(args.Length==2&&args[0]=="--tile-hotkey-verify")
        {
            var filter=new TileChordFilter();var bindings=new[]{new TileBinding("one",0x1100),new TileBinding("two",0x2100)};
            if(filter.Observe(true,0x1100,bindings)!=null)throw new IOException("Held startup triggered");filter.Observe(true,0,bindings);
            if(filter.Observe(true,0x1100,bindings)!="one"||filter.Observe(true,0x1100,bindings)!=null)throw new IOException("Chord rising edge failed");
            filter.Observe(false,0,bindings);if(filter.Observe(true,0x2100,bindings)!=null)throw new IOException("Reconnection triggered held chord");filter.Observe(true,0,bindings);if(filter.Observe(true,0x2100,bindings)!="two")throw new IOException("Rearmed chord failed");
            await File.WriteAllTextAsync(args[1],JsonSerializer.Serialize(new{passed=true,checks=new[]{"startup held ignored","release arms","one event per chord","disconnect resets","reconnect held ignored","clean release rearms"},physicalControllerTested=false},Backend.Program.Json));return 0;
        }
        if(args.Length==2&&args[0]=="--intel-fps-probe")
        {using var intel=new IntelFrameLimit();await File.WriteAllTextAsync(args[1],JsonSerializer.Serialize(intel.Read(),Backend.Program.Json));return 0;}
        if(args.Length==3&&args[0]=="--intel-fps-app-inspect")
        {using var intel=new IntelFrameLimit();await File.WriteAllTextAsync(args[2],JsonSerializer.Serialize(intel.InspectApplication(args[1]),Backend.Program.Json));return 0;}
        if(args.Length==2&&args[0]=="--windows-power-verify")
        {
            var before=WindowsPowerSettings.Read();if(!before.Available||before.Cpu is null)throw new InvalidOperationException(before.Error);
            var cases=new List<object>();
            try
            {
                foreach(int mode in new[]{0,1,2}){var actual=WindowsPowerSettings.SetMode(mode);if((before.OnAc?actual.ModeAc:actual.ModeDc)!=mode)throw new IOException("Power mode readback failed");cases.Add(actual);}
                var off=WindowsPowerSettings.SetCpu(before.Cpu with{BoostAc=0,BoostDc=0});if(off.Cpu!.BoostAc!=0||off.Cpu.BoostDc!=0)throw new IOException("CPU boost readback failed");cases.Add(off);

            }
            finally{try{WindowsPowerSettings.SetMode(before.OnAc?before.ModeAc:before.ModeDc);}finally{WindowsPowerSettings.SetCpu(before.Cpu);}}
            var restored=WindowsPowerSettings.Read();if(restored.Cpu!.BoostAc!=before.Cpu.BoostAc||restored.Cpu.BoostDc!=before.Cpu.BoostDc||restored.ModeAc!=before.ModeAc||restored.ModeDc!=before.ModeDc)throw new IOException("Windows power settings not restored");
            await File.WriteAllTextAsync(args[1],JsonSerializer.Serialize(new{passed=true,before,cases,restored,powerActions=new[]{"poweroff","reboot","sleep","hibernate","bios"}.Select(SystemPowerActions.Plan),powerActionsExecuted=false},Backend.Program.Json));return 0;
        }
        if(args.Length==2&&args[0]=="--windows-power-probe")
        {await File.WriteAllTextAsync(args[1],JsonSerializer.Serialize(WindowsPowerSettings.Read(),Backend.Program.Json));return 0;}
        if(args.Length==2&&args[0]=="--brightness-dim-state-probe")
        {await File.WriteAllTextAsync(args[1],JsonSerializer.Serialize(await new MediaController(Path.Combine(ReportDirectory,"..","brightness-dim.json")).ReadAsync(),Backend.Program.Json));return 0;}
        if (args.Length == 2 && args[0] == "--media-probe")
        {
            await File.WriteAllTextAsync(args[1], JsonSerializer.Serialize(await new MediaController().ReadAsync(), Backend.Program.Json));
            return 0;
        }
        if(args.Length==3 && args[0]=="--keyboard-probe")
        {
            try
            {
                TouchKeyboardState keyboard;
                if(args[1]=="show")
                {
                    using var desktopProbe=new DesktopActions();
                    var shown=await desktopProbe.Execute("keyboard",null,true);
                    if(!shown.Ok||shown.Keyboard is null)throw new InvalidOperationException(shown.Error);
                    keyboard=shown.Keyboard;
                }
                else keyboard=await TouchKeyboard.ReadAsync();
                await File.WriteAllTextAsync(args[2],JsonSerializer.Serialize(new {ok=true,keyboard},Backend.Program.Json));
                return 0;
            }
            catch(Exception e)
            {
                await File.WriteAllTextAsync(args[2],JsonSerializer.Serialize(new {ok=false,error=e.ToString()},Backend.Program.Json));
                return 1;
            }
        }
        if(args.Length==2&&args[0]=="--rtss-probe-profile")
        {RtssProfile.Apply(new Dictionary<string,byte[]>{{"AppDetectionLevel",BitConverter.GetBytes(3u)},{"EnableOSD",BitConverter.GetBytes(1u)}},"ONEXRenderProbe.exe");await File.WriteAllTextAsync(args[1],"configured");return 0;}
        if(args.Length==2&&args[0]=="--rtss-properties")
        {
            var props=new Dictionary<string,object>();foreach(string name in new[]{"AppDetectionLevel","EnableOSD","FontHeight","FontWeight","Implementation","FramerateLimit"}){try{props[name]=BitConverter.ToInt32(RtssProfile.Read(name));}catch(Exception ex){props[name]=ex.Message;}}
            await File.WriteAllTextAsync(args[1],JsonSerializer.Serialize(props,Backend.Program.Json));return 0;
        }
        if(args.Length==2&&args[0]=="--rtss-verify")
        {
            Directory.CreateDirectory(ReportDirectory);var probeSampler=new WindowsSampler();
            using var probe=new RtssOverlay(ReportDirectory,()=>new(probeSampler.Read(),null,null,null));
            var before=probe.Preferences;var levels=new List<object>();
            var originalRtss=new Dictionary<string,byte[]>();var originalBg=RtssProfile.ReadBackground();
            foreach(string name in new[]{"EnableOSD","FontFace","FontHeight","FontWeight"})originalRtss[name]=RtssProfile.Read(name,name=="FontFace"?260:4);
            try
            {
                probe.SetConfig("Font:Bahnschrift|400;TextSize:150;Background:1|60;Opacity:90");
                foreach(int level in new[]{1,2,3,4,0}){var actual=probe.SetLevel(level);if(!actual.Available||level>0&&(actual.Slot<1||string.IsNullOrEmpty(actual.Text)))throw new IOException(actual.Error??"RTSS OSD verification failed");levels.Add(actual);}
                await File.WriteAllTextAsync(args[1],JsonSerializer.Serialize(new{passed=true,levels},Backend.Program.Json));
            }
            catch(Exception ex){await File.WriteAllTextAsync(args[1],JsonSerializer.Serialize(new{passed=false,error=ex.ToString(),levels},Backend.Program.Json));return 1;}
            finally{probe.SetConfig(before.Config);probe.SetLevel(before.Level);RtssProfile.Apply(originalRtss);RtssProfile.SetBackground(originalBg.Enabled,originalBg.Color);}
            return 0;
        }
        using var singleton = new Mutex(true, @"Local\ONEXTweaks.WidgetBridge.v1", out bool created);
        if (!created) return 0;
        Directory.CreateDirectory(ReportDirectory);
        try{await ControllerCaptureLease.RecoverStale(ControllerCaptureLease.LeaseFile(ReportDirectory));}catch(Exception e){Log(e);}
        // Own the opening button before potentially slow device/driver initialization.
        using var gameBarEntry = new GameBarEntry(ReportDirectory,deferStart:true);
        await using var guideButton = new GuideButtonLauncher(ReportDirectory, gameBarEntry);
        using var gameBarKeyboard = new NativeGameBarKeyboard(()=>{if(!GamingExperience.Read().Active)gameBarEntry.EnqueueKeyboardOpen();});
        gameBarEntry.EnableStartup();
        using var oemButtons = new ControllerOemRawInput(()=>gameBarEntry.EnqueueTouchCornerOpen(),
            ()=>ControllerCaptureLease.PrepareForSessionEnd(ControllerCaptureLease.LeaseFile(ReportDirectory)));
        using var oemStartup = BridgeInstallationLayout.IsInstalled ? null : new OemConsoleStartupGuard(ReportDirectory);
        using var power = new PowerChannel();
        await RestorePreferredPower(power);
        var sampler = new WindowsSampler();
        using var sensors=new DeviceTelemetry();
        await using var overlayTelemetry=new OverlayTelemetry(()=>RefreshPower(power),()=>state,()=>homeHardware?["fan"]?["percent"]?.GetValue<double>());
        using var overlayController=new RtssOverlay(ReportDirectory,()=>overlayTelemetry.Sample);overlay=overlayController;
        OverlayPresetCatalog? overlayCatalog=null;string? overlayPresetError=null;
        try{overlayCatalog=new OverlayPresetCatalog(Path.Combine(ReportDirectory,"..","overlay-presets.json"),()=>overlayController.Preferences,overlayController.SetPreferences);}catch(Exception failure){overlayPresetError=failure.Message;Log(failure);}
        using var fpsController=new FrameLimiter(ReportDirectory);
        using var displayController = new DisplayController(Path.Combine(ReportDirectory, "..", "display-settings.json"),
            change => ChangeWindowsPower(power, change), Log);
        displayControl = displayController;
        var media = new MediaController(Path.Combine(ReportDirectory,"..","brightness-dim.json"));
        try{await media.RestoreAwakeDimAsync(null);}catch(Exception e){Log(e);}
        var audioDevices=new AudioDevicesController();
        var audioInputs=new AudioDevicesController(1);
        var audioMixer=new AudioMixerController();
        var airplane=new AirplaneController();var wifi=new WifiController(airplane);
        using var awake=new AwakeController(Path.Combine(ReportDirectory,"..","awake-settings.json"),new NativeAwakePlatform(media));
        async Task<string> ReadAudioMixer()
        {var apps=audioMixer.Read();return JsonSerializer.Serialize(new{apps.Available,apps.Apps,apps.Error,master=await media.ReadAsync()},Backend.Program.Json);}

        using var desktop = new DesktopActions();
        Func<string,Task>? tileHotkeySender=null;
        await using var tileHotkeys=new TileHotkeys(ReportDirectory,async id=>{await gameBarEntry.PrepareWidgetAsync();var sender=Volatile.Read(ref tileHotkeySender);if(sender!=null)await sender(id);});
        await using var homeControl = new HomeControlHeartbeat(power, powerGate, value => homeHardware = value, Log);
        var gameDetector=new ProfileGameDetector(()=>overlay.Read().Applications,Log,Path.Combine(ReportDirectory,"..","profile-context.json"));
        desktop.IsGameRunning=()=>gameDetector.Read()!=null;
        var screenSessions=new ProfileSessions(()=>overlay.Read().Applications,gameDetector.Read);
        desktop.RetainedApplication=screenSessions.Retained;
        cpuTuning=new CpuTuningService(ReportDirectory);
        var performanceRuntime = new PerformanceProfileRuntime(screenSessions.Current,ProfileSessions.Alive,screenSessions.Candidates,null,async policy=>
        {if(cpuTuning.Matches(policy))return;await ChangeWindowsPower(power,()=>{cpuTuning.Repair(policy);return true;});},CaptureTransition:()=>CaptureProfileTransition(power,fpsController,overlay));
        await using var profiles = power.HardwareConfigured
            ? await PerformanceProfiles.Create(ReportDirectory,()=>CaptureProfileValues(power,fpsController,overlay),
                value=>ApplyProfileValues(power,fpsController,overlay,value),gameDetector.Read,WindowsPowerSettings.IsOnAc,performanceRuntime)
            : PerformanceProfiles.WithoutHardware(ReportDirectory,WindowsPowerSettings.IsOnAc,performanceRuntime);
        await using var screenCatalog=new DisplayProfiles(ReportDirectory,displayController,profiles.Gate,screenSessions,Log,value=>ApplyScreenProfile(power,displayController,value),profiles.ActiveScreenProfile);screenProfiles=screenCatalog;
        await using var fanCatalog=power.HardwareConfigured?new FanProfiles(ReportDirectory,profiles.Gate,screenSessions,()=>FanPreferences.Capture(homeHardware?["fan"]),(value,owned)=>ApplyFanProfile(power,value,owned),Log,null,()=>profiles.CurrentSessionStamp.ToString(CultureInfo.InvariantCulture),observe:()=>homeHardware?["fan"],restore:value=>ApplyFanProfile(power,value,false,recoverOnly:true),independent:true):null;
        (string Id,string Name)[] ControllerParents()
        {if(!profiles.Available)return [];using var data=JsonDocument.Parse(profiles.Read());return data.RootElement.GetProperty("entries").EnumerateArray().Select(entry=>(entry.GetProperty("id").GetString()!,entry.GetProperty("name").GetString()!)).ToArray();}
        ControllerProfiles? preparedController=null;ControllerOemManager? preparedOem=null;string? controllerError=null;
        try{preparedController=new ControllerProfiles(ReportDirectory,()=>profiles.ActiveId+":"+profiles.CurrentSessionStamp,()=>profiles.ActiveId,ControllerParents,oemButtons,keyboardSettings:new KeyboardMouseSettings(ReportDirectory),power:power,session:()=>profiles.CurrentSession);preparedOem=new ControllerOemManager(oemButtons,power,()=>preparedController.CurrentOem,gameBarEntry,async()=>{await profiles.Gate.WaitAsync();try{await preparedController.SetKeyboardMouseMode(!preparedController.KeyboardMouseActive);}finally{profiles.Gate.Release();}},preparedController.ResolveOemAction,KeyboardMouseWindowsSecurity.Show,preparedController.NeutralizeSystemChord);preparedController.BindOemManager(preparedOem);await preparedController.Resume();}catch(Exception e){controllerError=e.Message;Log(e);}
        await using var oemRouting=preparedOem;
        await using var controller=preparedController;
        await using var sticksHost=new ControllerDiagnosticHostSession(controller,profiles.Gate,gameBarEntry);
        using var controllerRuntimeStop=new CancellationTokenSource();
        async Task MaintainControllerRuntime()
        {
            if(controller==null)return;
            using var timer=new PeriodicTimer(TimeSpan.FromSeconds(1));
            var token=controllerRuntimeStop.Token;
            try
            {
                while(await timer.WaitForNextTickAsync(token))
                {
                    await profiles.Gate.WaitAsync(token);
                    try{await controller.MaintainRuntime();if(power.Connected)await controller.MaintainOem();}
                    catch(Exception error){Log(error);}
                    finally{profiles.Gate.Release();}
                }
            }
            catch(OperationCanceledException) when(token.IsCancellationRequested){}
        }
        var controllerRuntime=MaintainControllerRuntime();
        string KeyboardMousePayload()=>JsonSerializer.Serialize(controller?.ReadKeyboardMouse()??(object)new{available=false,error=controllerError??"Keyboard/mouse settings unavailable"},Backend.Program.Json);
        string ControllerPayload()=>JsonSerializer.Serialize(controller?.Read()??(object)new{available=false,error=controllerError??"Controller settings are unavailable"},Backend.Program.Json);
        long telemetrySequence = 0;
        string? lastChargeError=null;
        int repairedOemHandoffs=0;
        try
        {
            while (true)
            {
                using var pipe = WidgetPipe.Create();
                await pipe.WaitForConnectionAsync();
                try
                {
                    WidgetPipe.VerifyClient(pipe);
                    using var session = new CancellationTokenSource();
                    using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
                    using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
                    using var sendGate = new SemaphoreSlim(1, 1);
                    bool widgetSession=false;
                    string? lastPushedRaw = null;
                    async Task Send(object message)
                    {
                        await sendGate.WaitAsync(session.Token);
                        try { await writer.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(), session.Token); }
                        finally { sendGate.Release(); }
                    }
                    tileHotkeySender=id=>Send(new{RequestId=0,TileHotkeyFired=id});
                    var ticker = Task.Run(async () =>
                    {
                        try
                        {
                            while (!session.IsCancellationRequested)
                            {
                                await RefreshPower(power);
                                int handoffs=oemStartup?.HandoffCount??0;
                                if(handoffs>repairedOemHandoffs&&state?.CanWrite==true)
                                {
                                    await profiles.Gate.WaitAsync(session.Token);
                                    try
                                    {
                                        await powerGate.WaitAsync(session.Token);
                                        try{if(await RestorePreferredPower(power,"oem-startup-handoff"))repairedOemHandoffs=handoffs;}
                                        finally{powerGate.Release();}
                                    }
                                    finally{profiles.Gate.Release();}
                                }
                                var metrics = sampler.Read();
                                liveTelemetry=sensors.Read();
                                telemetrySequence++;
                                if(telemetrySequence%5==1)await Send(new{RequestId=0,OnexStartupState=JsonSerializer.Serialize(StartupController.Read(),Backend.Program.Json)});
                                await Send(new { RequestId = 0, Command = 3, Function = (int)Function.QuickMetrics, Content = Metrics(metrics) });
                                await Send(new { RequestId = 0, OnexPowerAvailable = state?.CanWrite == true, OnexPowerState=PowerStatePayload() });
                                if(profiles.Available)await Send(new{RequestId=0,OnexProfilesState=profiles.Read()});
                                await Send(new{RequestId=0,OnexDisplayProfilesState=screenCatalog.Read()});
                                if(fanCatalog!=null)await Send(new{RequestId=0,OnexFanProfilesState=fanCatalog.Read()});
                                await profiles.Gate.WaitAsync(session.Token);try{controller?.Refresh();if(telemetrySequence%5==1)await Send(new{RequestId=0,OnexControllerState=ControllerPayload()});}finally{profiles.Gate.Release();}
                                if(telemetrySequence%5==1)await Send(new{RequestId=0,OnexKeyboardMouseState=KeyboardMousePayload()});
                                await Send(new{RequestId=0,OnexWifiState=JsonSerializer.Serialize(wifi.Read(),Backend.Program.Json)});
                                await Send(new{RequestId=0,OnexAirplaneState=JsonSerializer.Serialize(airplane.Read(),Backend.Program.Json)});
                                await Send(new{RequestId=0,OnexAwakeState=JsonSerializer.Serialize(awake.Read(),Backend.Program.Json)});
                                await Send(new{RequestId=0,OnexEnergySaverState=JsonSerializer.Serialize(EnergySaverController.Read(),Backend.Program.Json)});
                                await Send(new{RequestId=0,OnexFseState=JsonSerializer.Serialize(GamingExperience.Read(),Backend.Program.Json)});
                                await Send(new{RequestId=0,OnexFpsState=JsonSerializer.Serialize(fpsController.Read(),Backend.Program.Json)});
                                if(telemetrySequence%5==0)await Send(new{RequestId=0,OnexDisplayState=JsonSerializer.Serialize(displayController.ReadForWidget(),Backend.Program.Json)});
                                await Send(new{RequestId=0,OnexOverlayState=JsonSerializer.Serialize(overlay.Read(),Backend.Program.Json)});
                                var windowsPower=WindowsPowerSettings.Read();
                                await Send(new {RequestId=0,OnexWindowsPowerState=JsonSerializer.Serialize(windowsPower,Backend.Program.Json)});
                                var mediaLevels = await media.ReadAsync();
                                await Send(new { RequestId = 0, MediaLevels = JsonSerializer.Serialize(mediaLevels, Backend.Program.Json) });
                                if(power.HardwareConfigured)
                                {
                                    await powerGate.WaitAsync(session.Token);
                                    try
                                    {
                                        var c=await power.ReadChargeAsync();charge=c.Ok?c.Charge:null;
                                        if(!c.Ok&&c.Error!=null&&c.Error!=lastChargeError)Log(new IOException(c.Error));
                                        lastChargeError=c.Ok?null:c.Error;
                                    }
                                    catch (Exception e) {charge=null;Log(e);}
                                    finally { powerGate.Release(); }
                                    await powerGate.WaitAsync(session.Token);
                                    try{var b=await power.ReadBoostAsync();boost=b.Ok?b.Boost:null;}
                                    catch(Exception e){boost=null;Log(e);}
                                    finally{powerGate.Release();}
                                }
                                await Send(new{RequestId=0,OnexHardwareHomeState=JsonSerializer.Serialize(homeHardware,Backend.Program.Json)});
                                await Send(new {RequestId=0,OnexBoostState=JsonSerializer.Serialize(boost,Backend.Program.Json)});
                                await Send(new { RequestId=0, OnexChargeState=JsonSerializer.Serialize(charge, Backend.Program.Json) });
                                await Send(new { RequestId = 0, OnexDesktopState = JsonSerializer.Serialize(desktop.Read(), Backend.Program.Json) });
                                if (state is { } actual && PowerSelectionSignature(actual) != lastPushedRaw)
                                {
                                    foreach (var f in new[] { Function.TDP, Function.CurrentTDP, Function.TDPBoostFPPT, Function.TDPBoostEnabled })
                                        if (Value(f) is { } value) await Send(Push(f, value));
                                    lastPushedRaw = PowerSelectionSignature(actual);
                                }
                                await File.WriteAllTextAsync(Path.Combine(ReportDirectory, "widget-bridge.json"), JsonSerializer.Serialize(new
                                {
                                    version = typeof(Program).Assembly.GetName().Version?.ToString(3), connected = true,
                                    processId = Environment.ProcessId, sessionId = System.Diagnostics.Process.GetCurrentProcess().SessionId,
                                    widgetPackageFamily = WidgetPackageIdentity.FamilyName,
                                    requests, hardwareWrites = writes, state, charge, boost, homeHardware, windowsPower, mediaLevels, telemetry=liveTelemetry, telemetrySequence,profileGateAvailable=profiles.Gate.CurrentCount,powerGateAvailable=powerGate.CurrentCount,
                                    controllerDiagnostic=controller?.DiagnosticSnapshot,
                                    updatedUtc = DateTimeOffset.UtcNow
                                }, Backend.Program.Json));
                                await Task.Delay(1000, session.Token);
                            }
                        }
                        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { session.Cancel(); }
                        catch (Exception e) { Log(e); session.Cancel(); }
                    });
                    try
                    {
                        // Replies and async metrics share a serialized writer. Only explicit function
                        // IDs below can reach hardware; arbitrary upstream tools never execute here.
                        while (pipe.IsConnected && !session.IsCancellationRequested)
                        {
                            string? line = await ReadBounded(reader, session.Token);
                            if (line is null) break;
                            using var doc = JsonDocument.Parse(line);
                            var request = doc.RootElement;
                            Interlocked.Increment(ref requests);
                            int id = Number(request, "RequestId");
                            int command = Number(request, "Command", -1);
                            var function = (Function)Number(request, "Function");
                            var reply = new Dictionary<string, object?> { ["RequestId"] = id, ["Command"] = 3, ["Function"] = (int)function };
                            var powerOutcome=new PowerCommitOutcome877();bool? powerWriteOnAc=null;bool cpuTuningApplied=false;CpuTuningPolicy? previousCpuTuning=null;CpuPowerSettings? previousCpuWindows=null;
                            await profiles.Gate.WaitAsync();
                            try
                            {
                                // Read requests must remain available when automatic profile
                                // synchronization is deferred (for example OEM PL2 above the
                                // app's writable contract after resume). The worker owns automatic
                                // synchronization; explicit edits retain its strict validation.
                                if(ProfileSyncRequired(request))
                                    await profiles.RefreshPowerSource();
                                if(request.TryGetProperty("OnexFanProfileId",out var expectedFan)&&expectedFan.GetString()!=fanCatalog?.ActiveId)throw new InvalidOperationException("Fan profile changed; repeat the adjustment");
                                if(request.TryGetProperty("OnexOverlayPresetId",out var expectedOverlay)&&overlayCatalog!=null)overlayCatalog.EnsureActive(expectedOverlay.GetString()??"");
                                if(request.TryGetProperty("OnexProfileId",out var expectedScope)&&expectedScope.GetString()!=profiles.ActiveId)
                                    throw new InvalidOperationException("Performance profile changed before the setting was applied; try again.");
                                if(request.TryGetProperty("OnexStartupGet",out _)||request.TryGetProperty("OnexStartupSet",out _))
                                {
                                    StartupState startup;
                                    if(request.TryGetProperty("OnexStartupSet",out var startupOn))
                                    {
                                        if(!request.TryGetProperty("OnexUserInput",out var input)||input.ValueKind!=JsonValueKind.True)throw new InvalidOperationException("Startup changes require explicit input");
                                        if(startupOn.ValueKind is not(JsonValueKind.True or JsonValueKind.False))throw new ArgumentException("Startup state must be boolean");
                                        startup=StartupController.Set(startupOn.GetBoolean());
                                        if(!startup.Available)throw new IOException(startup.Error);
                                    }
                                    else startup=StartupController.Read();
                                    reply["OnexStartupState"]=JsonSerializer.Serialize(startup,Backend.Program.Json);
                                }
                                else if(request.TryGetProperty("OnexControllerHardwareGet",out _))
                                {
                                    var readback=await power.ReadControllerHardwareAsync();
                                    if(!readback.Ok||readback.Controller==null)throw new IOException(readback.Error??"Controller firmware readback unavailable");
                                    reply["OnexControllerHardwareState"]=JsonSerializer.Serialize(readback.Controller,Backend.Program.Json);
                                }
                                else if(request.TryGetProperty("OnexKeyboardMouseGet",out _)||request.TryGetProperty("OnexKeyboardMouseSet",out _))
                                {
                                    if(controller==null)throw new InvalidOperationException(controllerError??"Controller module unavailable");
                                    if(request.TryGetProperty("OnexKeyboardMouseSet",out var mutation)){if(!request.TryGetProperty("OnexUserInput",out var input)||input.ValueKind!=JsonValueKind.True)throw new InvalidOperationException("Keyboard/mouse changes require explicit input");using var change=JsonDocument.Parse(mutation.GetString()??"{}");await controller.MutateKeyboardMouse(change.RootElement);}
                                    reply["OnexKeyboardMouseState"]=KeyboardMousePayload();reply["OnexControllerState"]=ControllerPayload();
                                }
                                else if(request.TryGetProperty("OnexControllerDiagnosticHost",out var hostAction))
                                {
                                    if(!widgetSession)throw new UnauthorizedAccessException("Open the controller test from the widget");
                                    WidgetPipe.VerifyApplication(pipe,"App");
                                    if(!Guid.TryParseExact(request.GetProperty("OnexDiagnosticWindowOwner").GetString(),"N",out var owner)||owner==Guid.Empty)
                                        throw new ArgumentException("Invalid controller test owner");
                                    if(hostAction.GetString()=="start")
                                        sticksHost.Start(owner,request.TryGetProperty("OnexVerifyHost",out var verify)&&verify.ValueKind==JsonValueKind.True,request.TryGetProperty("OnexDiagnosticKind",out var diagnosticKind)?diagnosticKind.GetString()??"sticks":"sticks");
                                    else if(hostAction.GetString()!="status")throw new ArgumentException("Unknown controller test request");
                                    reply["OnexControllerHostState"]=sticksHost.Status(owner);
                                }
                                else if(request.TryGetProperty("OnexControllerDiagnosticGet",out var diagnosticToken))
                                {
                                    if(controller==null)throw new InvalidOperationException(controllerError??"Controller module unavailable");
                                    reply["OnexControllerDiagnosticState"]=JsonSerializer.Serialize(controller.DiagnosticRead(diagnosticToken.GetString()??""),Backend.Program.Json);
                                }
                                else if(request.TryGetProperty("OnexLinkedProcessCandidatesGet",out _))
                                {
                                    reply["OnexLinkedProcessCandidates"]=profiles.Candidates();
                                }
                                else if(request.TryGetProperty("OnexControllerGet",out _)||request.TryGetProperty("OnexControllerSet",out _))
                                {
                                    if(controller==null)throw new InvalidOperationException(controllerError??"Controller module unavailable");
                                    if(request.TryGetProperty("OnexControllerSet",out var mutation))
                                    {
                                        if(!request.TryGetProperty("OnexUserInput",out var input)||input.ValueKind!=JsonValueKind.True)throw new InvalidOperationException("Controller changes require explicit input");
                                        using var change=JsonDocument.Parse(mutation.GetString()??"{}");
                                        if(change.RootElement.TryGetProperty("processId",out _))
                                            await controller.CreateForProcess(profiles.ResolveLinkedProcess(change.RootElement));
                                        else await controller.Mutate(change.RootElement);
                                    }
                                    if(request.TryGetProperty("OnexControllerRefreshInstallation",out var refresh)&&refresh.ValueKind==JsonValueKind.True){ControllerViiperInstallation.Read(true);await controller.RefreshInputHealth();}
                                    reply["OnexControllerState"]=ControllerPayload();
                                }
                                else if(request.TryGetProperty("OnexFanProfilesGet",out _)||request.TryGetProperty("OnexFanProfilesSet",out _))
                                {
                                    if(fanCatalog==null)throw new InvalidOperationException("Fan profiles require the ONEXPLAYER hardware service.");
                                    if(request.TryGetProperty("OnexFanProfilesSet",out var mutation))
                                    {
                                        if(!request.TryGetProperty("OnexUserInput",out var input)||input.ValueKind!=JsonValueKind.True)throw new InvalidOperationException("Profile changes require explicit input");
                                        using var change=JsonDocument.Parse(mutation.GetString()??"{}");
                                        var fanChange=change.RootElement;
                                        string? operation=fanChange.GetProperty("operation").GetString();
                                        try
                                        {
                                            if(fanChange.TryGetProperty("processId",out _))
                                                fanCatalog.CreateForProcess(profiles.ResolveLinkedProcess(fanChange));
                                            else
                                            {
                                                if(fanChange.TryGetProperty("performanceProfileId",out _))
                                                    throw new InvalidOperationException("Fan profiles are independent. Choose a running app on the Fan page.");
                                                fanCatalog.Mutate(fanChange);
                                            }
                                        }
                                        catch(FanProfileApplyException failure)
                                        {
                                            Log(failure);
                                            reply["OnexFanApplyError"]=failure.Message;
                                        }
                                        catch(Exception failure) when(operation=="select")
                                        {
                                            // Send the committed selection and current hardware state
                                            // even when the new selection was rejected or rolled back.
                                            Log(failure);
                                            reply["OnexFanSelectionError"]=failure.Message;
                                        }
                                    }
                                    reply["OnexFanProfilesState"]=fanCatalog.Read();
                                    if(request.TryGetProperty("OnexFanProfilesGet",out _))reply["OnexFanCandidates"]=profiles.Candidates();
                                    reply["OnexHardwareHomeState"]=JsonSerializer.Serialize(homeHardware,Backend.Program.Json);
                                }
                                else if(request.TryGetProperty("OnexDisplayProfilesGet",out _)||request.TryGetProperty("OnexDisplayProfilesSet",out _))
                                {
                                    if(request.TryGetProperty("OnexDisplayProfilesSet",out var mutation))
                                    {
                                        if(!request.TryGetProperty("OnexUserInput",out var input)||input.ValueKind!=JsonValueKind.True)throw new InvalidOperationException("Profile changes require explicit input");
                                        using var change=JsonDocument.Parse(mutation.GetString()??"{}");screenCatalog.Mutate(change.RootElement);
                                    }
                                    reply["OnexDisplayProfilesState"]=screenCatalog.Read();reply["OnexDisplayCandidates"]=screenCatalog.Candidates();
                                    reply["OnexDisplayState"]=JsonSerializer.Serialize(displayController.ReadForWidget(),Backend.Program.Json);
                                }
                                else if(request.TryGetProperty("OnexRotationLockSet",out var rotationLock))
                                {
                                    if(!request.TryGetProperty("OnexUserInput",out var input)||input.ValueKind!=JsonValueKind.True)
                                        throw new InvalidOperationException("Rotation changes require explicit input");
                                    var rotation = displayController.SetRotationLock(rotationLock.GetBoolean());
                                    reply["OnexRotationLockState"] = JsonSerializer.Serialize(rotation, Backend.Program.Json);
                                }
                                else if(request.TryGetProperty("OnexDisplayGet",out _)||request.TryGetProperty("OnexDisplaySet",out _))
                                {
                                    if(request.TryGetProperty("OnexDisplaySet",out var displaySet))
                                    {
                                        if(!request.TryGetProperty("OnexUserInput",out var input)||input.ValueKind!=JsonValueKind.True)throw new InvalidOperationException("Display changes require explicit input");
                                        using var mutation=JsonDocument.Parse(displaySet.GetString()??"{}");var change=mutation.RootElement;
                                        if(change.TryGetProperty("profileId",out var expected))
                                        {using var current=JsonDocument.Parse(profiles.Read());if(expected.GetString()!=current.RootElement.GetProperty("activeId").GetString())throw new InvalidOperationException("The active profile changed. Try the display adjustment again.");}
                                        if(change.TryGetProperty("screenProfileId",out var screenExpected)&&screenExpected.GetString()!=screenCatalog.ActiveId)throw new InvalidOperationException("The active screen profile changed. Try the adjustment again.");
                                        await powerGate.WaitAsync();
                                        try
                                        {
                                            var before=await power.ReadAsync();if(!before.Ok||before.State==null)throw new IOException(before.Error);
                                            string key=change.GetProperty("key").GetString()??"";var oldImage=DisplayProfiles.ImageSetting(key)?screenCatalog.Capture():null;
                                            string? factoryToken = await CaptureFactoryPowerToken(power, before.State);
                                            try
                                            {
                                                displayController.Set(key,change.GetProperty("value"));
                                                if(oldImage!=null)try{screenCatalog.Record();}catch{displayController.ApplyProfile(oldImage);throw;}
                                            }
                                            finally{await PreserveWindowsPowerLimit(power, before.State, factoryToken, "display-preserved");}
                                        }
                                        finally{powerGate.Release();}
                                    }
                                    reply["OnexDisplayState"]=JsonSerializer.Serialize(displayController.ReadForWidget(),Backend.Program.Json);
                                    reply["OnexDisplayProfilesState"]=screenCatalog.Read();
                                }
                                else if(request.TryGetProperty("OnexProfilesGet",out _)||request.TryGetProperty("OnexProfilesSet",out _))
                                {
                                    if(!profiles.Available)throw new InvalidOperationException(PerformanceProfiles.HardwareUnavailable);
                                    if(request.TryGetProperty("OnexProfilesSet",out var profileSet))
                                    {
                                        if(!request.TryGetProperty("OnexUserInput",out var input)||input.ValueKind!=JsonValueKind.True)
                                            throw new InvalidOperationException("Profile changes require explicit input");
                                        using var mutation=JsonDocument.Parse(profileSet.GetString()??"{}");
                                        var edit=mutation.RootElement;
                                        string? operation=edit.GetProperty("operation").GetString();
                                        if(operation=="screen")
                                            throw new InvalidOperationException("Screen Color Profiles are independent. Select them on the Screen Color page.");
                                        if(operation=="fan"||operation=="source"&&edit.GetProperty("key").GetString()=="fanProfile")
                                            throw new InvalidOperationException("Fan profiles are independent. Select them on the Fan page.");
                                        await profiles.Mutate(edit);
                                    }
                                    reply["OnexProfilesState"]=profiles.Read();
                                    if(request.TryGetProperty("OnexProfilesCandidatesGet",out _))reply["OnexProfilesCandidates"]=profiles.Candidates();
                                }
                                else if(request.TryGetProperty("OnexPl2SelectionInitialize",out _))
                                {
                                    // The widget can request PL2 metadata before the saved profile reaches the hardware.
                                    await InitializePl2Selection(power,request);
                                    reply["OnexState"]=PowerStatePayload();reply["OnexProfilesState"]=profiles.Read();lastPushedRaw=null;
                                }
                                else if(request.TryGetProperty("OnexWifiNetworksGet",out _)||request.TryGetProperty("OnexWifiScan",out _)||request.TryGetProperty("OnexWifiConnect",out _)||request.TryGetProperty("OnexWifiDisconnect",out _))
                                {
                                    bool mutate=request.TryGetProperty("OnexWifiScan",out _)||request.TryGetProperty("OnexWifiConnect",out _)||request.TryGetProperty("OnexWifiDisconnect",out _);
                                    if(mutate&&(!request.TryGetProperty("OnexUserInput",out var input)||input.ValueKind!=JsonValueKind.True))throw new InvalidOperationException("Wi-Fi changes require explicit input");
                                    WifiNetworks networks;
                                    if(request.TryGetProperty("OnexWifiConnect",out var connection))
                                    {using var payload=JsonDocument.Parse(connection.GetString()??"{}");var root=payload.RootElement;networks=wifi.ConnectNetwork(root.GetProperty("token").GetString()??"",root.TryGetProperty("password",out var password)?password.GetString():null,root.TryGetProperty("remember",out var remember)&&remember.ValueKind==JsonValueKind.True);}
                                    else if(request.TryGetProperty("OnexWifiDisconnect",out var disconnect))networks=wifi.DisconnectNetwork(disconnect.GetString()??"");
                                    else networks=wifi.ReadNetworks(request.TryGetProperty("OnexWifiScan",out _));
                                    reply["OnexWifiNetworksState"]=JsonSerializer.Serialize(networks,Backend.Program.Json);
                                }
                                else if(request.TryGetProperty("OnexAwakeGet",out _)||request.TryGetProperty("OnexAwakeSet",out _))
                                {
                                    if(request.TryGetProperty("OnexAwakeSet",out var change))
                                    {
                                        if(!request.TryGetProperty("OnexUserInput",out var input)||input.ValueKind!=JsonValueKind.True)throw new InvalidOperationException("Amphetamine requires explicit input");
                                        var value=JsonSerializer.Deserialize<AwakeCommand>(change.GetString()??"{}",Backend.Program.Json)??throw new InvalidDataException("Invalid Amphetamine settings");
                                        if(value.Simple)awake.SetSimple(value.Enabled);
                                        else awake.Set(value.Enabled,value.Settings??throw new InvalidDataException("Missing Amphetamine settings"));
                                    }
                                    reply["OnexAwakeState"]=JsonSerializer.Serialize(awake.Read(),Backend.Program.Json);
                                }
                                else if(request.TryGetProperty("OnexEnergySaverGet",out _)||request.TryGetProperty("OnexEnergySaverSet",out _))
                                {
                                    EnergySaverState saver;
                                    if(request.TryGetProperty("OnexEnergySaverSet",out var enabled))
                                    {
                                        if(!request.TryGetProperty("OnexUserInput",out var input)||input.ValueKind!=JsonValueKind.True)throw new InvalidOperationException("Energy saver changes require explicit input");
                                        if(enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw new ArgumentException("Energy saver state must be boolean");
                                        saver=await ChangeWindowsPower(power,()=>EnergySaverController.Set(enabled.GetBoolean()));
                                    }
                                    else saver=EnergySaverController.Read();
                                    reply["OnexEnergySaverState"]=JsonSerializer.Serialize(saver,Backend.Program.Json);
                                }
                                else if(request.TryGetProperty("OnexAirplaneGet",out _)||request.TryGetProperty("OnexAirplaneSet",out _))
                                {
                                    if(request.TryGetProperty("OnexAirplaneSet",out var enabled))
                                    {
                                        if(!request.TryGetProperty("OnexUserInput",out var input)||input.ValueKind!=JsonValueKind.True)throw new InvalidOperationException("Airplane changes require explicit input");
                                        if(enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw new ArgumentException("Airplane state must be boolean");
                                        airplane.Set(enabled.GetBoolean());
                                    }
                                    reply["OnexAirplaneState"]=JsonSerializer.Serialize(airplane.Read(),Backend.Program.Json);reply["OnexWifiState"]=JsonSerializer.Serialize(wifi.Read(),Backend.Program.Json);
                                }
                                else if(request.TryGetProperty("OnexWifiGet",out _)||request.TryGetProperty("OnexWifiSet",out _))
                                {
                                    if(request.TryGetProperty("OnexWifiSet",out var wifiOn))
                                    {if(!request.TryGetProperty("OnexUserInput",out var input)||input.ValueKind!=JsonValueKind.True)throw new InvalidOperationException("Wi-Fi changes require explicit input");if(wifiOn.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw new ArgumentException("Wi-Fi state must be boolean");wifi.Set(wifiOn.GetBoolean());}
                                    reply["OnexWifiState"]=JsonSerializer.Serialize(wifi.Read(),Backend.Program.Json);
                                    reply["OnexAirplaneState"]=JsonSerializer.Serialize(airplane.Read(),Backend.Program.Json);
                                }
                                else if(request.TryGetProperty("OnexFseGet",out _)){reply["OnexFseState"]=JsonSerializer.Serialize(GamingExperience.Read(),Backend.Program.Json);}
                                else if(request.TryGetProperty("UpdateTileHotkeys",out var bindingUpdate))
                                {tileHotkeys.Update(bindingUpdate.GetString()??"[]");reply["TileHotkeysSaved"]=true;}
                                else if(request.TryGetProperty("OnexFpsGet",out _)||request.TryGetProperty("OnexFpsSet",out _))
                                {
                                    if(request.TryGetProperty("OnexFpsSet",out var fpsSet))
                                    {if(!request.TryGetProperty("OnexUserInput",out var input)||input.ValueKind!=JsonValueKind.True)throw new InvalidOperationException("FPS changes require explicit input");fpsController.Set(JsonSerializer.Deserialize<FpsPreferences>(fpsSet.GetString()!,Backend.Program.Json)??throw new InvalidDataException("Invalid FPS preferences"));}
                                    reply["OnexFpsState"]=JsonSerializer.Serialize(fpsController.Read(),Backend.Program.Json);
                                }
                                else if(request.TryGetProperty("OnexOverlayPreviewGet",out var previewLevel))
                                {
                                    string draft=request.TryGetProperty("OnexOverlayPreviewConfig",out var previewConfig)?previewConfig.GetString()??"":overlay.Preferences.Config;
                                    reply["OnexOverlayPreview"]=JsonSerializer.Serialize(overlay.Preview(previewLevel.GetInt32(),draft),Backend.Program.Json);
                                }
                                else if(request.TryGetProperty("OnexOverlayPresetsGet",out _)||request.TryGetProperty("OnexOverlayPresetsSet",out _))
                                {
                                    if(overlayCatalog==null)throw new InvalidOperationException(overlayPresetError??"Overlay presets unavailable");
                                    if(request.TryGetProperty("OnexOverlayPresetsSet",out var presetChange)){if(!request.TryGetProperty("OnexUserInput",out var input)||input.ValueKind!=JsonValueKind.True)throw new InvalidOperationException("Preset changes require explicit input");using var change=JsonDocument.Parse(presetChange.GetString()??"{}");overlayCatalog.Mutate(change.RootElement);}
                                    reply["OnexOverlayPresetsState"]=overlayCatalog.Read();reply["OnexOverlayState"]=JsonSerializer.Serialize(overlay.Read(),Backend.Program.Json);reply["OnexOverlayPreferences"]=JsonSerializer.Serialize(overlay.Preferences,Backend.Program.Json);
                                }
                                else if(request.TryGetProperty("OnexOverlayGet",out _)||request.TryGetProperty("OnexOverlayLevel",out _))
                                {
                                    if(request.TryGetProperty("OnexOverlayLevel",out var level))
                                    {if(!request.TryGetProperty("OnexUserInput",out var input)||input.ValueKind!=JsonValueKind.True)throw new InvalidOperationException("Overlay changes require explicit input");var previous=overlay.Preferences;try{overlay.SetLevel(level.GetInt32());overlayCatalog?.Record();}catch{overlay.SetPreferences(previous);throw;}}
                                    reply["OnexOverlayState"]=JsonSerializer.Serialize(overlay.Read(),Backend.Program.Json);
                                    reply["OnexOverlayPreferences"]=JsonSerializer.Serialize(overlay.Preferences,Backend.Program.Json);
                                    reply["OnexOverlayEditorDefaults"]=OverlayFormatting.EditorDefaults();if(overlayCatalog!=null)reply["OnexOverlayPresetsState"]=overlayCatalog.Read();
                                }
                                else if(command==1&&function==Function.OSDConfig)
                                {
                                    if(!request.TryGetProperty("OnexUserInput",out var input)||input.ValueKind!=JsonValueKind.True)throw new InvalidOperationException("Overlay formatting changes require explicit input");
                                    var previous=overlay.Preferences;try{overlay.SetConfig(request.GetProperty("Content").GetString()??"");overlayCatalog?.Record();}catch{overlay.SetPreferences(previous);throw;}if(overlayCatalog!=null)reply["OnexOverlayPresetsState"]=overlayCatalog.Read();reply["Content"]=overlay.Preferences.Config;reply["UpdatedTime"]=DateTime.Now.Ticks;reply["OnexOverlayState"]=JsonSerializer.Serialize(overlay.Read(),Backend.Program.Json);
                                }
                                else if(request.TryGetProperty("OnexCpuTuningGet",out _)||request.TryGetProperty("OnexCpuTuningSet",out _))
                                {
                                    if(request.TryGetProperty("OnexCpuTuningSet",out var tune))
                                    {
                                        if(!request.TryGetProperty("OnexUserInput",out var input)||input.ValueKind!=JsonValueKind.True)throw new InvalidOperationException("CPU tuning requires explicit input");
                                        var policy=JsonSerializer.Deserialize<CpuTuningPolicy>(tune.GetString()??"null",Backend.Program.Json);policy?.Validate();
                                        var currentWindows=WindowsPowerSettings.Read();if(!currentWindows.Available||currentWindows.Cpu==null)throw new IOException(currentWindows.Error);
                                        if(request.TryGetProperty("OnexCpuTuningOnAc",out var side)&&side.GetBoolean()!=currentWindows.OnAc)throw new InvalidOperationException("Power source changed; reopen CPU settings");
                                        if(request.TryGetProperty("OnexCpuTuningScheme",out var scheme)&&scheme.GetString()!=currentWindows.Cpu.Scheme)throw new InvalidOperationException("Power plan changed; reopen CPU settings");
                                        previousCpuTuning=cpuTuning.CapturePolicy();previousCpuWindows=currentWindows.Cpu;var requestedWindows=previousCpuWindows;
                                        if(request.TryGetProperty("OnexCpuTuningBoostMode",out var boostMode))requestedWindows=currentWindows.OnAc?requestedWindows with{BoostAc=boostMode.GetUInt32()}:requestedWindows with{BoostDc=boostMode.GetUInt32()};
                                        if(request.TryGetProperty("OnexCpuTuningScheduling",out var scheduling))requestedWindows=currentWindows.OnAc?requestedWindows with{SchedulingAc=scheduling.GetUInt32(),ShortSchedulingAc=scheduling.GetUInt32()}:requestedWindows with{SchedulingDc=scheduling.GetUInt32(),ShortSchedulingDc=scheduling.GetUInt32()};
                                        await ChangeWindowsPower(power,()=>
                                        {
                                            try{if(requestedWindows!=previousCpuWindows)WindowsPowerSettings.SetCpu(requestedWindows);cpuTuning.Apply(policy,userInput:true);cpuTuningApplied=true;return true;}
                                            catch{if(requestedWindows!=previousCpuWindows)WindowsPowerSettings.SetCpu(previousCpuWindows);cpuTuning.Apply(previousCpuTuning);throw;}
                                        });
                                    }
                                    reply["OnexCpuTuningState"]=JsonSerializer.Serialize(cpuTuning.Read(),Backend.Program.Json);
                                    reply["OnexWindowsPowerState"]=JsonSerializer.Serialize(WindowsPowerSettings.Read(),Backend.Program.Json);
                                }
                                else if(request.TryGetProperty("OnexWindowsPowerGet",out _)||request.TryGetProperty("OnexWindowsPowerMode",out _)||request.TryGetProperty("OnexCpuSettings",out _))
                                {
                                    WindowsPowerState settings;
                                    if(request.TryGetProperty("OnexWindowsPowerMode",out var mode))
                                    {if(!request.TryGetProperty("OnexUserInput",out var input)||input.ValueKind!=JsonValueKind.True)throw new InvalidOperationException("Power mode requires explicit input");settings=await ChangeWindowsPower(power,()=>WindowsPowerSettings.SetMode(mode.GetInt32()));}
                                    else if(request.TryGetProperty("OnexCpuSettings",out var cpuSettings))
                                    {if(!request.TryGetProperty("OnexUserInput",out var input)||input.ValueKind!=JsonValueKind.True)throw new InvalidOperationException("CPU settings require explicit input");settings=await ChangeWindowsPower(power,()=>WindowsPowerSettings.SetCpu(JsonSerializer.Deserialize<CpuPowerSettings>(cpuSettings.GetString()!,Backend.Program.Json)??throw new InvalidDataException("Invalid CPU settings")));}
                                    else settings=WindowsPowerSettings.Read();
                                    reply["OnexWindowsPowerState"]=JsonSerializer.Serialize(settings,Backend.Program.Json);
                                }
                                else if(request.TryGetProperty("OnexHardwareHomeGet",out _)||request.TryGetProperty("OnexFanSet",out _)||request.TryGetProperty("OnexLightingSet",out _)||request.TryGetProperty("OnexSmallMaskSet",out _)||request.TryGetProperty("OnexCpuClockSet",out _))
                                {
                                    await powerGate.WaitAsync();
                                    try
                                    {
                                        PowerReply result;
                                        bool writing=!request.TryGetProperty("OnexHardwareHomeGet",out _);
                                        if(writing&&(!request.TryGetProperty("OnexUserInput",out var input)||input.ValueKind!=JsonValueKind.True))throw new InvalidOperationException("Hardware settings require explicit input");
                                        if(request.TryGetProperty("OnexCpuClockSet",out var clock))
                                        {
                                            var baseline=await power.ReadAsync();if(!baseline.Ok||baseline.State is null)throw new IOException("Cannot preserve TDP before changing CPU frequency");
                                            string? factoryToken = await CaptureFactoryPowerToken(power, baseline.State);
                                            try{result=await power.SetCpuClockAsync(clock.GetInt32());}finally{await PreserveWindowsPowerLimit(power,baseline.State,factoryToken);}
                                        }
                                        else if(request.TryGetProperty("OnexFanSet",out var profile))
                                        {
                                            int mode=FanPreferences.SupportedMode(profile.GetInt32());
                                            if(fanCatalog==null)throw new InvalidOperationException("Fan control requires the ONEXPLAYER hardware service.");
                                            if(fanCatalog.ActiveId==FanPreferences.AutoId&&mode!=0)throw new InvalidOperationException("Auto is a read-only Fan preset");
                                            result=await power.SetFanAsync(mode);
                                        }
                                        else if(request.TryGetProperty("OnexLightingSet",out var lighting))result=await power.SetLightingAsync(lighting.GetString()??"");
                                        else if(request.TryGetProperty("OnexSmallMaskSet",out var mask))result=await power.SetSmallMaskAsync(mask.GetBoolean());
                                        else result=await power.ReadHomeAsync();
                                        if(!result.Ok)throw new InvalidOperationException(result.Error);homeHardware=result.Home;reply["OnexHardwareHomeState"]=JsonSerializer.Serialize(homeHardware,Backend.Program.Json);
                                    }
                                    finally{powerGate.Release();}
                                    if(fanCatalog!=null)
                                    {
                                        if(request.TryGetProperty("OnexFanSet",out _)&&fanCatalog.ActiveId!=FanPreferences.AutoId)fanCatalog.Record();
                                        reply["OnexFanProfilesState"]=fanCatalog.Read();
                                    }
                                }
                                else if(request.TryGetProperty("OnexBoostGet",out _)||request.TryGetProperty("OnexBoostSet",out _))
                                {
                                    await powerGate.WaitAsync();
                                    try
                                    {
                                        PowerReply result;
                                        if(request.TryGetProperty("OnexBoostSet",out var enabled))
                                        {
                                            if(!request.TryGetProperty("OnexUserInput",out var input)||input.ValueKind!=JsonValueKind.True)throw new InvalidOperationException("Boost writes require explicit input");
                                            result=await power.SetBoostAsync(enabled.GetBoolean());
                                        }
                                        else result=await power.ReadBoostAsync();
                                        if(!result.Ok)throw new InvalidOperationException(result.Error);
                                        boost=result.Boost;
                                        reply["OnexBoostState"]=JsonSerializer.Serialize(boost,Backend.Program.Json);
                                    }
                                    finally{powerGate.Release();}
                                }
                                else if (request.TryGetProperty("OnexChargeGet", out _) || request.TryGetProperty("OnexChargeSet", out _))
                                {
                                    await powerGate.WaitAsync();
                                    try
                                    {
                                        PowerReply result;
                                        if (request.TryGetProperty("OnexChargeSet", out var percent))
                                        {
                                            if (!request.TryGetProperty("OnexUserInput", out var input) || input.ValueKind!=JsonValueKind.True) throw new InvalidOperationException("Charge writes require explicit input");
                                            result=await power.SetChargeAsync(percent.GetInt32());
                                        }
                                        else result=await power.ReadChargeAsync();
                                        if (!result.Ok) throw new InvalidOperationException(result.Error);
                                        charge=result.Charge;
                                        reply["OnexChargeState"]=JsonSerializer.Serialize(charge, Backend.Program.Json);
                                    }
                                    finally {powerGate.Release();}
                                }
                                else if(request.TryGetProperty("OnexBluetoothPairHost",out var bluetoothPairToken))
                                {
                                    if(!request.TryGetProperty("OnexUserInput",out var input)||input.ValueKind!=JsonValueKind.True)throw new InvalidOperationException("Bluetooth pairing requires explicit input");
                                    var pairing=BluetoothPairingLauncher.Reserve(bluetoothPairToken.GetString()??"");
                                    reply["Accepted"]=true;if(id>0)await Send(reply);
                                    _=Task.Run(()=>BluetoothPairingLauncher.RunAsync(pairing,gameBarEntry));continue;
                                }
                                else if(request.TryGetProperty("OnexBluetoothUi",out var bluetoothUi))
                                {
                                    if(!request.TryGetProperty("OnexUserInput",out var input)||input.ValueKind!=JsonValueKind.True)throw new InvalidOperationException("Bluetooth UI transitions require explicit input");
                                    string transition=bluetoothUi.GetString()??"";
                                    if(transition=="release")
                                    {
                                        reply["Accepted"]=true;if(id>0)await Send(reply);
                                        _=Task.Run(async()=>{try{await DesktopActions.DismissOverlay();}catch(Exception e){Log(e);}});continue;
                                    }
                                    if(transition!="restore")throw new ArgumentException("Unknown Bluetooth UI transition");
                                    gameBarEntry.EnqueueKeyboardOpen();reply["Accepted"]=true;
                                }
                                else if(request.TryGetProperty("OnexBluetoothDisconnect",out var bluetoothAddress))
                                {
                                    if(!request.TryGetProperty("OnexUserInput",out var input)||input.ValueKind!=JsonValueKind.True)throw new InvalidOperationException("Bluetooth disconnect requires explicit input");
                                    BluetoothConnections.Disconnect(bluetoothAddress.GetString()??"");reply["Accepted"]=true;
                                }
                                else if(request.TryGetProperty("OnexWidgetReady",out var ready))
                                {
                                    widgetSession=true;gameBarEntry.SetWidgetReady(ready.ValueKind==JsonValueKind.True);reply["Accepted"]=true;
                                }
                                else if(request.TryGetProperty("OnexWidgetVisible",out var widgetVisibility))
                                {
                                    gameBarEntry.SetWidgetVisible(widgetVisibility.ValueKind == JsonValueKind.True);
                                    reply["Accepted"] = true;
                                }
                                else if(request.TryGetProperty("OnexSystemPowerAction",out var powerAction))
                                {
                                    if(!request.TryGetProperty("OnexUserInput",out var input)||input.ValueKind!=JsonValueKind.True)throw new InvalidOperationException("Power actions require explicit input");
                                    string action=powerAction.GetString()??"";SystemPowerActions.Plan(action);
                                    bool inBar=request.TryGetProperty("InGameBar",out var inGame)&&inGame.ValueKind==JsonValueKind.True;
                                    reply["SystemPowerQueued"]=true;if(id>0)await Send(reply);
                                    _=Task.Run(async()=>{try{await SystemPowerActions.Execute(action,inBar);}catch(Exception e){Log(e);}});continue;
                                }
                                else if (request.TryGetProperty("OnexDesktopAction", out var desktopAction))
                                {
                                    if (!request.TryGetProperty("OnexUserInput", out var desktopInput) || desktopInput.ValueKind != JsonValueKind.True)
                                        throw new InvalidOperationException("Desktop actions require an explicit input action");
                                    string action = desktopAction.GetString() ?? "";
                                    string? targetToken = request.TryGetProperty("TargetToken", out var token) ? token.GetString() : null;
                                    bool inGameBar = request.TryGetProperty("InGameBar", out var inBar) && inBar.ValueKind == JsonValueKind.True;
                                    string? shortcut=request.TryGetProperty("Shortcut",out var shortcutElement)?shortcutElement.GetString():null;
                                    string? launchTarget=request.TryGetProperty("LaunchTarget",out var launchElement)?launchElement.GetString():null;
                                    if(action=="shortcut")DesktopActions.ParseShortcut(shortcut??"");
                                    if(action=="launch")DesktopActions.ValidateLaunch(launchTarget);
                                    if(action=="chrome")DesktopActions.ResolveChrome();
                                    desktop.Validate(action, targetToken);
                                    reply["DesktopQueued"] = true;
                                    if (id > 0) await Send(reply); // Acknowledge before Win+G suspends the UWP client.
                                    _ = Task.Run(async () =>
                                    {
                                        var result = await desktop.Execute(action,targetToken,inGameBar,shortcut,launchTarget);
                                        await File.WriteAllTextAsync(Path.Combine(ReportDirectory, "desktop-action.json"), JsonSerializer.Serialize(new { utc=DateTimeOffset.UtcNow, result }, Backend.Program.Json));
                                    });
                                    continue;
                                }
                                else if(request.TryGetProperty("OnexAudioOutputGet",out _)||request.TryGetProperty("OnexAudioOutputSet",out _))
                                {
                                    AudioDevicesState devices;
                                    if(request.TryGetProperty("OnexAudioOutputSet",out var selected))
                                    {
                                        if(!request.TryGetProperty("OnexMediaUserInput",out var input)||input.ValueKind!=JsonValueKind.True)throw new InvalidOperationException("Audio output changes require explicit input");
                                        if(selected.ValueKind!=JsonValueKind.String||!Guid.TryParseExact(selected.GetString(),"N",out _))throw new ArgumentException("Invalid audio output selection");
                                        devices=audioDevices.SetDefault(selected.GetString()!);
                                    }
                                    else devices=audioDevices.Read();
                                    reply["OnexAudioOutputState"]=JsonSerializer.Serialize(devices,Backend.Program.Json);
                                    reply["MediaLevels"]=JsonSerializer.Serialize(await media.ReadAsync(),Backend.Program.Json);
                                }
                                else if(request.TryGetProperty("OnexAudioInputGet",out _)||request.TryGetProperty("OnexAudioInputSet",out _))
                                {
                                    AudioDevicesState devices;
                                    if(request.TryGetProperty("OnexAudioInputSet",out var selected))
                                    {
                                        if(!request.TryGetProperty("OnexMediaUserInput",out var input)||input.ValueKind!=JsonValueKind.True)throw new InvalidOperationException("Audio input changes require explicit input");
                                        if(selected.ValueKind!=JsonValueKind.String||!Guid.TryParseExact(selected.GetString(),"N",out _))throw new ArgumentException("Invalid audio input selection");
                                        devices=audioInputs.SetDefault(selected.GetString()!);
                                    }
                                    else devices=audioInputs.Read();
                                    reply["OnexAudioInputState"]=JsonSerializer.Serialize(devices,Backend.Program.Json);
                                    reply["MediaLevels"]=JsonSerializer.Serialize(await media.ReadAsync(),Backend.Program.Json);
                                }
                                else if(request.TryGetProperty("OnexAudioMixerGet",out _)||request.TryGetProperty("OnexAudioMixerSet",out _))
                                {
                                    if(request.TryGetProperty("OnexAudioMixerSet",out var edit))
                                    {
                                        if(!request.TryGetProperty("OnexMediaUserInput",out var mixerInput)||mixerInput.ValueKind!=JsonValueKind.True)throw new InvalidOperationException("Mixer writes require explicit input");
                                        using var document=JsonDocument.Parse(edit.GetString()!);var change=document.RootElement;string token=change.GetProperty("token").GetString()??"";
                                        double? volume=change.TryGetProperty("volume",out var level)?level.GetDouble():null;bool toggle=change.TryGetProperty("toggleMute",out var flag)&&flag.ValueKind==JsonValueKind.True;
                                        if(volume.HasValue==toggle||volume.HasValue&&(!double.IsFinite(volume.Value)||volume.Value is <0 or >100))throw new ArgumentException("Invalid mixer edit");
                                        if(token=="master"){if(volume.HasValue)await media.SetVolumeAsync(volume.Value);else await media.ToggleMuteAsync();}
                                        else audioMixer.Set(token,volume,toggle);
                                    }
                                    reply["OnexAudioMixerState"]=await ReadAudioMixer();
                                }
                                else if (request.TryGetProperty("GetMediaLevels", out _))
                                {
                                    reply["MediaLevels"] = JsonSerializer.Serialize(await media.ReadAsync(), Backend.Program.Json);
                                }
                                else if (request.TryGetProperty("SetBrightnessLevel", out var brightness) || request.TryGetProperty("SetVolumeLevel", out _) || request.TryGetProperty("ToggleMute", out _) || request.TryGetProperty("ToggleMicrophoneMute", out _) || request.TryGetProperty("ToggleBrightnessDim", out _))
                                {
                                    if (!request.TryGetProperty("OnexMediaUserInput", out var input) || input.ValueKind != JsonValueKind.True)
                                        throw new InvalidOperationException("Media writes require an explicit input action");
                                    MediaLevels actualMedia;
                                    if (brightness.ValueKind != JsonValueKind.Undefined) actualMedia = await media.SetBrightnessAsync(Integer(brightness));
                                    else if(request.TryGetProperty("ToggleBrightnessDim",out var dimBrightness)&&dimBrightness.ValueKind==JsonValueKind.True)actualMedia=await media.ToggleBrightnessDimAsync();
                                    else if (request.TryGetProperty("SetVolumeLevel", out var volume)) actualMedia = await media.SetVolumeAsync(volume.GetDouble());
                                    else if (request.TryGetProperty("ToggleMute",out var mute) && mute.ValueKind == JsonValueKind.True) actualMedia = await media.ToggleMuteAsync();
                                    else if(request.TryGetProperty("ToggleMicrophoneMute",out var microphoneMute) && microphoneMute.ValueKind==JsonValueKind.True)actualMedia=await media.ToggleMicrophoneMuteAsync();
                                    else throw new InvalidDataException("Mute toggle must be true");
                                    reply["MediaLevels"] = JsonSerializer.Serialize(actualMedia, Backend.Program.Json);
                                    if (actualMedia.Muted is { } muted) reply["MuteState"] = muted;
                                }
                                else if (request.TryGetProperty("OnexReadState", out var readState) && readState.GetBoolean())
                                {
                                    await RefreshPower(power);
                                    reply["OnexState"] = PowerStatePayload();
                                }
                                else if (request.TryGetProperty("OnexRestore", out var restore) && restore.GetBoolean())
                                {
                                    await powerGate.WaitAsync();
                                    try
                                    {
                                        var result = await power.RestoreAsync();
                                        if (!result.Ok) throw new InvalidOperationException(result.Error);
                                        state = result.State;
                                        reply["OnexState"] = PowerStatePayload();
                                        lastPushedRaw = null;
                                    }
                                    finally { powerGate.Release(); }
                                }
                                else if (command == 2 && request.TryGetProperty("Functions", out var functions))
                                {
                                    int[] ids = JsonSerializer.Deserialize<int[]>(functions.GetString() ?? "[]") ?? [];
                                    if (ids.Length > 1024) throw new InvalidDataException("Too many requested properties");
                                    var batch = new Dictionary<string, object>();
                                    // Capabilities must arrive before watts so UWP never clamps to a stale MSI limit.
                                    foreach (var f in new[] { Function.DeviceMaxPL1, Function.DeviceMaxPL2, Function.TDPLimits })
                                        await Send(Push(f, Value(f)!));
                                    foreach (int fid in ids)
                                        if (Value((Function)fid) is { } value) batch[fid.ToString(CultureInfo.InvariantCulture)] = new { Content = value, UpdatedTime = DateTime.Now.Ticks };
                                    reply["BatchData"] = JsonSerializer.Serialize(batch);
                                }
                                else if (command == 0 && Value(function) is { } value)
                                {
                                    reply["Content"] = value; reply["UpdatedTime"] = DateTime.Now.Ticks;
                                }
                                else if (command == 1 && IsPower(function))
                                {
                                    // Every hardware action must carry the marker added at a genuine input
                                    // handler. Startup sync, profile loading and capability updates cannot write.
                                    if (!request.TryGetProperty("OnexUserInput", out var userInput) || !userInput.GetBoolean())
                                        throw new InvalidOperationException("Power writes require an explicit input action");
                                    powerWriteOnAc=await ApplyPower(power, function, request,powerOutcome);
                                    reply["Content"] = Value(function); reply["UpdatedTime"] = DateTime.Now.Ticks;
                                }
                                else if (command == 1 && function is Function.QuickMetricsEnabled or Function.Foreground or Function.AppModeWindowState or Function.GameBarWidgetFavorited)
                                    reply["Content"] = request.TryGetProperty("Content", out var content) ? content.Clone() : null;
                                else reply["Error"] = "This function is not connected to the ONEXPLAYER backend yet.";
                                string? savedGroup=null;
                                if(!reply.ContainsKey("Error"))
                                {
                                    if(command==1&&IsPower(function))savedGroup="TDP";
                                    else if(request.TryGetProperty("OnexCpuSettings",out _)||request.TryGetProperty("OnexCpuClockSet",out _)||request.TryGetProperty("OnexBoostSet",out _)||request.TryGetProperty("OnexCpuTuningSet",out _))savedGroup="CPUBoost";
                                    else if(request.TryGetProperty("OnexWindowsPowerMode",out _))savedGroup="OSPowerMode";
                                    else if(request.TryGetProperty("OnexFpsSet",out _))savedGroup="FPSLimit";
                                    else if(request.TryGetProperty("OnexOverlayLevel",out _))savedGroup="OverlayLevel";
                                    else if(request.TryGetProperty("OnexOverlayPresetsSet",out var presetRequest)){using var change=JsonDocument.Parse(presetRequest.GetString()??"{}");if(change.RootElement.GetProperty("operation").GetString()=="select")savedGroup="OverlayLevel";}
                                    else if(request.TryGetProperty("OnexDisplaySet",out var displaySet))
                                    {using var change=JsonDocument.Parse(displaySet.GetString()??"{}");string key=change.RootElement.GetProperty("key").GetString()??"";savedGroup=DisplayProfiles.ImageSetting(key)?null:key switch{"resolution"=>"Resolution","refreshRate"=>"RefreshRate","displayOrientation" or "displayScale"=>null,_=>"Display"};}
                                    if((savedGroup is "CPUBoost" or "OSPowerMode")&&cpuTuning.HasLease&&!request.TryGetProperty("OnexCpuTuningSet",out _))
                                        await ChangeWindowsPower(power,()=>{cpuTuning.ReapplyIntent();return true;});
                                    if(savedGroup!=null&&profiles.Available){await profiles.Record(savedGroup,savedGroup=="CPUBoost"&&!request.TryGetProperty("OnexCpuTuningSet",out _)?WindowsPowerSettings.Read().Cpu:null,savedGroup=="ScreenProfile"?screenCatalog.ActiveId:null,request.TryGetProperty("OnexPreferActiveProfile",out var preferActive)&&preferActive.ValueKind==JsonValueKind.True,powerWriteOnAc);if(command==1&&IsPower(function)&&savedGroup=="TDP")powerOutcome.ProfilePersisted();}
                                    cpuTuningApplied=false;
                                    if(savedGroup!=null||request.TryGetProperty("OnexProfilesSet",out _)||request.TryGetProperty("OnexFanProfilesSet",out _)||request.TryGetProperty("OnexDisplayProfilesSet",out _))
                                    {
                                        // Return one committed view after profile persistence, so
                                        // both pages can update without waiting for separate pushes.
                                        if(profiles.Available)reply["OnexProfilesState"]=profiles.Read();
                                        reply["OnexDisplayProfilesState"]=screenCatalog.Read();
                                        if(fanCatalog!=null)reply["OnexFanProfilesState"]=fanCatalog.Read();
                                        reply["OnexHardwareHomeState"]=JsonSerializer.Serialize(homeHardware,Backend.Program.Json);
                                        reply["OnexDisplayState"]=JsonSerializer.Serialize(displayController.ReadForWidget(),Backend.Program.Json);
                                        reply["OnexState"]=PowerStatePayload();
                                        reply["OnexWindowsPowerState"]=JsonSerializer.Serialize(WindowsPowerSettings.Read(),Backend.Program.Json);
                                        reply["OnexFpsState"]=JsonSerializer.Serialize(fpsController.Read(),Backend.Program.Json);
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                if(cpuTuningApplied)try{await ChangeWindowsPower(power,()=>{if(previousCpuWindows!=null)WindowsPowerSettings.SetCpu(previousCpuWindows);cpuTuning.Apply(previousCpuTuning);return true;});}catch(Exception recovery){Log(recovery);}
                                Exception failure=e;
                                if(e is PowerSourceChangedException)
                                    try{await profiles.RefreshPowerSource();}
                                    catch(Exception synchronization){failure=new IOException(e.Message+" Power source synchronization failed: "+synchronization.Message,new AggregateException(e,synchronization));}
                                reply["Error"] = powerOutcome.DescribeFailure(failure);
                                Log(failure);
                                if (IsPower(function)) { await RefreshPower(power); lastPushedRaw = null; }
                                else reply["MediaLevels"] = JsonSerializer.Serialize(await media.ReadAsync(), Backend.Program.Json);
                            }
                            finally{profiles.Gate.Release();}
                            if (id > 0) await Send(reply);
                        }
                    }
                    finally { tileHotkeySender=null;if(widgetSession)gameBarEntry.SetWidgetReady(false);session.Cancel(); await ticker; }
                }
                catch (Exception e) when (e is IOException or OperationCanceledException or UnauthorizedAccessException) { Log(e); }
            }
        }
        catch (Exception e) { Log(e); return 1; }
        finally{controllerRuntimeStop.Cancel();await controllerRuntime;}
        // Disposing PowerChannel closes its authenticated pipe. The privileged agent restores its
        // original registers on disconnect; the UWP frontend never holds an elevated handle.
    }

    private static async Task PreserveWindowsPowerLimit(PowerChannel power, PowerState baseline,
        string? factoryToken = null, string? displayAudit = null)
    {
        var actual=await power.ReadAsync();if(!actual.Ok||actual.State is null)throw new IOException("Could not verify TDP after changing Windows power settings");
        state = actual.State;
        bool matches = displayAudit == null || factoryToken != null
            ? state.RawLimits == baseline.RawLimits
            : (state.Pl1Watts, state.Pl2Watts) == (baseline.Pl1Watts, baseline.Pl2Watts);
        if(matches && factoryToken == null)return;
        if(baseline.Pl1Watts!=(int)baseline.Pl1Watts||baseline.Pl2Watts!=(int)baseline.Pl2Watts)throw new IOException("Cannot preserve fractional TDP");
        var restored = factoryToken != null ? await power.RestoreFactoryPowerAsync(factoryToken)
            : await power.SetAsync((int)baseline.Pl1Watts, (int)baseline.Pl2Watts);
        if (!restored.Ok || restored.State is not {} readback ||
            ((displayAudit == null || factoryToken != null) && readback.RawLimits != baseline.RawLimits))
            throw new IOException(restored.Error ?? "TDP preservation failed after changing Windows power settings");
        state = readback;
        if (!matches)
        {
            if (displayAudit == null) Interlocked.Increment(ref writes);
            else AuditPower(displayAudit, state);
        }
    }
    private static async Task<T> ChangeWindowsPower<T>(PowerChannel power,Func<T> apply)
    {
        await powerGate.WaitAsync();
        try
        {
            var baseline=await power.ReadAsync();if(!baseline.Ok||baseline.State is null)throw new IOException("Cannot preserve TDP before changing Windows power settings");
            string? factoryToken = await CaptureFactoryPowerToken(power, baseline.State);
            try{return apply();}finally{await PreserveWindowsPowerLimit(power,baseline.State,factoryToken);}
        }
        finally{powerGate.Release();}
    }
    private static bool IsPower(Function f) => f is Function.TDP or Function.TDPBoostFPPT or Function.TDPBoostEnabled;
    private static async Task<bool> RestorePreferredPower(PowerChannel power,string reason="bridge-startup")
    {
        if (!power.HardwareConfigured) return false;
        try
        {
            var preferred=powerPreferences.Load();
            var live=await power.ReadAsync();
            if(!live.Ok||live.State is not {CanWrite:true} current)throw new IOException(live.Error??"Cannot read power settings on startup");
            state=current;
            if(preferred is null)return true;
            // Do not replace an externally established OEM power state unless
            // the current limits can also be restored through the validated
            // write API. In particular preserve resume's PL2=52 readback.
            if(!PowerSnapshotWritable(Convert.ToDouble(current.Pl1Watts),Convert.ToDouble(current.Pl2Watts)))
            {
                await File.WriteAllTextAsync(Path.Combine(ReportDirectory,"power-startup-deferred.json"),JsonSerializer.Serialize(new{restored=false,actual=current,reason="external-power-outside-write-contract",automaticWritesDeferred=true},Backend.Program.Json));
                return true;
            }
            bool restored=current.Pl1Watts!=preferred.Pl1Watts||current.Pl2Watts!=preferred.Pl2Watts;
            if(restored)
            {
                var reply=await power.SetAsync(preferred.Pl1Watts,preferred.Pl2Watts);
                if(!reply.Ok||reply.State is null||reply.State.Pl1Watts!=preferred.Pl1Watts||reply.State.Pl2Watts!=preferred.Pl2Watts)throw new IOException(reply.Error??"Saved power limits did not restore");
                state=reply.State;Interlocked.Increment(ref writes);
                AuditPower(reason,state);
            }
            await File.WriteAllTextAsync(Path.Combine(ReportDirectory,reason=="bridge-startup"?"power-startup-restore.json":"oem-handoff-power-repair.json"),JsonSerializer.Serialize(new{preferred,restored,actual=state,verified=true,reason},Backend.Program.Json));
            return true;
        }
        catch(Exception e){Log(e);return false;}
    }
    private static void AuditPower(string reason,PowerState actual)
    {
        try{File.AppendAllText(Path.Combine(ReportDirectory,"power-writes.jsonl"),JsonSerializer.Serialize(new{utc=DateTimeOffset.UtcNow,pid=Environment.ProcessId,reason,actual.Pl1Watts,actual.Pl2Watts,actual.RawLimits},Backend.Program.Json)+Environment.NewLine);}
        catch(IOException){}catch(UnauthorizedAccessException){}
    }
    private static async Task RefreshPower(PowerChannel power)
    {
        if (!power.HardwareConfigured) return;
        await powerGate.WaitAsync();
        try { var reply = await power.ReadAsync(); if (reply.Ok) state = reply.State; }
        catch (Exception e) { state = null; Log(e); }
        finally { powerGate.Release(); }
    }
    private static async Task<bool> ApplyPower(PowerChannel power, Function f, JsonElement request,PowerCommitOutcome877? outcome=null)
    {
        await powerGate.WaitAsync();
        try
        {
            bool onAc=WindowsPowerSettings.IsOnAc();
            PowerSourceChangedException.Require(PowerSourceChangedException.ExpectedFrom(request),onAc);
            var before = await power.ReadAsync();
            if (!before.Ok || before.State is not { CanWrite: true } current) throw new InvalidOperationException(before.Error ?? "PowerAgent unavailable");
            int pl1 = checked((int)current.Pl1Watts), pl2 = checked((int)current.Pl2Watts);
            if (pl1 != current.Pl1Watts || pl2 != current.Pl2Watts) throw new InvalidOperationException("Fractional baseline cannot be rounded for a write");
            var selection = PowerSelectionFor(current);
            bool factoryReadback = Pl2ControlPolicy.IsFactoryReadback(pl1, pl2);
            bool separate = selection?.SeparatePl2 ?? (!factoryReadback && pl2 > pl1);
            int manual = selection?.ManualPl2Watts ?? (factoryReadback ? Pl2ControlPolicy.Automatic(pl1) : pl2);
            var content = request.GetProperty("Content");
            if (f == Function.TDP)
            {
                pl1=Integer(content);if(request.TryGetProperty("OnexSeparatePl2",out var mode))separate=Boolean(mode);
                if(request.TryGetProperty("OnexRememberedPl2Watts",out var remembered))manual=Integer(remembered);
                if(request.TryGetProperty("OnexPl2Watts",out var explicitPl2))pl2=Integer(explicitPl2);
                else if(separate)pl2=Pl2ControlPolicy.AdjustManual(pl1,pl2);
                if(!separate)pl2=Pl2ControlPolicy.Automatic(pl1);
            }
            else if(f==Function.TDPBoostFPPT){separate=true;pl2=Integer(content);}
            else{if(separate)manual=pl2;separate=Boolean(content);pl2=separate?Pl2ControlPolicy.AdjustManual(pl1,manual):Pl2ControlPolicy.Automatic(pl1);}
            if(!Pl2ControlPolicy.IsWritable(pl1,pl2))throw new InvalidDataException("PL2 exceeds the limit for this PL1.");
            if(manual is <Pl2ControlPolicy.MinimumPl1 or >Pl2ControlPolicy.ExtendedManualCeiling)throw new InvalidDataException("Invalid remembered PL2");
            if(separate)manual=pl2;
            PowerSourceChangedException.Require(onAc,WindowsPowerSettings.IsOnAc());
            var applied = await power.SetAsync(pl1, pl2);
            if (!applied.Ok) throw new InvalidOperationException(applied.Error);
            state = applied.State ?? throw new IOException("Missing power readback after write");
            Interlocked.Increment(ref writes);
            outcome?.HardwareAcknowledged();
            PowerSourceChangedException.Require(onAc,WindowsPowerSettings.IsOnAc());
            powerPreferences.Save(checked((int)state.Pl1Watts),checked((int)state.Pl2Watts),separate,manual);
            outcome?.PreferencesPersisted();
            AuditPower(request.TryGetProperty("OnexPowerSource",out var source)?source.GetString()??"explicit-input":"explicit-"+f,state);
            return onAc;
        }
        finally { powerGate.Release(); }
    }

    private static object? Value(Function f)
    {
        var s = state;
        return f switch
        {
            Function.OSD => overlay?.Preferences.Level??0,
            Function.RTSSInstalled => RtssOverlay.DirectoryPath!=null,
            Function.DeviceType => 0,
            Function.DeviceManufacturer => "ONE-NETBOOK",
            Function.DeviceModel => "ONEXPLAYER 3",
            Function.DeviceDisplayName => "ONEXPLAYER 3",
            Function.DeviceMaxPL1 => Pl2ControlPolicy.MaximumPl1,
            Function.DeviceMaxPL2 => Pl2ControlPolicy.ManualCeiling((int)(s?.Pl1Watts??0)), Function.TDPLimits => "8,40",
            Function.TDP when s is not null => (int)s.Pl1Watts,
            Function.CurrentTDP when s is not null => $"PL1 {s.Pl1Watts:0.#} W · PL2 {s.Pl2Watts:0.#} W",
            Function.TDPBoostFPPT when s is not null => PowerSelectionFor(s)?.ManualPl2Watts??(int)s.Pl2Watts,
            Function.TDPBoostEnabled when s is not null => PowerSelectionFor(s)?.SeparatePl2
                ?? (!Pl2ControlPolicy.IsFactoryReadback(s.Pl1Watts, s.Pl2Watts) && s.Pl2Watts > s.Pl1Watts),
            Function.LegionGoDetected => false,
            Function.ControllerBatteryLeft or Function.ControllerBatteryRight => -1,
            Function.DeviceHasDetachableControllers => true,
            Function.DeviceSupportsWmiTdp or Function.DeviceSupportsControllerRemap or Function.DeviceSupportsRgbLighting or
            Function.DeviceSupportsGyro or Function.DeviceSupportsFirmwareKeyboardRemap or Function.DeviceHasScrollWheel or
            Function.DeviceHasTouchpad or Function.DeviceSupportsFanControl or Function.DeviceSupportsDriverManagement or
            Function.DeviceSupportsCpuAdvanced => false,
            _ => null
        };
    }
    private static object Push(Function f, object value) => new { RequestId = 0, Command = 1, Function = (int)f, Content = value, UpdatedTime = DateTime.Now.Ticks };
    private static string Metrics(WindowsMetrics w)
    {
        var data = new Dictionary<string, object>();
        if (w.CpuLoadPercent is { } cpu) data["cpuUsage"] = cpu;
        if (state?.CpuTemperatureC is { } temperature) data["cpuTemp"] = temperature;
        if (w.MemoryUsedBytes is { } used && w.MemoryTotalBytes is > 0) data["memoryUsage"] = (double)used / w.MemoryTotalBytes.Value * 100;
        if (w.BatteryPercent is { } battery) data["batteryLevel"] = battery;
        if (w.OnAcPower is { } ac) data["isCharging"] = ac;
        if (w.BatteryRemainingSeconds is { } remaining && remaining != uint.MaxValue) data["timeRemaining"] = remaining / 60.0;
        if(state?.ApuPowerWatts is {} apu)data["apuPowerWatts"]=apu;
        data["apuPowerWattsSource"]="RAPL package energy delta (MSR 0x611), CPU/GPU package watts";
        if(liveTelemetry is {} live)
        {
            if(live.GpuLoadPercent is {} gpu)data["gpuUsage"]=gpu;
            if(live.GpuTemperatureC is {} gpuTemp)data["gpuTemp"]=gpuTemp;
            if(live.SystemPowerWatts is {} total)data["systemPowerWatts"]=total;
            data["gpuUsageSource"]=live.GpuLoadSource;data["gpuTempSource"]=live.GpuTemperatureSource;data["systemPowerWattsSource"]=live.SystemPowerSource;
        }
        return JsonSerializer.Serialize(data);
    }
    private static int Number(JsonElement value, string property, int fallback = 0) => value.TryGetProperty(property, out var n) && n.TryGetInt32(out int i) ? i : fallback;
    private static int Integer(JsonElement value) => value.ValueKind == JsonValueKind.String ? int.Parse(value.GetString()!, CultureInfo.InvariantCulture) : value.GetInt32();
    private static bool Boolean(JsonElement value) => value.ValueKind == JsonValueKind.String ? bool.Parse(value.GetString()!) : value.GetBoolean();
    private static async Task<string?> ReadBounded(StreamReader reader, CancellationToken cancellation)
    {
        var line = new StringBuilder();
        var buffer = new char[1];
        while (await reader.ReadAsync(buffer.AsMemory(), cancellation) != 0)
        {
            if (buffer[0] == '\n') return line.ToString().TrimEnd('\r');
            if (line.Length >= 262144) throw new InvalidDataException("Widget message exceeds limit");
            line.Append(buffer[0]);
        }
        return null;
    }
    private static void Log(Exception error)
    {
        try { File.AppendAllText(Path.Combine(ReportDirectory, "widget-bridge-errors.log"), $"{DateTimeOffset.UtcNow:o} {error.GetType().Name}: {error.Message}\n"); } catch { }
    }
}
