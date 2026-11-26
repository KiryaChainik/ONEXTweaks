// SPDX-License-Identifier: AGPL-3.0-only
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
namespace ONEXTweaks.WidgetBridge;
internal sealed record ControllerActionPlan(string Shortcut,bool BigPicture);
internal sealed partial class DesktopActions
{
    internal Func<bool> IsGameRunning {get;set;}=()=>false;
    internal static ControllerActionPlan SteamPlan(string action,bool gameRunning)=>action switch
    {
        "steamMenu"=>new("F1",true),"steamQuickAccess"=>new("Ctrl+2",true),
        "steamOverlay"=>new("Shift+Tab",false),"steamContext"=>gameRunning?new("Shift+Tab",false):new("F1",true),
        _=>throw new ArgumentException("Unknown Steam action")
    };
    internal static string ResolveChrome()
    {
        var candidates=new List<string?>();
        foreach(var hive in new[]{Registry.CurrentUser,Registry.LocalMachine})
        foreach(string root in new[]{@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe",@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe"})
        {using var key=hive.OpenSubKey(root);candidates.Add((key?.GetValue("") as string)?.Trim('"'));}
        foreach(string root in new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)})candidates.Add(Path.Combine(root,"Google","Chrome","Application","chrome.exe"));
        return candidates.FirstOrDefault(p=>p!=null&&Path.IsPathFullyQualified(p)&&File.Exists(p)&&Path.GetFileName(p).Equals("chrome.exe",StringComparison.OrdinalIgnoreCase))??throw new FileNotFoundException("Google Chrome is not installed. Install Chrome to use this action.");
    }
    private static async Task SendSteam(ControllerActionPlan plan,Func<bool>? stillFocused=null)
    {
        foreach(int key in new[]{0x10,0x11,0x12,0x5b,0x5c})if((GetAsyncKeyState(key)&0x8000)!=0)throw new InvalidOperationException("Release modifier keys and try again");
        var keys=ParseShortcut(plan.Shortcut);
        Input InputFor(ushort key,bool up)
        {
            if(!plan.BigPicture)return Key(key,up);
            uint scan=MapVirtualKeyW(key,0);
            if(scan==0||scan>255)throw new InvalidOperationException("Steam shortcut scan code is unavailable");
            bool useScanCode=keys.Length>1;
            return new(){Type=1,Data=new InputData{Keyboard=new KeyboardInput{
                VirtualKey=useScanCode?(ushort)0:key,
                Scan=(ushort)scan,
                Flags=(useScanCode?8u:0u)|(up?2u:0u)
            }}};
        }
        var presses=keys.Select(key=>InputFor(key,false)).ToArray();
        var releases=keys.Select(key=>InputFor(key,true)).ToArray();
        int pressed=0;
        try
        {
            foreach(var input in presses)
            {
                if(stillFocused?.Invoke()==false)throw new InvalidOperationException("Steam lost focus; the menu shortcut was cancelled.");
                if(SendInput(1,new[]{input},Marshal.SizeOf<Input>())!=1)throw new Win32Exception(Marshal.GetLastWin32Error());
                pressed++;
                await Task.Delay(30);
            }
            await Task.Delay(30);
        }
        finally
        {
            if(pressed>0)
            {
                var release=releases.Take(pressed).Reverse().ToArray();
                if(SendInput((uint)release.Length,release,Marshal.SizeOf<Input>())!=release.Length)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
    }
    [DllImport("user32.dll",ExactSpelling=true)]private static extern uint MapVirtualKeyW(uint code,uint mapType);
}
/// <summary>A genuine Guide tap, as in ClawTweaks; never substitute Win+G.</summary>
internal static class VirtualGuide
{
 private static readonly SemaphoreSlim gate=new(1,1);private static ControllerVirtualPad? transient;
 private static int index=-1;internal static int OwnIndex=>Volatile.Read(ref index);
 internal static async Task<int> Tap()
 {
  await gate.WaitAsync();
  try
  {
   int? remapped=await ControllerRemapper.TapGuide();if(remapped.HasValue)return remapped.Value;
   using var config=Registry.CurrentUser.OpenSubKey(@"Software\ONEXTweaks\Overlay");int physical=config?.GetValue("GuideControllerIndex") as int? ??0;
   using var pad=new ControllerVirtualPad(physical,false);transient=pad;Volatile.Write(ref index,pad.Slot);
   try{pad.Guide(true);await Task.Delay(100);}finally{pad.Guide(false);}
   return pad.Slot;
  }
  finally{transient=null;Volatile.Write(ref index,-1);gate.Release();}
 }
 internal static void Disconnect(){try{transient?.Dispose();}finally{transient=null;Volatile.Write(ref index,-1);}}
}
