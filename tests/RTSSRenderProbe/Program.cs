// SPDX-License-Identifier: AGPL-3.0-only
// COM vtables: Windows SDK d3d11.h/dxgi.h. Dedicated window; never injects into a user's game.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
internal static partial class Program
{
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct WndClass{public uint Style;public IntPtr Proc;public int ClsExtra,WndExtra;public IntPtr Instance,Icon,Cursor,Background;public string? Menu;public string Name;}
    [StructLayout(LayoutKind.Sequential)]private struct Mode{public uint Width,Height,RateNumerator,RateDenominator,Format,Scanline,Scaling;}
    [StructLayout(LayoutKind.Sequential)]private struct SwapDesc{public Mode Mode;public uint SampleCount,SampleQuality,Usage,Count;public IntPtr Window;[MarshalAs(UnmanagedType.Bool)]public bool Windowed;public uint Effect,Flags;}
    [StructLayout(LayoutKind.Sequential)]private struct Msg{public IntPtr Window;public uint Message;public UIntPtr WParam;public IntPtr LParam;public uint Time;public int X,Y;public uint Private;}
    private delegate IntPtr WindowProc(IntPtr w,uint m,UIntPtr a,IntPtr b);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int GetBuffer(IntPtr s,uint i,ref Guid iid,out IntPtr buffer);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int CreateTarget(IntPtr d,IntPtr resource,IntPtr description,out IntPtr target);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate void Clear(IntPtr c,IntPtr view,[MarshalAs(UnmanagedType.LPArray,SizeConst=4)]float[] color);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int Present(IntPtr s,uint interval,uint flags);
    [DllImport("d3d11.dll")]private static extern int D3D11CreateDeviceAndSwapChain(IntPtr adapter,int driver,IntPtr software,uint flags,IntPtr levels,uint count,uint version,ref SwapDesc desc,out IntPtr swap,out IntPtr device,out uint feature,out IntPtr context);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern ushort RegisterClass(ref WndClass c);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern IntPtr CreateWindowEx(uint ex,string cls,string title,uint style,int x,int y,int width,int height,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr param);
    [DllImport("user32.dll")]private static extern bool PeekMessage(out Msg m,IntPtr w,uint min,uint max,uint remove);
    [DllImport("user32.dll")]private static extern bool TranslateMessage(ref Msg m);
    [DllImport("user32.dll")]private static extern IntPtr DispatchMessage(ref Msg m);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern IntPtr DefWindowProc(IntPtr w,uint m,UIntPtr a,IntPtr b);
    [DllImport("user32.dll")]private static extern bool DestroyWindow(IntPtr w);
    [DllImport("user32.dll")]private static extern bool SetProcessDPIAware();
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")]private static extern bool ShowWindow(IntPtr h,int mode);
    [DllImport("user32.dll")]private static extern bool SetForegroundWindow(IntPtr h);
    private static T Method<T>(IntPtr obj,int slot)where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj),slot*IntPtr.Size));
    private static int Main()
    {
        SetProcessDPIAware();WindowProc proc=(w,m,a,b)=>DefWindowProc(w,m,a,b);
        IntPtr instance=GetModuleHandle(null);var cls=new WndClass{Instance=instance,Name="ONEXTweaks.RenderProbe",Proc=Marshal.GetFunctionPointerForDelegate(proc)};RegisterClass(ref cls);
        IntPtr window=CreateWindowEx(0,cls.Name,"ONEXTweaks · RTSS / FPS verification",0x10CF0000,160,40,1600,1100,IntPtr.Zero,IntPtr.Zero,instance,IntPtr.Zero);
        ShowWindow(window,9);SetForegroundWindow(window);
        if(Environment.GetEnvironmentVariable("ONEX_PROBE_API")=="RTSSPreview")return RunPreview(window,proc);
        var desc=new SwapDesc{Mode=new Mode{Format=28},SampleCount=1,Usage=32,Count=1,Window=window,Windowed=true};
        Marshal.ThrowExceptionForHR(D3D11CreateDeviceAndSwapChain(IntPtr.Zero,1,IntPtr.Zero,0,IntPtr.Zero,0,7,ref desc,out var swap,out var device,out _,out var context));
        Guid iid=new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");Marshal.ThrowExceptionForHR(Method<GetBuffer>(swap,9)(swap,0,ref iid,out var back));
        Marshal.ThrowExceptionForHR(Method<CreateTarget>(device,9)(device,back,IntPtr.Zero,out var target));Marshal.Release(back);
        var pipeline=CreateTriangle(device,context,target);
        // Match RTSS's own DesktopOverlayHost: a post-D3D popup triggers its delayed module detection.
        IntPtr dummy=CreateWindowEx(0,cls.Name,"",0x80000000,0,0,0,0,IntPtr.Zero,IntPtr.Zero,instance,IntPtr.Zero);DestroyWindow(dummy);
        var clear=Method<Clear>(context,50);var present=Method<Present>(swap,8);var timer=Stopwatch.StartNew();long last=0;int count=0;
        try
        {
            while(timer.Elapsed.TotalMinutes<3)
            {
                while(PeekMessage(out var message,IntPtr.Zero,0,0,1)){if(message.Message==0x12)return 0;TranslateMessage(ref message);DispatchMessage(ref message);}
                clear(context,target,new[]{0.035f,0.05f,0.075f,1f});DrawTriangle(context);Marshal.ThrowExceptionForHR(Method<Present>(swap,8)(swap,0,0));count++;
                if(timer.ElapsedMilliseconds-last>=1000){File.WriteAllText("RTSSRenderProbe-fps.json",JsonSerializer.Serialize(new{fps=count*1000.0/(timer.ElapsedMilliseconds-last),processId=Environment.ProcessId,window=window.ToInt64()}));last=timer.ElapsedMilliseconds;count=0;}
                Thread.Sleep(1);
            }
        }
        finally{foreach(var item in pipeline)Marshal.Release(item);Marshal.Release(target);Marshal.Release(swap);Marshal.Release(context);Marshal.Release(device);DestroyWindow(window);GC.KeepAlive(proc);}
        return 0;
    }
}
