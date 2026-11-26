// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;

[assembly: AssemblyVersion("0.29.422.0")]
[assembly: AssemblyFileVersion("0.29.422.0")]

namespace ONEXTweaks.BackgroundHost
{
    // Compile as WinExe: hiding an already-created console can still flash.
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length != 0) return 2;
            using (var gate = new Mutex(false, @"Local\ONEXTweaks.BridgeBackgroundHost.v1"))
            {
                bool owned = false;
                try
                {
                    try { owned = gate.WaitOne(0); }
                    catch (AbandonedMutexException) { owned = true; }
                    if (!owned) return 0;
                    string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ONEXTweaks");
                    string image = Path.Combine(root, @"dev\project\src\ONEXTweaks.WidgetBridge\bin\Release\net10.0-windows\ONEXTweaks.WidgetBridge.exe");
                    string runtime = Path.Combine(root, @"toolchain\dotnet-10.0.403");
                    if (!File.Exists(image) || !File.Exists(Path.Combine(runtime, "dotnet.exe"))) return 3;
                    var start = new ProcessStartInfo(image);
                    start.UseShellExecute = false;
                    start.CreateNoWindow = true;
                    start.WindowStyle = ProcessWindowStyle.Hidden;
                    start.EnvironmentVariables["DOTNET_ROOT"] = runtime;
                    start.EnvironmentVariables["DOTNET_ROOT_X64"] = runtime;
                    using (var child = Process.Start(start))
                    {
                        if (child == null) return 4;
                        child.WaitForExit();
                        return child.ExitCode;
                    }
                }
                catch { return 1; }
                finally { if (owned) gate.ReleaseMutex(); }
            }
        }
    }
}
