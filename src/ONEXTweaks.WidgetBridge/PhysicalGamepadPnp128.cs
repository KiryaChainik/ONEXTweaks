using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Onex.RecoveryExecutor128;

internal static class PhysicalGamepadPnp128
{
    private const int MaxNodes = 24;
    private const string OriginalUsbModel = @"USB\VID_045E&PID_028E\";

    internal static void RequireInterface(string path, string expectedRootSha)
    {
        if (path is null || path.Length is < 5 or > 1023 || !path.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            !IsPrintableAscii(path) || !IsDigest(expectedRootSha))
            throw new InvalidOperationException("physical128-interface-identity");

        using var set = SetupDiCreateDeviceInfoList(IntPtr.Zero, IntPtr.Zero);
        var iface = new DeviceInterfaceData { Size = (uint)Marshal.SizeOf<DeviceInterfaceData>() };
        if (set.IsInvalid || !SetupDiOpenDeviceInterfaceW(set, path, 0, ref iface) || (iface.Flags & 5) != 1)
            throw new InvalidOperationException("physical128-interface-not-active");
        var info = new DeviceInfoData { Size = (uint)Marshal.SizeOf<DeviceInfoData>() };
        if (SetupDiGetDeviceInterfaceDetailW(set, ref iface, IntPtr.Zero, 0, out var bytes, ref info) ||
            Marshal.GetLastWin32Error() != 122 || bytes < 8 || bytes > 4096 || info.DevInst == 0)
            throw new InvalidOperationException("physical128-interface-devnode");
        if (CM_Get_DevNode_Status(out var flags, out var problem, info.DevInst, 0) != 0 ||
            (flags & 8) == 0 || problem != 0)
            throw new InvalidOperationException("physical128-endpoint-not-started");
        if (CM_Locate_DevNodeW(out uint root, null, 0) != 0 || root == 0 || root == info.DevInst)
            throw new InvalidOperationException("physical128-ancestor-root-unavailable");

        var chain = new string[MaxNodes];
        var text = new StringBuilder(512);
        uint current = info.DevInst;
        for (int i = 0; i < chain.Length; i++)
        {
            if (current == root)
            {
                RequireOriginalChain(chain.AsSpan(0, i), expectedRootSha, reachedRoot: true);
                return;
            }
            text.Clear();
            if (CM_Get_Device_IDW(current, text, (uint)text.Capacity, 0) != 0)
                throw new InvalidOperationException("physical128-ancestor-id");
            chain[i] = text.ToString();
            if (CM_Get_Parent(out uint parent, current, 0) != 0 || parent == 0 || parent == current)
                throw new InvalidOperationException("physical128-ancestor-unavailable");
            current = parent;
        }
        throw new InvalidOperationException("physical128-ancestor-limit");
    }

    internal static void RequireOriginalChain(ReadOnlySpan<string> chain, string expectedRootSha, bool reachedRoot)
    {
        if (!reachedRoot || chain.Length < 1 || chain.Length >= MaxNodes || !IsDigest(expectedRootSha))
            throw new InvalidOperationException("physical128-ancestor-chain");

        int matches = 0;
        foreach (string node in chain)
        {
            if (string.IsNullOrEmpty(node) || node.Length >= 512 || !IsPrintableAscii(node))
                throw new InvalidOperationException("physical128-ancestor-id");
            string canonical = node.ToUpperInvariant();
            if (canonical.Contains("VIGEM", StringComparison.Ordinal) ||
                canonical.Contains("VIIPER", StringComparison.Ordinal) ||
                canonical.Contains("USBIP", StringComparison.Ordinal) ||
                canonical.Contains("VIRTUAL", StringComparison.Ordinal) ||
                canonical.StartsWith(@"SWD\", StringComparison.Ordinal))
                throw new InvalidOperationException("physical128-virtual-ancestor");
            if (canonical.StartsWith(OriginalUsbModel, StringComparison.Ordinal) &&
                canonical.Length > OriginalUsbModel.Length &&
                Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(canonical))) == expectedRootSha)
                matches++;
        }
        if (matches != 1) throw new InvalidOperationException("physical128-original-usb-root");
    }

    internal static bool IsPrintableAscii(string text)
    {
        foreach (char c in text)
            if (c < 0x21 || c > 0x7E) return false;
        return true;
    }

    private static bool IsDigest(string text)
    {
        if (text is null || text.Length != 64) return false;
        foreach (char c in text)
            if (c is not (>= '0' and <= '9') and not (>= 'A' and <= 'F')) return false;
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInterfaceData
    {
        internal uint Size;
        internal Guid Class;
        internal uint Flags;
        internal UIntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfoData
    {
        internal uint Size;
        internal Guid Class;
        internal uint DevInst;
        internal UIntPtr Reserved;
    }

    private sealed class DeviceInfoSet : SafeHandleMinusOneIsInvalid
    {
        private DeviceInfoSet() : base(true) { }
        protected override bool ReleaseHandle() => SetupDiDestroyDeviceInfoList(handle);
    }

    [DllImport("setupapi.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern DeviceInfoSet SetupDiCreateDeviceInfoList(IntPtr cls, IntPtr parent);

    [DllImport("setupapi.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiOpenDeviceInterfaceW(DeviceInfoSet set, string path, uint flags, ref DeviceInterfaceData iface);

    [DllImport("setupapi.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetailW(DeviceInfoSet set, ref DeviceInterfaceData iface,
        IntPtr detail, uint capacity, out uint bytes, ref DeviceInfoData info);

    [DllImport("setupapi.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [DllImport("cfgmgr32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint CM_Get_Device_IDW(uint node, StringBuilder id, uint count, uint flags);

    [DllImport("cfgmgr32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint CM_Locate_DevNodeW(out uint node, string? id, uint flags);

    [DllImport("cfgmgr32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint CM_Get_Parent(out uint parent, uint node, uint flags);

    [DllImport("cfgmgr32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint CM_Get_DevNode_Status(out uint status, out uint problem, uint node, uint flags);
}
