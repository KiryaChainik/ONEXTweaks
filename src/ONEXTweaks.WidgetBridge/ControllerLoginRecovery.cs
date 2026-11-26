// SPDX-License-Identifier: AGPL-3.0-only
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Xml.Linq;
namespace ONEXTweaks.WidgetBridge;
// A short least-privilege cleanup at logon, independent of the user's widget startup preference.
internal static class ControllerLoginRecovery
{
 internal const string Name="ONEXTweaks-ControllerInputRecovery",Argument="--controller-login-recovery";
 internal static string Image=>BridgeInstallationLayout.DevelopmentImage;
 internal static string Runtime=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ONEXTweaks","toolchain","dotnet-10.0.403","dotnet.exe");
 internal static string Host=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe");
 private static string LegacyArgs(string image)=>"\""+Path.ChangeExtension(image,".dll")+"\" "+Argument;
 private static string Args(string image,string runtime,bool legacyWait=false)
 {
  string Q(string value)=>"'"+value.Replace("'","''")+"'";string script="$ErrorActionPreference='Stop';$env:DOTNET_ROOT="+Q(Path.GetDirectoryName(runtime)!)+";$p=Start-Process -FilePath "+Q(image)+" -ArgumentList "+Q(Argument)+" -WindowStyle Hidden "+(legacyWait?"-Wait -PassThru;exit $p.ExitCode":"-PassThru;$null=$p.Handle;$p.WaitForExit();exit $p.ExitCode");
  return "-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -EncodedCommand "+Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
 }
 internal static string Definition(string sid,string image,string runtime)=>TaskDefinition(sid,Host,Args(image,runtime));
 internal static string InstalledDefinition(string sid,string image)=>TaskDefinition(sid,image,Argument);
 private static string TaskDefinition(string sid,string command,string arguments)
 {
  XNamespace n="http://schemas.microsoft.com/windows/2004/02/mit/task";
  return new XElement(n+"Task",new XAttribute("version","1.2"),new XElement(n+"Triggers",new XElement(n+"LogonTrigger",new XElement(n+"Enabled",true),new XElement(n+"UserId",sid))),new XElement(n+"Principals",new XElement(n+"Principal",new XAttribute("id","Owner"),new XElement(n+"UserId",sid),new XElement(n+"LogonType","InteractiveToken"),new XElement(n+"RunLevel","LeastPrivilege"))),new XElement(n+"Settings",new XElement(n+"MultipleInstancesPolicy","IgnoreNew"),new XElement(n+"DisallowStartIfOnBatteries",false),new XElement(n+"StopIfGoingOnBatteries",false),new XElement(n+"StartWhenAvailable",true),new XElement(n+"Enabled",true),new XElement(n+"Hidden",true),new XElement(n+"ExecutionTimeLimit","PT1M")),new XElement(n+"Actions",new XAttribute("Context","Owner"),new XElement(n+"Exec",new XElement(n+"Command",command),new XElement(n+"Arguments",arguments)))).ToString();
 }
 internal static void Validate(string xml,string sid,string image,string runtime,bool legacy=false,bool legacyWait=false)=>
  ValidateTask(xml,sid,legacy?runtime:Host,legacy?LegacyArgs(image):Args(image,runtime,legacyWait));
 internal static void ValidateInstalled(string xml,string sid,string image)=>ValidateTask(xml,sid,image,Argument);
 private static void ValidateTask(string xml,string sid,string command,string arguments)
 {
  XNamespace n="http://schemas.microsoft.com/windows/2004/02/mit/task";var root=XElement.Parse(xml);var principals=root.Element(n+"Principals")?.Elements().ToArray()??[];var actions=root.Element(n+"Actions")?.Elements().ToArray()??[];var triggers=root.Element(n+"Triggers")?.Elements().ToArray()??[];
  if(principals.Length!=1||actions.Length!=1||triggers.Length!=1)throw new InvalidDataException("Unexpected controller recovery task");var p=principals[0];var a=actions[0];var t=triggers[0];
  bool Owner(string? value){if(value==sid)return true;try{return !string.IsNullOrWhiteSpace(value)&&new NTAccount(value).Translate(typeof(SecurityIdentifier)).Value==sid;}catch(IdentityNotMappedException){return false;}}
  if(!Owner(p.Element(n+"UserId")?.Value)||p.Element(n+"LogonType")?.Value!="InteractiveToken"||p.Element(n+"RunLevel")?.Value is not(null or "LeastPrivilege")||a.Name!=n+"Exec"||!BridgeInstallationLayout.SameImage(a.Element(n+"Command")?.Value,command)||a.Element(n+"Arguments")?.Value!=arguments||t.Name!=n+"LogonTrigger"||!Owner(t.Element(n+"UserId")?.Value)||
   (bool?)t.Element(n+"Enabled")==false||(bool?)root.Element(n+"Settings")?.Element(n+"Enabled")==false)throw new InvalidDataException("Controller recovery task belongs to another command or account, or is disabled");
 }
 private static void ValidatePreviousDevelopment(string xml,string sid)
 {
  try{Validate(xml,sid,Image,Runtime);}catch(InvalidDataException)
  {
   try{Validate(xml,sid,Image,Runtime,legacyWait:true);}catch(InvalidDataException){Validate(xml,sid,Image,Runtime,legacy:true);}
  }
 }
 private static void ConfirmRetargetSafe(int taskState)
 {
  if(taskState is 2 or 4||File.Exists(ControllerCaptureLease.LeaseFile(BridgeInstallationLayout.Reports)))
   throw new IOException("Recover the previous controller capture before moving its recovery task");
 }
 internal static void Ensure(bool run=false)
 {
  object? service=null,folder=null,task=null,started=null;try{
   bool installed=BridgeInstallationLayout.IsInstalled;
   string image=installed?BridgeInstallationLayout.InstalledImage:Image;
   if(!File.Exists(image)||(!installed&&!File.Exists(Runtime)))throw new IOException("The installed controller recovery image is missing");service=Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service",true)!);((dynamic)service!).Connect();folder=((dynamic)service).GetFolder(@"\");string sid=WindowsIdentity.GetCurrent().User!.Value;
   string definition=installed?InstalledDefinition(sid,image):Definition(sid,image,Runtime);
   void ValidateCurrent(string xml){if(installed)ValidateInstalled(xml,sid,image);else Validate(xml,sid,image,Runtime);}
   try{task=((dynamic)folder).GetTask(Name);}catch(Exception error)when(error.HResult==unchecked((int)0x80070002))
   {
    if(installed)ConfirmRetargetSafe(0);
    task=((dynamic)folder).RegisterTask(Name,definition,2,sid,null,3,null);
   }
   if((int)((dynamic)task!).Definition.Principal.RunLevel!=0)throw new InvalidDataException("Controller recovery requires least privilege");if(!(bool)((dynamic)task!).Enabled)throw new InvalidOperationException("Controller input recovery is disabled in Task Scheduler");
   string currentXml=(string)((dynamic)task).Xml;
   try{ValidateCurrent(currentXml);}catch(InvalidDataException)
   {
    ValidatePreviousDevelopment(currentXml,sid);
    if(installed)ConfirmRetargetSafe((int)((dynamic)task).State);
    object previous=task;task=((dynamic)folder).RegisterTask(Name,definition,4,sid,null,3,null);
    if(Marshal.IsComObject(previous))Marshal.ReleaseComObject(previous);
    ValidateCurrent((string)((dynamic)task).Xml);
   }
   if(run)started=((dynamic)task).Run(null);
  }finally{foreach(object? value in new[]{started,task,folder,service})if(value!=null&&Marshal.IsComObject(value))Marshal.ReleaseComObject(value);}
 }
 // Verification waits for an older IgnoreNew invocation to finish before starting its own run.
 internal static async Task<DateTime> RunForVerification()
 {
  Ensure();object? service=null,folder=null,task=null,started=null;
  try{
   service=Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service",true)!);((dynamic)service!).Connect();folder=((dynamic)service).GetFolder(@"\");task=((dynamic)folder).GetTask(Name);
   async Task Idle(){for(int n=0;n<250;n++){if((int)((dynamic)task!).State is not(2 or 4))return;await Task.Delay(100);}throw new IOException("Previous controller recovery task did not finish");}
   await Idle();DateTime stamp=DateTime.UtcNow;started=((dynamic)task).Run(null);await Task.Delay(200);await Idle();if((int)((dynamic)task).LastTaskResult!=0)throw new IOException("Controller recovery task returned a failure");return stamp;
  }finally{foreach(object? value in new[]{started,task,folder,service})if(value!=null&&Marshal.IsComObject(value))Marshal.ReleaseComObject(value);}
 }
 internal static async Task<int> Run(string reports)
 {
  Directory.CreateDirectory(reports);string result=await ControllerCaptureLease.RecoverAfterLogon(reports);string path=Path.Combine(reports,"controller-login-recovery.json"),temp=path+".tmp-"+Guid.NewGuid().ToString("N");await File.WriteAllTextAsync(temp,System.Text.Json.JsonSerializer.Serialize(new{utc=DateTimeOffset.UtcNow,result,normalHelperStarted=false,widgetStarted=false,preferencesWritten=false,cleanupProcessOnly=true},Backend.Program.Json));File.Move(temp,path,true);return 0;
 }
}
