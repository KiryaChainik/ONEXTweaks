// SPDX-License-Identifier: AGPL-3.0-only
using System.Runtime.InteropServices;

namespace ONEXTweaks.WidgetBridge;

internal static class NativeGameBarState
{
    // Windows SDK Windows.Gaming.UI.IGameBarStatics: IInspectable (6 slots),
    // four event accessors, Visible, IsInputRedirected. WinRT boolean is one byte.
    private static readonly Guid InterfaceId = new("1DB9A292-CC78-4173-BE45-B61E67283EA7");
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetBoolean(nint self, out byte value);

    internal static bool IsInputRedirected()
    {
        int initialized = RoInitialize(1);
        if (initialized < 0 && initialized != unchecked((int)0x80010106)) Marshal.ThrowExceptionForHR(initialized);
        nint name = 0, factory = 0;
        try
        {
            const string type = "Windows.Gaming.UI.GameBar";
            Marshal.ThrowExceptionForHR(WindowsCreateString(type, type.Length, out name));
            Guid iid = InterfaceId;
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(name, ref iid, out factory));
            nint vtable = Marshal.ReadIntPtr(factory);
            var get = Marshal.GetDelegateForFunctionPointer<GetBoolean>(Marshal.ReadIntPtr(vtable, 11 * nint.Size));
            Marshal.ThrowExceptionForHR(get(factory, out byte value));
            return value != 0;
        }
        finally
        {
            if (factory != 0) Marshal.Release(factory);
            if (name != 0) WindowsDeleteString(name);
            if (initialized >= 0) RoUninitialize();
        }
    }

    [DllImport("combase.dll")] private static extern int RoInitialize(uint type);
    [DllImport("combase.dll")] private static extern void RoUninitialize();
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] private static extern int WindowsCreateString(string source, int length, out nint value);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(nint value);
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(nint name, ref Guid iid, out nint factory);
}
