// SPDX-License-Identifier: AGPL-3.0-only
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
namespace ONEXTweaks.WidgetBridge;
internal sealed partial class DesktopActions
{
    private static readonly HashSet<string> HomeActions=new(StringComparer.Ordinal){"fullscreen","optiScaler","reShade","shortcut","desktop","altTab","altTabBack","mediaNext","mediaPrevious","mediaPlayPause","launch","chrome","steamMenu","steamQuickAccess","steamOverlay","steamContext","xboxGuide","fse","fseEnter","fseExit","fseSettings","volumeMixer","wifiNetworks","bluetoothSettings","displaySettings","networkSettings","hdrCalibration"};
    private static readonly Dictionary<string,ushort> Keys=new(StringComparer.OrdinalIgnoreCase){["Ctrl"]=0x11,["Control"]=0x11,["Shift"]=0x10,["Alt"]=0x12,["Win"]=0x5b,["Windows"]=0x5b,["Insert"]=0x2d,["Home"]=0x24,["End"]=0x23,["Delete"]=0x2e,["PageUp"]=0x21,["PageDown"]=0x22,["Space"]=0x20,["Enter"]=0x0d,["Return"]=0x0d,["Tab"]=0x09,["Escape"]=0x1b,["Esc"]=0x1b,["Backspace"]=0x08,["Up"]=0x26,["Down"]=0x28,["Left"]=0x25,["Right"]=0x27,["MediaNext"]=0xb0,["MediaPrevious"]=0xb1,["MediaPlayPause"]=0xb3};
    internal static ushort[] ParseShortcut(string value)
    {
        if(string.IsNullOrWhiteSpace(value)||value.Length>100)throw new ArgumentException("Invalid keyboard shortcut");
        var list=new List<ushort>();
        foreach(var token in value.Split('+',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries))
        {
            ushort key;if(Keys.TryGetValue(token,out key)){}
            else if(token.Length==1&&char.IsAsciiLetterOrDigit(token[0]))key=char.ToUpperInvariant(token[0]);
            else if(token.StartsWith('F')&&int.TryParse(token.AsSpan(1),out int f)&&f is >=1 and <=24)key=(ushort)(0x70+f-1);
            else throw new ArgumentException("Unsupported shortcut key: "+token);
            if(list.Contains(key))throw new ArgumentException("Duplicate shortcut key");list.Add(key);
        }
        if(list.Count is <1 or >5)throw new ArgumentException("Invalid shortcut length");return list.ToArray();
    }
    internal static void ToggleWindowsRotationLock() => SendShortcut("Win+O");
    private static void SendShortcut(string value)
    {
        foreach(int key in new[]{0x10,0x11,0x12,0x5b,0x5c})if((GetAsyncKeyState(key)&0x8000)!=0)throw new InvalidOperationException("Release modifier keys and try again");
        var keys=ParseShortcut(value);var input=keys.Select(v=>Key(v,false)).Concat(keys.Reverse().Select(v=>Key(v,true))).ToArray();
        if(SendInput((uint)input.Length,input,Marshal.SizeOf<Input>())!=input.Length)throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    private static List<nint> AppWindows(string app)
    {
        var windows=new List<nint>();
        EnumWindows((h,l)=>{try{if(!IsWindowVisible(h))return true;GetWindowThreadProcessId(h,out var id);using var p=Process.GetProcessById((int)id);if(p.ProcessName.Equals(app,StringComparison.OrdinalIgnoreCase)&&p.SessionId==Process.GetCurrentProcess().SessionId){var cls=new StringBuilder(256);GetClassName(h,cls,256);if(app!="explorer"||cls.ToString() is "CabinetWClass" or "ExploreWClass")windows.Add(h);}}catch(ArgumentException){}return true;},0);
        return windows;
    }
    private static bool ActivateWindow(nint window)
    {
        if(!IsWindow(window))return false;
        if(IsIconic(window))ShowWindowAsync(window,9);
        uint current=GetCurrentThreadId(),foreground=GetWindowThreadProcessId(GetForegroundWindow(),out _),targetThread=GetWindowThreadProcessId(window,out _);
        bool attachedForeground=foreground!=0&&foreground!=current&&AttachThreadInput(current,foreground,true),attachedTarget=targetThread!=0&&targetThread!=current&&targetThread!=foreground&&AttachThreadInput(current,targetThread,true);
        try{BringWindowToTop(window);SetForegroundWindow(window);SetFocus(window);}
        finally{if(attachedTarget)AttachThreadInput(current,targetThread,false);if(attachedForeground)AttachThreadInput(current,foreground,false);}
        if(GetForegroundWindow()!=window)
        {
            var alt=new[]{Key(0x12,false),Key(0x12,true)};SendInput(2,alt,Marshal.SizeOf<Input>());SetForegroundWindow(window);
        }
        return GetForegroundWindow()==window;
    }
    private static async Task<nint> LaunchForegroundApp(string action)
    {
        string app=action=="taskManager"?"Taskmgr":"explorer";var before=AppWindows(app);
        string path=Path.Combine(action=="taskManager"?Environment.GetFolderPath(Environment.SpecialFolder.System):Environment.GetFolderPath(Environment.SpecialFolder.Windows),action=="taskManager"?"Taskmgr.exe":"explorer.exe");
        using var launched=Process.Start(new ProcessStartInfo(path,action=="explorer"?"/n, shell:MyComputerFolder":""){UseShellExecute=true});
        nint candidate=0;
        for(int i=0;i<60;i++)
        {
            var found=AppWindows(app);candidate=found.FirstOrDefault(h=>!before.Contains(h));
            if(candidate==0&&i>=12)candidate=found.FirstOrDefault();
            if(candidate!=0&&ActivateWindow(candidate)){await Task.Delay(100);if(GetForegroundWindow()==candidate)return candidate;}
            await Task.Delay(100);
        }
        throw new InvalidOperationException("The window launched, but Windows did not allow it to receive focus");
    }
    private async Task<DesktopResult> ExecuteHomeAction(string action,string? token,string? shortcut,string? launchTarget,bool inGameBar)
    {
        Target? selected;lock(targetLock)selected=target;
        ControllerActionPlan? steam=action.StartsWith("steam",StringComparison.Ordinal)?SteamPlan(action,IsGameRunning()):null;
        if(action=="chrome"){launchTarget=ResolveChrome();action="launch";}
        bool directed=action is "fullscreen" or "optiScaler" or "reShade" or "shortcut";
        if(directed&&(selected is null||!Valid(selected)||(token is not null&&token!=selected.Token)))throw new InvalidOperationException("The target window changed; reopen the overlay");
        if(action is "fse" or "fseEnter" or "fseExit")GamingExperience.Validate(action=="fse"?null:action=="fseEnter");
        if(action=="launch")ValidateLaunch(launchTarget);
        if(action=="shortcut")ParseShortcut(shortcut??"");
        if(inGameBar)await DismissOverlay();
        if(action is "fse" or "fseEnter" or "fseExit"){await GamingExperience.Change(action=="fse"?null:action=="fseEnter");return new(action,true,"Windows Xbox mode",false,null);}
        if(action=="wifiNetworks"){using var networks=Process.Start(new ProcessStartInfo("ms-availablenetworks:"){UseShellExecute=true});return new(action,true,"Windows Wi-Fi networks",false,null);}
        if(action=="networkSettings"){using var settings=Process.Start(new ProcessStartInfo("ms-settings:network-status"){UseShellExecute=true});return new(action,true,"Windows Network and internet",false,null);}
        if(action=="displaySettings"){using var settings=Process.Start(new ProcessStartInfo("ms-settings:display"){UseShellExecute=true});return new(action,true,"Windows System Display",false,null);}
        if(action=="hdrCalibration"){bool installed=WindowsHdrDisplay.CalibrationInstalled();using var calibration=Process.Start(new ProcessStartInfo(installed?@"shell:AppsFolder\Microsoft.WindowsHDRCalibration_8wekyb3d8bbwe!App":"ms-windows-store://pdp/?ProductId=9N7F2SM5D1LR"){UseShellExecute=true});return new(action,true,installed?"Windows HDR Calibration":"Windows HDR Calibration in Microsoft Store",false,null);}
        if(action=="bluetoothSettings"){using var settings=Process.Start(new ProcessStartInfo("ms-settings:bluetooth"){UseShellExecute=true});return new(action,true,"Windows Bluetooth and devices",false,null);}
        if(action=="volumeMixer"){SendShortcut("Win+Ctrl+V");return new(action,true,"Windows Quick Settings volume mixer",false,null);}
        if(action=="fseSettings"){using var settings=Process.Start(new ProcessStartInfo("ms-settings:gaming-fullscreen"){UseShellExecute=true});return new(action,true,"Windows Xbox mode settings",false,null);}
        if(steam?.BigPicture==true)
        {
            var result=await ExecuteOemSteam(action=="steamQuickAccess"?"steamQuickAccess":"steamMenu",()=>!stopping.IsCancellationRequested,stopping.Token);
            return new(action,result.ForegroundVerified,"Steam Big Picture",false,result.ForegroundVerified?null:"Steam lost focus",ForegroundVerified:result.ForegroundVerified);
        }
        if(steam!=null&&selected!=null&&Valid(selected)&&!ActivateWindow(selected.Window))throw new InvalidOperationException("Could not focus the target window");
        if(steam!=null){await SendSteam(steam);return new(action,true,selected?.App,false,null,null,selected?.Window.ToInt64(),selected!=null?GetForegroundWindow()==selected.Window:null);}
        if(action=="xboxGuide"){await VirtualGuide.Tap();return new(action,true,"Virtual Xbox Guide",false,null);}
        if(directed&&!ActivateWindow(selected!.Window))throw new InvalidOperationException("Could not focus the target window");
        if(action=="launch")
        {
            string destination=launchTarget!;var start=new ProcessStartInfo(destination){UseShellExecute=true};
            if(Path.GetExtension(destination).Equals(".ps1",StringComparison.OrdinalIgnoreCase))
            {start=await PowerShellScriptLaunch.CreateAsync(destination);}
            using var process=Process.Start(start);return new(action,true,null,false,null);
        }
        if(action=="fullscreen"){SendShortcut("F11");await Task.Delay(120);if(Valid(selected!)&&ActivateWindow(selected!.Window))SendShortcut("Alt+Enter");else throw new InvalidOperationException("The target window changed after F11");}
        else SendShortcut(action switch{"optiScaler"=>"Insert","reShade"=>"Home","desktop"=>"Win+D","altTab"=>"Alt+Tab","altTabBack"=>"Alt+Shift+Tab","mediaNext"=>"MediaNext","mediaPrevious"=>"MediaPrevious","mediaPlayPause"=>"MediaPlayPause",_=>shortcut!});
        return new(action,true,selected?.App,false,null,null,selected?.Window.ToInt64(),directed?GetForegroundWindow()==selected!.Window:null);
    }
    internal static void ValidateLaunch(string? path)
    {
        if(string.IsNullOrWhiteSpace(path)||path.Length>2048)throw new ArgumentException("Enter an EXE, PS1 path or URL");
        if(Uri.TryCreate(path,UriKind.Absolute,out var uri)&&uri.Scheme is "https" or "http" or "steam" or "msxbox" or "ms-windows-store" or "spotify")return;
        if(!Path.IsPathFullyQualified(path)||!File.Exists(path)||Path.GetExtension(path).ToLowerInvariant() is not (".exe" or ".ps1"))throw new ArgumentException("The selected EXE/PS1 file does not exist");
    }
    private delegate bool EnumWindow(nint window,nint parameter);
    [DllImport("user32.dll")]private static extern bool EnumWindows(EnumWindow callback,nint parameter);
    [DllImport("user32.dll")]private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")]private static extern bool BringWindowToTop(nint window);
    [DllImport("user32.dll")]private static extern nint SetFocus(nint window);
    [DllImport("user32.dll")]private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")]private static extern bool ShowWindowAsync(nint window,int command);
    [DllImport("user32.dll")]private static extern bool AttachThreadInput(uint from,uint to,bool attach);
    [DllImport("kernel32.dll")]private static extern uint GetCurrentThreadId();
}
