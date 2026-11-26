// SPDX-License-Identifier: AGPL-3.0-only
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ONEXTweaks.WidgetBridge;

internal static class BluetoothConnections
{
    // Windows SDK bthioctl.h: BTH_CTL(BTH_IOCTL_BASE + 0x03).
    // Disconnects the link; it does not delete the pairing or disable services.
    private const uint DisconnectDevice=0x0041000C;
    internal static ulong ParseAddress(string address)
    {
        string hex=address.Replace(":", "",StringComparison.Ordinal).Replace("-", "",StringComparison.Ordinal);
        if(hex.Length!=12||!ulong.TryParse(hex,NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out ulong value)||value==0||value==0xFFFFFFFFFFFF)
            throw new ArgumentException("Invalid Bluetooth address");
        return value;
    }
    internal static void Disconnect(string address)
    {
        ulong peer=ParseAddress(address);int error=1167;bool found=false;
        var parameters=new RadioParameters{Size=(uint)Marshal.SizeOf<RadioParameters>()};
        nint search=BluetoothFindFirstRadio(ref parameters,out var radio);
        if(search==0){radio?.Dispose();throw new InvalidOperationException("Bluetooth radio is unavailable");}
        try
        {
            do
            {
                using(radio)
                {
                    found=true;
                    if(DeviceIoControl(radio,DisconnectDevice,ref peer,8,nint.Zero,0,out _,nint.Zero))return;
                    error=Marshal.GetLastWin32Error();
                }
            }while(BluetoothFindNextRadio(search,out radio));
        }
        finally{BluetoothFindRadioClose(search);}
        if(found&&error is 1167 or 2250)return; // Already disconnected; UI still reads actual state.
        throw new Win32Exception(error,"Windows did not disconnect the Bluetooth link");
    }
    internal static object Probe()
    {
        int count=0;var parameters=new RadioParameters{Size=(uint)Marshal.SizeOf<RadioParameters>()};
        nint search=BluetoothFindFirstRadio(ref parameters,out var radio);
        if(search!=0)try{do{using(radio){if(!radio.IsInvalid)count++;}}while(BluetoothFindNextRadio(search,out radio));}finally{BluetoothFindRadioClose(search);}
        else radio?.Dispose();
        int rejected=0;foreach(string invalid in new[]{"", "000000000000", "FFFFFFFFFFFF", "not-an-address", "12345678901234"})try{ParseAddress(invalid);}catch(ArgumentException){rejected++;}
        if(rejected!=5)throw new InvalidOperationException("Bluetooth address validation failed");
        return new{passed=count>0,radioCount=count,invalidAddressesRejected=rejected,radioWrites=0,disconnectOperations=0,pairingChanges=0};
    }
    [StructLayout(LayoutKind.Sequential)]private struct RadioParameters{internal uint Size;}
    [DllImport("BluetoothAPIs.dll",SetLastError=true)]private static extern nint BluetoothFindFirstRadio(ref RadioParameters parameters,out SafeFileHandle radio);
    [DllImport("BluetoothAPIs.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool BluetoothFindNextRadio(nint search,out SafeFileHandle radio);
    [DllImport("BluetoothAPIs.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool BluetoothFindRadioClose(nint search);
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool DeviceIoControl(SafeFileHandle device,uint code,ref ulong input,uint inputBytes,nint output,uint outputBytes,out uint returned,nint overlapped);
}
