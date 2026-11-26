// SPDX-License-Identifier: AGPL-3.0-only
namespace ONEXTweaks.WidgetBridge;

internal enum ControllerDiagnosticReturnAction { Wait, Open, Cancel, AlreadyOpen }

// Closing our foreground test permits returning to the window it displaced.
// Alt+Tab or an independently opened overlay permanently cancels that request.
internal sealed class ControllerDiagnosticReturnGate(nint origin,nint host)
{
    private bool cancelled;
    internal ControllerDiagnosticReturnAction Observe(bool exited,bool overlay,nint foreground)
    {
        if(cancelled)return ControllerDiagnosticReturnAction.Cancel;
        if(overlay){cancelled=true;return ControllerDiagnosticReturnAction.AlreadyOpen;}
        if(origin==0||foreground!=0&&foreground!=host&&foreground!=origin)
        {cancelled=true;return ControllerDiagnosticReturnAction.Cancel;}
        return exited&&foreground==origin?ControllerDiagnosticReturnAction.Open:ControllerDiagnosticReturnAction.Wait;
    }
}
