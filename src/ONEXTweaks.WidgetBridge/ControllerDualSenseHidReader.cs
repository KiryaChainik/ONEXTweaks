// SPDX-License-Identifier: AGPL-3.0-only
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Devices.Enumeration;
using Windows.Devices.HumanInterfaceDevice;
namespace ONEXTweaks.WidgetBridge;
/// <summary>Independent Windows HID transport for an endpoint proved by its per-instance feature nonce.</summary>
internal sealed class ControllerDualSenseHidReader:IDisposable
{
 [StructLayout(LayoutKind.Sequential)]private struct Attributes{internal uint Size;internal ushort Vendor,Product,Version;}
 [StructLayout(LayoutKind.Explicit,Size=64)]private struct Caps{[FieldOffset(0)]internal ushort Usage;[FieldOffset(2)]internal ushort Page;[FieldOffset(4)]internal ushort InputLength;[FieldOffset(6)]internal ushort OutputLength;[FieldOffset(8)]internal ushort FeatureLength;}
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]private static extern SafeFileHandle CreateFileW(string name,uint access,uint share,nint security,uint creation,uint flags,nint template);
 [DllImport("hid.dll",SetLastError=true)][DefaultDllImportSearchPaths(DllImportSearchPath.System32)][return:MarshalAs(UnmanagedType.I1)]private static extern bool HidD_GetAttributes(SafeFileHandle handle,ref Attributes value);
 [DllImport("hid.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)][return:MarshalAs(UnmanagedType.I1)]private static extern bool HidD_GetPreparsedData(SafeFileHandle handle,out nint data);
 [DllImport("hid.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)][return:MarshalAs(UnmanagedType.I1)]private static extern bool HidD_FreePreparsedData(nint data);
 [DllImport("hid.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]private static extern int HidP_GetCaps(nint data,out Caps caps);
 [DllImport("hid.dll",SetLastError=true)][DefaultDllImportSearchPaths(DllImportSearchPath.System32)][return:MarshalAs(UnmanagedType.I1)]private static extern bool HidD_GetFeature(SafeFileHandle handle,[In,Out]byte[] report,uint size);
 [DllImport("hid.dll",SetLastError=true)][DefaultDllImportSearchPaths(DllImportSearchPath.System32)][return:MarshalAs(UnmanagedType.I1)]private static extern bool HidD_SetOutputReport(SafeFileHandle handle,byte[] report,uint size);
 private readonly FileStream stream;private readonly int outputLength;
 private ControllerDualSenseHidReader(SafeFileHandle handle,Caps caps){stream=new FileStream(handle,FileAccess.ReadWrite,1,true);outputLength=caps.OutputLength;}
 private static bool Owned(SafeFileHandle handle,byte[] identity,ushort product,out Caps caps){
  caps=default;if(handle.IsInvalid)return false;var attributes=new Attributes{Size=(uint)Marshal.SizeOf<Attributes>()};if(!HidD_GetAttributes(handle,ref attributes)||attributes.Vendor!=0x054c||attributes.Product!=product||!HidD_GetPreparsedData(handle,out nint data))return false;
  try{if(HidP_GetCaps(data,out caps)!=0x110000||caps.InputLength!=64||caps.FeatureLength is <20 or >4096||caps.OutputLength is <48 or >4096)return false;}finally{HidD_FreePreparsedData(data);}
  byte[] feature=new byte[caps.FeatureLength];feature[0]=0x09;return HidD_GetFeature(handle,feature,(uint)feature.Length)&&feature.AsSpan(1,6).SequenceEqual(identity.Reverse().ToArray());
 }
 internal static async Task<ControllerDualSenseHidReader> OpenOwned(byte[] identity,ushort product){
  byte[] expected=identity;if(expected.Length!=6)throw new InvalidDataException("Invalid owned DualSense identity");string selector=HidDevice.GetDeviceSelector(1,5,0x054c,product);
  for(int n=0;n<120;n++){
   var entries=await DeviceInformation.FindAllAsync(selector);
   foreach(var entry in entries){
    // Query metadata with no write access. Do not open a physical controller for writes.
    using var query=CreateFileW(entry.Id,0,3,0,3,0,0);if(!Owned(query,expected,product,out _))continue;
    var handle=CreateFileW(entry.Id,0xc0000000,3,0,3,0x40000000,0);if(!Owned(handle,expected,product,out var caps)){handle.Dispose();continue;}
    return new(handle,caps);
   }
   await Task.Delay(50);
  }
  throw new IOException("Owned DualSense HID interface with matching feature nonce was not enumerated");
 }
 internal static async Task<bool> IsOwnedPresent(byte[] identity,ushort product){
  byte[] expected=identity;var entries=await DeviceInformation.FindAllAsync(HidDevice.GetDeviceSelector(1,5,0x054c,product));
  foreach(var entry in entries){using var query=CreateFileW(entry.Id,0,3,0,3,0,0);if(Owned(query,expected,product,out _))return true;}return false;
 }
 internal async Task<byte[]> ReadUntil(Func<byte[],bool> predicate,int timeoutMs=3000){
  using var stop=new CancellationTokenSource(timeoutMs);
  while(true){byte[] report=new byte[64];int count=await stream.ReadAsync(report,stop.Token);if(count==0)throw new IOException("Owned DualSense HID stream ended");if(count==64&&report[0]==1&&predicate(report))return report;}
 }
 internal void SendOutput(byte flags,byte small,byte large,byte red,byte green,byte blue){
  byte[] report=new byte[outputLength];report[0]=2;report[1]=3;report[2]=0x14;report[3]=small;report[4]=large;report[44]=3;report[45]=red;report[46]=green;report[47]=blue;
  if(!HidD_SetOutputReport(stream.SafeFileHandle,report,(uint)report.Length))throw new Win32Exception(Marshal.GetLastWin32Error(),"Owned DualSense output report rejected");
 }
 public void Dispose()=>stream.Dispose();
}
