// SPDX-License-Identifier: AGPL-3.0-only
// HidHide's published control-device ABI: https://docs.nefarius.at/projects/HidHide/API-Documentation/
// No registry manipulation; existing raw NT application paths are preserved verbatim.
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
namespace ONEXTweaks.WidgetBridge;
internal sealed record ControllerHideState(bool Active,bool Inverse,string[] Devices,string[] Applications);
internal sealed class ControllerHidHide:IDisposable
{
 private readonly SafeFileHandle handle;
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern SafeFileHandle CreateFileW(string path,uint access,uint share,nint security,uint create,uint flags,nint template);
 [DllImport("kernel32.dll",SetLastError=true)]private static extern bool DeviceIoControl(SafeFileHandle file,uint code,byte[]? input,uint inputSize,byte[]? output,uint outputSize,out uint returned,nint overlapped);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern uint QueryDosDeviceW(string device,StringBuilder text,int length);
 internal ControllerHidHide(){handle=CreateFileW(@"\\.\HidHide",0xc0000000,3,0,3,0,0);if(handle.IsInvalid){int error=Marshal.GetLastWin32Error();handle.Dispose();throw new Win32Exception(error,"HidHide is unavailable. Install its signed driver and reconnect the controller if requested.");}}
 private static uint Code(uint function)=>(32769u<<16)|(1u<<14)|(function<<2);
 private byte[] Get(uint function,bool flag=false)
 {
  uint size=flag?1u:0u;if(!flag&&!DeviceIoControl(handle,Code(function),null,0,null,0,out size,0))throw new Win32Exception(Marshal.GetLastWin32Error(),"HidHide size query failed");
  if(size>32768)throw new InvalidDataException("HidHide returned an excessive list");var value=new byte[Math.Max(size,flag?1u:4u)];if(!DeviceIoControl(handle,Code(function),null,0,value,(uint)value.Length,out _,0))throw new Win32Exception(Marshal.GetLastWin32Error(),"HidHide read failed");return value;
 }
 private void Set(uint function,byte[] value){if(!DeviceIoControl(handle,Code(function),value,(uint)value.Length,null,0,out _,0))throw new Win32Exception(Marshal.GetLastWin32Error(),"HidHide update failed");}
 private static string[] Strings(byte[] value)=>Encoding.Unicode.GetString(value).Split('\0',StringSplitOptions.RemoveEmptyEntries);
 private static byte[] Multi(IEnumerable<string> values){string[] unique=values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();if(unique.Any(s=>s.Contains('\0')||s.Length>2048))throw new InvalidDataException("Invalid HidHide path");var data=Encoding.Unicode.GetBytes(string.Join('\0',unique)+"\0\0");if(data.Length>32768)throw new InvalidDataException("HidHide list is too long");return data;}
 internal ControllerHideState Read()=>new(Get(2052,true)[0]!=0,Get(2054,true)[0]!=0,Strings(Get(2050)),Strings(Get(2048)));
 internal void Devices(IEnumerable<string> values)=>Set(2051,Multi(values));internal void Applications(IEnumerable<string> values)=>Set(2049,Multi(values));internal void Active(bool value)=>Set(2053,[value?(byte)1:(byte)0]);
 internal static string ImagePath(string path){path=Path.GetFullPath(path);string drive=Path.GetPathRoot(path)??"";if(drive.Length!=3||drive[1]!=':')throw new InvalidDataException("The remapper must run from a local volume");var buffer=new StringBuilder(32768);if(QueryDosDeviceW(drive[..2],buffer,buffer.Capacity)==0)throw new Win32Exception(Marshal.GetLastWin32Error());return buffer.ToString().Split('\0')[0]+path[2..];}
 public void Dispose()=>handle.Dispose();
}
