// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using System.Security.Principal;
namespace ONEXTweaks.WidgetBridge;
internal static class StartupVerification
{
    internal static async Task<int> Run(string report)
    {
        var initial=StartupController.Read();bool passed=false,off=false,on=false,restored=false;string? error=null;
        try
        {
            if(!initial.Available)throw new IOException(initial.Error);
            var disabled=StartupController.Set(false);off=disabled.Available&&!disabled.Enabled;if(!off)throw new IOException(disabled.Error??"Startup did not disable");
            var enabled=StartupController.Set(true);on=enabled.Available&&enabled.Enabled;if(!on)throw new IOException(enabled.Error??"Startup did not enable");
            passed=true;
        }
        catch(Exception e){error=e.Message;}
        finally{if(initial.Available){var actual=StartupController.Set(initial.Enabled);restored=actual.Available&&actual.Enabled==initial.Enabled;passed&=restored;}}
        bool elevated=new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        await File.WriteAllTextAsync(report,JsonSerializer.Serialize(new{passed,initial.Enabled,off,on,restored,elevated,hardwareWrites=0,error},Backend.Program.Json));return passed?0:1;
    }
}
