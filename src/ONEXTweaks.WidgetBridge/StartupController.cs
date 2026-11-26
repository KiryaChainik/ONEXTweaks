// SPDX-License-Identifier: AGPL-3.0-only
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Xml.Linq;
namespace ONEXTweaks.WidgetBridge;

internal sealed record StartupState(bool Available,bool Enabled,string? Error);
internal static class StartupController
{
    internal const string TaskName="ONEXTweaks-Startup";
    internal static StartupState Read()=>Access(null);
    internal static StartupState Set(bool enabled)=>Access(enabled);
    private static StartupState Access(bool? enabled)
    {
        object? service=null,folder=null,task=null;
        try
        {
            service=Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service",true)!);
            ((dynamic)service!).Connect();folder=((dynamic)service).GetFolder(@"\");
            task=((dynamic)folder).GetTask(TaskName);
            using var identity=WindowsIdentity.GetCurrent();
            return ReadOrSetTask(task, identity.User!.Value, BridgeInstallationLayout.IsInstalled, enabled);
        }
        catch(Exception error){return new(false,false,error.Message);}
        finally{Release(task);Release(folder);Release(service);}
    }
    internal static StartupState ReadOrSetTask(dynamic task, string sid, bool installed, bool? enabled)
    {
        ValidateDefinition((string)task.Xml, sid, installed);
        if (enabled.HasValue) task.Enabled = enabled.Value;
        bool actual = (bool)task.Enabled;
        if (enabled.HasValue && actual != enabled.Value)
            throw new IOException("Windows did not confirm the startup setting");
        return new(true, actual, null);
    }

    private static void ValidateDefinition(string xml, string sid, bool installed)
    {
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        var root = XElement.Parse(xml);
        var principals = root.Element(ns + "Principals")?.Elements().ToArray() ?? [];
        var actions = root.Element(ns + "Actions")?.Elements().ToArray() ?? [];
        var triggers = root.Element(ns + "Triggers")?.Elements().ToArray() ?? [];
        if (root.Name != ns + "Task" || principals.Length != 1 || actions.Length != 1 || triggers.Length != 1)
            throw new InvalidDataException("Unexpected ONEXTweaks startup task");

        var principal = principals[0];
        var action = actions[0];
        var trigger = triggers[0];
        if (principal.Name != ns + "Principal" || !IsOwner(principal.Element(ns + "UserId")?.Value,sid) ||
            principal.Element(ns + "LogonType")?.Value != "InteractiveToken" ||
            principal.Element(ns + "RunLevel")?.Value is not (null or "LeastPrivilege"))
            throw new InvalidDataException("Unexpected ONEXTweaks startup principal");

        string launcher = installed ? BridgeInstallationLayout.InstalledLauncher : BridgeInstallationLayout.DevelopmentLauncher;
        string? arguments = action.Element(ns + "Arguments")?.Value;
        string? workingDirectory = action.Element(ns + "WorkingDirectory")?.Value;
        if (action.Name != ns + "Exec" || !BridgeInstallationLayout.SameImage(action.Element(ns + "Command")?.Value, launcher) ||
            (!string.IsNullOrEmpty(workingDirectory) && !string.Equals(workingDirectory, Path.GetDirectoryName(launcher), StringComparison.OrdinalIgnoreCase)) ||
            (arguments != "--startup" && (installed || !string.IsNullOrEmpty(arguments))))
            throw new InvalidDataException("Unexpected ONEXTweaks startup action");

        if (trigger.Name != ns + "LogonTrigger" || trigger.Element(ns + "Enabled")?.Value is "false" or "0" ||
            !IsOwner(trigger.Element(ns + "UserId")?.Value,sid))
            throw new InvalidDataException("Unexpected ONEXTweaks startup trigger");
    }
    private static bool IsOwner(string? value,string sid)
    {
        if(value==sid)return true;
        if(string.IsNullOrWhiteSpace(value)||value.StartsWith("S-1-",StringComparison.OrdinalIgnoreCase))return false;
        try{return new NTAccount(value).Translate(typeof(SecurityIdentifier)).Value==sid;}
        catch(IdentityNotMappedException){return false;}
    }
    private static void Release(object? value){if(value!=null&&Marshal.IsComObject(value))Marshal.ReleaseComObject(value);}
}
