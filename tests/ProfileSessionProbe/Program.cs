// SPDX-License-Identifier: AGPL-3.0-only
using System.Runtime.InteropServices;
using System.Text.Json;

internal static class Program
{
    private delegate nint WindowProc(nint window,uint message,nuint w,nint l);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    private struct WindowClass
    {
        internal uint Style;internal nint Procedure;internal int ClassExtra,WindowExtra;
        internal nint Instance,Icon,Cursor,Background;internal string? Menu;
        internal string Name;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Message{internal nint Window;internal uint Id;internal nuint W;internal nint L;internal uint Time;internal int X,Y;internal uint Private;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]private static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern ushort RegisterClass(ref WindowClass value);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern nint CreateWindowEx(uint extra,string cls,string title,uint style,int x,int y,int width,int height,nint parent,nint menu,nint instance,nint parameter);
    [DllImport("user32.dll")]private static extern bool ShowWindow(nint window,int mode);
    [DllImport("user32.dll")]private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")]private static extern int GetMessage(out Message message,nint window,uint min,uint max);
    [DllImport("user32.dll")]private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")]private static extern nint DispatchMessage(ref Message message);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern nint DefWindowProc(nint window,uint message,nuint w,nint l);
    [DllImport("user32.dll")]private static extern void PostQuitMessage(int code);
    [DllImport("gdi32.dll")]private static extern nint CreateSolidBrush(uint color);
    [DllImport("user32.dll")]private static extern bool SetProcessDPIAware();
    private static int Main()
    {
        SetProcessDPIAware();
        WindowProc procedure=(window,message,w,l)=>{if(message==2)PostQuitMessage(0);return DefWindowProc(window,message,w,l);};
        var cls=new WindowClass{Instance=GetModuleHandle(null),Name="ONEXTweaks.ProfileSessionProbe",Procedure=Marshal.GetFunctionPointerForDelegate(procedure),Background=CreateSolidBrush(0x222222)};
        if(RegisterClass(ref cls)==0)throw new InvalidOperationException("Probe class registration failed");
        nint handle=CreateWindowEx(0,cls.Name,"ONEXTweaks · own profile session verification",0x10CF0000,100,100,900,650,0,0,cls.Instance,0);
        if(handle==0)throw new InvalidOperationException("Probe window was not created");
        ShowWindow(handle,9);SetForegroundWindow(handle);
        File.WriteAllText("ProfileSessionProbe.json",JsonSerializer.Serialize(new{processId=Environment.ProcessId,window=handle.ToInt64()}));
        while(GetMessage(out var message,0,0,0)>0){TranslateMessage(ref message);DispatchMessage(ref message);}
        GC.KeepAlive(procedure);return 0;
    }
}
