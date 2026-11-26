// Dedicated disposable close target. AGPL-3.0-only.
using System;
using System.IO;
using System.Windows.Forms;
using System.Runtime.InteropServices;
internal sealed class CloseProbe : Form
{
    private readonly string report;
    private bool wmClose;
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    private CloseProbe(string path)
    {
        report=path; Text="ONEXTweaks close verification"; Width=480; Height=200;
        StartPosition=FormStartPosition.CenterScreen; TopMost=true;
        Controls.Add(new Label { Text="This disposable window verifies the overlay close action.", Dock=DockStyle.Fill, TextAlign=System.Drawing.ContentAlignment.MiddleCenter });
        Shown+=(s,e)=>{Activate(); SetForegroundWindow(Handle);};
        FormClosed+=(s,e)=>File.WriteAllText(report,"{\"closed\":true,\"wmClose\":"+(wmClose?"true":"false")+",\"reason\":\""+e.CloseReason+"\"}");
        var timer=new Timer { Interval=120000 }; timer.Tick+=(s,e)=>Close(); timer.Start();
    }
    protected override void WndProc(ref Message message) { if(message.Msg==0x10) wmClose=true; base.WndProc(ref message); }
    [STAThread] private static void Main(string[] args) { Application.EnableVisualStyles(); Application.Run(new CloseProbe(args[0])); }
}
