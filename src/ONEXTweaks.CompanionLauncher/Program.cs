// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
namespace ONEXTweaks.CompanionLauncher
{
    internal static class Program
    {
        [STAThread] private static int Main(string[] args)
        {
            if(Array.IndexOf(args,"--restart-widget")>=0)return RestartWidget();
            // Named activation for dependency UI is independent of the normal companion/startup route.
            if(Array.IndexOf(args,"--driver-manager")>=0)
            {
                try
                {
                    string file=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ONEXTweaks.DependencyManager.exe");
                    using(var child=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file){UseShellExecute=false,CreateNoWindow=true}))return child==null?1:0;
                }
                catch{return 1;}
            }
            using(var gate=new Mutex(false,@"Local\ONEXTweaks.StartCompanions.v1"))
            {
                bool owned=false;object service=null,folder=null;
                try
                {
                    try{owned=gate.WaitOne(TimeSpan.FromSeconds(10));}catch(AbandonedMutexException){owned=true;}
                    if(!owned)return 0;
#if !ONEXTWEAKS_INSTALLED_DISTRIBUTION
                    OemConsoleHandoff.Close();
#endif
                    var type=Type.GetTypeFromProgID("Schedule.Service",true);service=Activator.CreateInstance(type);((dynamic)service).Connect();folder=((dynamic)service).GetFolder(@"\");
                    bool hardwareAgentInstalled = true;
#if ONEXTWEAKS_INSTALLED_DISTRIBUTION
                    hardwareAgentInstalled = File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PowerAgent", "ONEXTweaks.PowerAgent.exe"));
#endif
                    var states=CompanionBootstrap.Start(name=>WithTask(folder,name,task=>(int)task.State==4),name=>WithTask(folder,name,task=>{if(!(bool)task.Enabled)throw new InvalidOperationException("Companion task disabled");object instance=task.Run(null);Release(instance);return true;}), hardwareAgentInstalled);
                    string reports=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ONEXTweaks","dev","Reports");Directory.CreateDirectory(reports);
                    using(var text=new StreamWriter(Path.Combine(reports,"companion-startup.txt"),false))
                    {text.WriteLine(DateTimeOffset.UtcNow.ToString("o"));foreach(var pair in states)text.WriteLine(pair.Key+": "+pair.Value);}
                    foreach(var value in states.Values)if(value.StartsWith("error:",StringComparison.Ordinal))return 1;
                    // The bridge owns background prewarm, direct activation and Guide. No extra
                    // Game Bar URI here: it would race its activation queue and expose Home.
                    return 0;
                }
                catch{return 1;}
                finally{Release(folder);Release(service);if(owned)gate.ReleaseMutex();}
            }
        }
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]
        private static extern int GetCurrentPackageFamilyName(ref int length,System.Text.StringBuilder family);
        private static int RestartWidget()
        {
            // This route never touches companion tasks, controller drivers, games,
            // Windows power state or other apps. Wait only for the original UI PIDs.
            using(var gate=new Mutex(false,@"Local\ONEXTweaks.RestartWidget.v1"))
            {
                bool owned=false;
                try
                {
                    try{owned=gate.WaitOne(0);}catch(AbandonedMutexException){owned=true;}
                    if(!owned)return 0;
                    int length=0;GetCurrentPackageFamilyName(ref length,null);
                    if(length<2)return 1;
                    var family=new System.Text.StringBuilder(length);
                    if(GetCurrentPackageFamilyName(ref length,family)!=0)return 1;
                    string reports=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Packages",family.ToString(),"LocalState","Reports");
                    string[] ticket=File.ReadAllLines(Path.Combine(reports,"language-restart.txt"));
                    int pid;long ticks;Guid nonce;
                    if(ticket.Length!=3||!int.TryParse(ticket[0],out pid)||!long.TryParse(ticket[1],out ticks)||!Guid.TryParseExact(ticket[2],"N",out nonce))return 1;
                    var age=DateTime.UtcNow-new DateTime(ticks,DateTimeKind.Utc);
                    if(age.TotalSeconds<0||age.TotalSeconds>15)return 1;
                    string root=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,".."))+Path.DirectorySeparatorChar;
                    using(var original=System.Diagnostics.Process.GetProcessById(pid))
                    {
                        if(!original.MainModule.FileName.StartsWith(root,StringComparison.OrdinalIgnoreCase))return 1;
                        File.WriteAllText(Path.Combine(reports,"language-restart-ready.txt"),nonce.ToString("N"));
                        if(!original.WaitForExit(10000))return 1;
                    }
                    string uri="ms-gamebar://launchforeground/activate/"+family+"_App_GamingWidget";
                    using(var child=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri){UseShellExecute=true})){}
                    return 0;
                }
                catch{return 1;}
                finally{if(owned)gate.ReleaseMutex();}
            }
        }
        private static T WithTask<T>(object folder,string name,Func<dynamic,T> action)
        {object task=null;try{task=((dynamic)folder).GetTask(name);return action(task);}finally{Release(task);}}
        private static void Release(object value){if(value!=null&&Marshal.IsComObject(value))Marshal.ReleaseComObject(value);}
    }
}
