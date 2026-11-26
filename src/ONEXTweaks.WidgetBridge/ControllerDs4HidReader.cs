// SPDX-License-Identifier: AGPL-3.0-only
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Devices.Enumeration;
using Windows.Devices.HumanInterfaceDevice;
namespace ONEXTweaks.WidgetBridge;
/// <summary>Independent Windows HID transport for an endpoint proved by its per-instance feature nonce.</summary>
internal sealed class ControllerDs4HidReader:IDisposable
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
 private ControllerDs4HidReader(SafeFileHandle handle,Caps caps){stream=new FileStream(handle,FileAccess.ReadWrite,1,true);outputLength=caps.OutputLength;}
 private static bool Owned(SafeFileHandle handle,byte[] serial,out Caps caps){
  caps=default;if(handle.IsInvalid)return false;var attributes=new Attributes{Size=(uint)Marshal.SizeOf<Attributes>()};if(!HidD_GetAttributes(handle,ref attributes)||attributes.Vendor!=0x054c||attributes.Product!=0x09cc||!HidD_GetPreparsedData(handle,out nint data))return false;
  try{if(HidP_GetCaps(data,out caps)!=0x110000||caps.InputLength!=64||caps.FeatureLength is <16 or >4096||caps.OutputLength is <11 or >4096)return false;}finally{HidD_FreePreparsedData(data);}
  byte[] feature=new byte[caps.FeatureLength];feature[0]=0x12;return HidD_GetFeature(handle,feature,(uint)feature.Length)&&feature.AsSpan(8,8).SequenceEqual(serial);
 }
 internal static async Task<ControllerDs4HidReader> OpenOwned(string serial){
  byte[] expected=Convert.FromHexString(serial);if(expected.Length!=8)throw new InvalidDataException("Invalid owned DS4 identity");string selector=HidDevice.GetDeviceSelector(1,5,0x054c,0x09cc);
  for(int n=0;n<120;n++){
   var entries=await DeviceInformation.FindAllAsync(selector);
   foreach(var entry in entries){
    // Query metadata with no write access. Do not open a physical controller for writes.
    using var query=CreateFileW(entry.Id,0,3,0,3,0,0);if(!Owned(query,expected,out _))continue;
    var handle=CreateFileW(entry.Id,0xc0000000,3,0,3,0x40000000,0);if(!Owned(handle,expected,out var caps)){handle.Dispose();continue;}
    return new(handle,caps);
   }
   await Task.Delay(50);
  }
  throw new IOException("Owned DS4 HID interface with matching feature nonce was not enumerated");
 }
 internal static async Task<bool> IsOwnedPresent(string serial){
  byte[] expected=Convert.FromHexString(serial);var entries=await DeviceInformation.FindAllAsync(HidDevice.GetDeviceSelector(1,5,0x054c,0x09cc));
  foreach(var entry in entries){using var query=CreateFileW(entry.Id,0,3,0,3,0,0);if(Owned(query,expected,out _))return true;}return false;
 }
 internal async Task<byte[]> ReadUntil(Func<byte[],bool> predicate,int timeoutMs=3000){
  using var stop=new CancellationTokenSource(timeoutMs);
  while(true){byte[] report=new byte[64];int count=await stream.ReadAsync(report,stop.Token);if(count==0)throw new IOException("Owned DS4 HID stream ended");if(count==64&&report[0]==1&&predicate(report))return report;}
 }
 internal void SendOutput(byte flags,byte small,byte large,byte red,byte green,byte blue){
  byte[] report=new byte[outputLength];report[0]=5;report[1]=flags;report[4]=small;report[5]=large;report[6]=red;report[7]=green;report[8]=blue;
  if(!HidD_SetOutputReport(stream.SafeFileHandle,report,(uint)report.Length))throw new Win32Exception(Marshal.GetLastWin32Error(),"Owned DS4 output report rejected");
 }
 public void Dispose()=>stream.Dispose();
}
