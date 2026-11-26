// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
namespace ONEXTweaks.CompanionLauncher
{
    internal static class CompanionBootstrap
    {
        internal static readonly string[] Required={"ONEXTweaks-PowerAgent","ONEXTweaks-WidgetBridge"};
        internal static Dictionary<string,string> Start(Func<string,bool> running,Action<string> start, bool hardwareAgentInstalled = true)
        {
            var states=new Dictionary<string,string>();
            foreach(string name in Required)
            {
                if (!hardwareAgentInstalled && name == "ONEXTweaks-PowerAgent") continue;
                try{if(running(name))states[name]="already running";else{start(name);states[name]="start requested";}}
                catch(Exception error){states[name]="error: "+error.GetType().Name;}
            }
            return states;
        }
    }
}
