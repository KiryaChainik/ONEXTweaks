// SPDX-License-Identifier: AGPL-3.0-only
// Native signatures: Microsoft Windows SDK gamingexperience.h.
using System.Runtime.InteropServices;
using Microsoft.Win32;
namespace ONEXTweaks.WidgetBridge;
internal sealed record FseState(bool Available,bool Supported,bool Active,bool CanSet,bool HomeAppConfigured,bool EntryRequiresRestart,string? Error);
internal static class GamingExperience
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]private delegate int Query();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]private delegate int SetMode(int active);
    private static readonly nint library;private static readonly Query? supported,active,canSet;private static readonly SetMode? set;
    static GamingExperience()
    {
        if(NativeLibrary.TryLoad("api-ms-win-gaming-experience-l1-1-0.dll",typeof(GamingExperience).Assembly,DllImportSearchPath.System32,out library))
        {
            T Load<T>(string name) where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library,name));
            try{supported=Load<Query>("IsGamingFullScreenExperienceSupported");active=Load<Query>("IsGamingFullScreenExperienceActive");canSet=Load<Query>("CanSetGamingFullScreenExperience");set=Load<SetMode>("SetGamingFullScreenExperience");}catch(EntryPointNotFoundException){supported=null;}
        }
    }
    internal static FseState Read()
    {
        try
        {
            using var config=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\GamingConfiguration");using var dialog=config?.OpenSubKey("SystemDialogResults");
            bool home=!string.IsNullOrWhiteSpace(config?.GetValue("GamingHomeApp") as string);bool restart=(dialog?.GetValue("EnterGamingPostureConfirmation_Config")??dialog?.GetValue("EnterGamingPostureConfirmation")) is int choice&&choice==1;
            if(supported==null||active==null||canSet==null||set==null)return new(false,false,false,false,home,restart,"Windows Xbox mode API unavailable");
            bool support=supported()!=0,on=active()!=0,change=canSet()!=0;
            return new(true,support,on,change,home,restart,!support?"Xbox mode is not enabled on this device":!change?"Windows cannot switch Xbox mode right now":null);
        }
        catch(Exception e){return new(false,false,false,false,false,false,e.Message);}
    }
    internal static bool IsActive()=>active!=null&&active()!=0;
    internal static void Validate(bool? desired)
    {
        var s=Read();if(!s.Available||!s.Supported||!s.CanSet)throw new InvalidOperationException(s.Error??"Xbox mode unavailable");
        if((desired??!s.Active)&&!s.Active&&s.EntryRequiresRestart)throw new InvalidOperationException("Windows is configured to restart when entering Xbox mode. Choose Continue without restarting in Windows first.");
    }
    internal static async Task<FseState> Change(bool? desired)
    {
        Validate(desired);var before=Read();bool target=desired??!before.Active;if(before.Active==target)return before;
        Marshal.ThrowExceptionForHR(set!(target?1:0));
        for(int n=0;n<80;n++){await Task.Delay(100);var actual=Read();if(actual.Active==target)return actual;}
        throw new TimeoutException("Windows has not confirmed the Xbox mode change. Check its on-screen prompt.");
    }
}
