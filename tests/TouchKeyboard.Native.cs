// Independent interactive Windows verification. No text or window titles are collected.
using System;
using System.Runtime.InteropServices;
using System.Threading;
public static class OnexNativeTouchKeyboard
{
    public sealed class Snapshot
    {
        public bool visible, docked;
        public int left, top, right, bottom, screenLeft, screenTop, screenRight, screenBottom;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Monitor { public int size; public Rect bounds,work; public uint flags; }
    [ComImport,Guid("5752238B-24F0-495A-82F1-2FD593056796"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPane
    {
        [PreserveSig] int Advise(IntPtr window,IntPtr handler,out uint cookie);
        [PreserveSig] int AdviseWithHWND(IntPtr window,IntPtr handler,out uint cookie);
        [PreserveSig] int Unadvise(uint cookie);
        [PreserveSig] int Location(out Rect rect);
    }
    [ComImport,Guid("37C994E7-432B-4834-A2F7-DCE1F13B834B"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IInvocation { void Toggle(IntPtr window); }
    public static Snapshot Read()
    {
        IntPtr old=SetThreadDpiAwarenessContext(new IntPtr(-4));
        try
        {
            object pane=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("D5120AA3-46BA-44C5-822D-CA8092C1FC72")));
            Rect rect;
            try { Marshal.ThrowExceptionForHR(((IPane)pane).Location(out rect)); }
            finally { Marshal.FinalReleaseComObject(pane); }
            Monitor m=new Monitor();m.size=Marshal.SizeOf(typeof(Monitor));
            if(!GetMonitorInfo(MonitorFromWindow(GetForegroundWindow(),2),ref m))throw new Exception("Monitor read failed");
            bool visible=rect.right>rect.left&&rect.bottom>rect.top;
            return new Snapshot {visible=visible,docked=visible&&Math.Abs(rect.bottom-m.bounds.bottom)<=12&&Math.Abs(rect.left-m.bounds.left)<=12&&Math.Abs(rect.right-m.bounds.right)<=12&&rect.top>m.bounds.top,
                left=rect.left,top=rect.top,right=rect.right,bottom=rect.bottom,screenLeft=m.bounds.left,screenTop=m.bounds.top,screenRight=m.bounds.right,screenBottom=m.bounds.bottom};
        }
        finally {if(old!=IntPtr.Zero)SetThreadDpiAwarenessContext(old);}
    }
    public static void HideForTest()
    {
        if(!Read().visible)return;
        object invocation=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("4CE576FA-83DC-4F88-951C-9D0782B4E376")));
        try {((IInvocation)invocation).Toggle(GetDesktopWindow());}
        finally {Marshal.FinalReleaseComObject(invocation);}
        for(int i=0;i<50;i++){Thread.Sleep(100);if(!Read().visible)return;}
        throw new Exception("Touch keyboard did not hide before verification");
    }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor,ref Monitor info);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
}
