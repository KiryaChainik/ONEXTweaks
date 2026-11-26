// SPDX-License-Identifier: AGPL-3.0-only
// RTSS SDK RTSSHooksInterface.h preview sample: actual RTSS renderer and limiter on a D3D9 surface.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
internal static partial class Program
{
    [StructLayout(LayoutKind.Sequential)]private struct Parameters9
    {
        public uint Width,Height,Format,Count,Sample,Quality,Swap;
        public IntPtr Window;
        [MarshalAs(UnmanagedType.Bool)]public bool Windowed,Depth;
        public uint DepthFormat,Flags,Refresh,Interval;
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int Scene9(IntPtr device);
    [DllImport("d3d9.dll")]private static extern IntPtr Direct3DCreate9(uint version);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int Create9(IntPtr api,uint adapter,uint type,IntPtr window,uint flags,ref Parameters9 p,out IntPtr device);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int Clear9(IntPtr device,uint count,IntPtr rect,uint flags,uint color,float z,uint stencil);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int Present9(IntPtr device,IntPtr source,IntPtr target,IntPtr window,IntPtr dirty);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl,CharSet=CharSet.Ansi)]private delegate void InitPreview([MarshalAs(UnmanagedType.LPStr)]string profile);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate void BeginPreview(IntPtr device);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate void EndPreview();
    private static int RunPreview(IntPtr window,WindowProc proc)
    {
        string dll=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"RivaTuner Statistics Server","RTSSHooks64.dll");
        IntPtr library=NativeLibrary.Load(dll);
        T Export<T>(string name)where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library,name));
        var init=Export<InitPreview>("InitPreview");var begin=Export<BeginPreview>("RenderPreviewBeginD3D9");var end=Export<EndPreview>("RenderPreviewEnd");var cleanup=Export<EndPreview>("CleanupPreview");
        IntPtr api=Direct3DCreate9(32);if(api==IntPtr.Zero)throw new InvalidOperationException("D3D9 is unavailable");
        var p=new Parameters9{Count=1,Swap=1,Window=window,Windowed=true,Interval=0x80000000};
        Marshal.ThrowExceptionForHR(Method<Create9>(api,16)(api,0,1,window,0x20,ref p,out var device));
        init("");var clear=Method<Clear9>(device,43);var timer=Stopwatch.StartNew();long last=0;int count=0;
        try
        {
            while(timer.Elapsed.TotalMinutes<3)
            {
                while(PeekMessage(out var message,IntPtr.Zero,0,0,1)){if(message.Message==0x12)return 0;TranslateMessage(ref message);DispatchMessage(ref message);}
                Marshal.ThrowExceptionForHR(clear(device,0,IntPtr.Zero,1,0xff090d13,1,0));
                Marshal.ThrowExceptionForHR(Method<Scene9>(device,41)(device));begin(device);Marshal.ThrowExceptionForHR(Method<Scene9>(device,42)(device));Marshal.ThrowExceptionForHR(Method<Present9>(device,17)(device,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero));end();count++;
                if(timer.ElapsedMilliseconds-last>=1000){File.WriteAllText("RTSSRenderProbe-fps.json",JsonSerializer.Serialize(new{fps=count*1000.0/(timer.ElapsedMilliseconds-last),processId=Environment.ProcessId,window=window.ToInt64(),renderer="RTSS SDK D3D9 preview"}));last=timer.ElapsedMilliseconds;count=0;}
                Thread.Sleep(1);
            }
        }
        finally{cleanup();Marshal.Release(device);Marshal.Release(api);NativeLibrary.Free(library);DestroyWindow(window);GC.KeepAlive(proc);}
        return 0;
    }
}
