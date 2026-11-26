// SPDX-License-Identifier: AGPL-3.0-only
using System.Runtime.InteropServices;
using System.Text;
namespace ONEXTweaks.WidgetBridge;
internal static class ControllerInputDesktop
{
 internal static bool Available()
 {
  nint desktop=OpenInputDesktop(0,false,1);if(desktop==0)return false;
  try{var name=new StringBuilder(256);return GetUserObjectInformation(desktop,2,name,name.Capacity*2,out _)&&name.ToString().Equals("Default",StringComparison.OrdinalIgnoreCase);}
  finally{CloseDesktop(desktop);}
 }
 [DllImport("user32.dll",SetLastError=true)]private static extern nint OpenInputDesktop(uint flags,[MarshalAs(UnmanagedType.Bool)]bool inherit,uint access);
 [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool GetUserObjectInformation(nint value,int index,StringBuilder name,int bytes,out uint needed);
 [DllImport("user32.dll")]private static extern bool CloseDesktop(nint desktop);
}
