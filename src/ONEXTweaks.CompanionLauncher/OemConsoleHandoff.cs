// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
namespace ONEXTweaks.CompanionLauncher
{
    // The owner authorized closing the stock GUI when ONEXTweaks takes control.
    // Match its installed path and this session; never stop the OEM service/driver.
    internal static class OemConsoleHandoff
    {
        internal static int Close()
        {
            string expected=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"OneXConsole","OneXConsole.exe");
            int session;using(var self=Process.GetCurrentProcess())session=self.SessionId;
            int count=0;
            foreach(var process in Process.GetProcessesByName("OneXConsole"))
            using(process)
            {
                try
                {
                    if(process.SessionId!=session||!string.Equals(process.MainModule?.FileName,expected,StringComparison.OrdinalIgnoreCase))continue;
                    if(process.CloseMainWindow())process.WaitForExit(500);
                    if(!process.HasExited){process.Kill();process.WaitForExit(2000);}count++;
                }
                catch(InvalidOperationException){}
                catch(Win32Exception){}
            }
            return count;
        }
    }
}
