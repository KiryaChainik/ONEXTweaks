using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class Setup
{
    internal const int Canceled = 1223;

    [DllImport("kernel32.dll")]
    private static extern uint GetOEMCP();

    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        if (!Environment.Is64BitOperatingSystem || Environment.OSVersion.Version.Build < 22000)
        {
            MessageBox.Show("ONEXTweaks Setup requires Windows 11 x64.", "ONEXTweaks Setup");
            return 1;
        }

        using (var identity = WindowsIdentity.GetCurrent())
        {
            var principal = new WindowsPrincipal(identity);
            bool elevated = principal.IsInRole(WindowsBuiltInRole.Administrator);
            if (args.Length == 0 && !elevated)
            {
                try
                {
                    var start = new ProcessStartInfo(Application.ExecutablePath, "--desktop " + identity.User.Value);
                    start.Verb = "runas";
                    start.UseShellExecute = true;
                    using (var process = Process.Start(start))
                    {
                        if (process == null) return 1;
                        process.WaitForExit();
                        return process.ExitCode;
                    }
                }
                catch (Win32Exception error)
                {
                    if (error.NativeErrorCode != Canceled) MessageBox.Show(error.Message, "ONEXTweaks Setup");
                    return error.NativeErrorCode == Canceled ? Canceled : 1;
                }
            }

            string desktop = args.Length == 0 ? identity.User.Value : args.Length == 2 && args[0] == "--desktop" ? args[1] : "";
            if (!elevated || !Regex.IsMatch(desktop, @"\AS-1-5-21(?:-[0-9]{1,10}){4}\z")) return 1;
            using (var window = new SetupWindow(desktop))
            {
                Application.Run(window);
                return window.ExitCode;
            }
        }
    }

    internal static int Run(string desktop, Action<string> progress, Action<string> error, Action<string> result, Func<string, bool> confirmTrust)
    {
        using (var mutex = new Mutex(false, @"Global\ONEXTweaks.Setup.Distribution.v1"))
        {
            bool acquired;
            try { acquired = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new InvalidOperationException("Another ONEXTweaks Setup is running.");
            try
            {
                progress("Preparing the installation files...");
                string folder = CreateProtectedPayload(desktop);
                try
                {
                    if (!String.IsNullOrEmpty(PayloadInfo.BetaCertificateSha256) && !TrustBetaCertificate(folder, confirmTrust))
                    {
                        result("Setup canceled. Windows trust was not changed.");
                        return Canceled;
                    }
                    string shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
                    string script = Path.Combine(folder, "Invoke-ONEXTweaksSetup.ps1");
                    var start = new ProcessStartInfo(shell, "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -File \"" + script + "\" -OwnerSid " + desktop);
                    start.WorkingDirectory = folder;
                    start.UseShellExecute = false;
                    start.CreateNoWindow = true;
                    start.RedirectStandardOutput = true;
                    start.RedirectStandardError = true;
                    start.StandardOutputEncoding = Encoding.UTF8;
                    start.StandardErrorEncoding = Encoding.GetEncoding((int)GetOEMCP());
                    var errorLock = new object();
                    Action<string> reportError = line => { lock (errorLock) error(line); };
                    using (var process = new Process { StartInfo = start })
                    {
                        process.OutputDataReceived += (sender, args) =>
                        {
                            if (args.Data == null) return;
                            if (args.Data.StartsWith("STEP ", StringComparison.Ordinal)) progress(args.Data.Substring(5));
                            if (args.Data.StartsWith("RESULT ", StringComparison.Ordinal)) result(args.Data.Substring(7));
                            if (args.Data.StartsWith("ERROR ", StringComparison.Ordinal)) reportError(args.Data.Substring(6));
                        };
                        process.ErrorDataReceived += (sender, args) => { if (args.Data != null) reportError(args.Data); };
                        process.Start();
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();
                        process.WaitForExit();
                        return process.ExitCode;
                    }
                }
                finally { RemovePayload(folder); }
            }
            finally { mutex.ReleaseMutex(); }
        }
    }

    private static bool TrustBetaCertificate(string folder, Func<string, bool> confirmTrust)
    {
        var signature = new SignedCms();
        using (var widget = File.OpenRead(Path.Combine(folder, PayloadInfo.WidgetFile)))
        using (var package = new ZipArchive(widget, ZipArchiveMode.Read))
        {
            var entry = package.GetEntry("AppxSignature.p7x");
            if (entry == null || entry.Length <= 4 || entry.Length > 128 * 1024)
                throw new InvalidDataException("The beta widget signature is missing or invalid.");
            using (var reader = new BinaryReader(entry.Open()))
            {
                if (!reader.ReadBytes(4).SequenceEqual(new byte[] { 0x50, 0x4B, 0x43, 0x58 })) // PKCX
                    throw new InvalidDataException("The beta widget signature is invalid.");
                signature.Decode(reader.ReadBytes((int)entry.Length - 4));
            }
        }

        if (signature.SignerInfos.Count != 1)
            throw new InvalidDataException("The beta widget must have one signing certificate.");
        using (var certificate = signature.SignerInfos[0].Certificate)
        using (var sha = SHA256.Create())
        {
            string fingerprint = certificate == null ? "" : BitConverter.ToString(sha.ComputeHash(certificate.RawData)).Replace("-", "");
            if (!String.Equals(fingerprint, PayloadInfo.BetaCertificateSha256, StringComparison.Ordinal))
                throw new InvalidDataException("The beta widget signing certificate does not match this Setup.");
            signature.CheckSignature(true);

            using (var store = new X509Store(StoreName.TrustedPeople, StoreLocation.LocalMachine))
            {
                store.Open(OpenFlags.ReadOnly);
                if (store.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, false)
                    .Cast<X509Certificate2>().Any(existing => existing.RawData.SequenceEqual(certificate.RawData)))
                    return true;
                string message = "Trust this ONEXTweaks signing certificate on this PC?\n\n" +
                    "Windows will use it to verify this Game Bar widget and future packages signed with the same certificate. " +
                    "Setup will add the certificate to Trusted People.\n\n" +
                    "Publisher: " + certificate.GetNameInfo(X509NameType.SimpleName, false) + "\n" +
                    "SHA-256: " + fingerprint.Substring(0, 32) + "\n                 " + fingerprint.Substring(32);
                if (!confirmTrust(message)) return false;
                store.Close();
                store.Open(OpenFlags.ReadWrite);
                store.Add(certificate);
            }
        }
        return true;
    }

    private static string CreateProtectedPayload(string desktop)
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ONEXTweaks-Setup-" + Guid.NewGuid().ToString("N"));
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        security.SetOwner(admins);
        Grant(security, admins, FileSystemRights.FullControl);
        Grant(security, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl);
        Grant(security, new SecurityIdentifier(desktop), FileSystemRights.ReadAndExecute);
        Directory.CreateDirectory(folder, security);
        try
        {
            using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("ONEXTweaks.Setup.Payload"))
            using (var sha = SHA256.Create())
            {
                if (source == null || !String.Equals(BitConverter.ToString(sha.ComputeHash(source)).Replace("-", ""), PayloadInfo.Sha256, StringComparison.Ordinal))
                    throw new InvalidDataException("The installation payload is missing or damaged.");
                source.Position = 0;
                using (var archive = new ZipArchive(source, ZipArchiveMode.Read))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        foreach (string part in entry.FullName.Split('/'))
                        {
                            if (!Regex.IsMatch(part, @"\A[A-Za-z0-9_ .-]+\z") || part == "." || part == ".." || part != part.Trim() || part.EndsWith(".", StringComparison.Ordinal))
                                throw new InvalidDataException("Invalid installation payload path.");
                        }
                        string target = Path.Combine(folder, entry.FullName.Replace('/', '\\'));
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        using (Stream input = entry.Open())
                        using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None)) input.CopyTo(output);
                    }
                }
            }
            return folder;
        }
        catch
        {
            RemovePayload(folder);
            throw;
        }
    }

    private static void Grant(DirectorySecurity security, SecurityIdentifier account, FileSystemRights rights)
    {
        security.AddAccessRule(new FileSystemAccessRule(account, rights, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
    }

    private static void RemovePayload(string folder)
    {
        try { Directory.Delete(folder, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

internal sealed class SetupWindow : Form
{
    private readonly Label status;
    private readonly ProgressBar progress;
    private readonly Button install;
    private readonly Button close;
    private bool running;
    internal int ExitCode { get; private set; }

    internal SetupWindow(string desktop)
    {
        ExitCode = Setup.Canceled;
        Text = "ONEXTweaks Setup";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Font;
        Font = SystemFonts.MessageBoxFont;
        ClientSize = new Size(470, 207);
        var heading = new Label { Text = "Install ONEXTweaks", Font = new Font(Font, FontStyle.Bold), AutoSize = true, Location = new Point(20, 19) };
        status = new Label { Text = "Set up the Xbox Game Bar widget and the components it needs.", Bounds = new Rectangle(20, 49, 430, 65) };
        progress = new ProgressBar { Bounds = new Rectangle(20, 121, 428, 11), Visible = false };
        close = new Button { Text = "Close", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(350, 154, 98, 31) };
        close.Click += (sender, args) => Close();
        install = new Button { Text = "Install", Bounds = new Rectangle(244, 154, 98, 31) };
        install.Click += async (sender, args) => await Install(desktop);
        FormClosing += (sender, args) => { if (running) args.Cancel = true; };
        AcceptButton = install;
        CancelButton = close;
        Controls.AddRange(new Control[] { heading, status, progress, install, close });
    }

    private async Task Install(string desktop)
    {
        running = true;
        install.Enabled = false;
        close.Enabled = false;
        progress.Visible = true;
        progress.Style = ProgressBarStyle.Marquee;
        string final = "";
        try
        {
            int exit = await Task.Run(() => Setup.Run(desktop, message => BeginInvoke(new Action(() => status.Text = message)),
                line => System.Diagnostics.Trace.WriteLine(line), line => final = line,
                message => (bool)Invoke(new Func<bool>(() => MessageBox.Show(this, message, "ONEXTweaks signing certificate",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes))));
            ExitCode = exit;
            if (String.IsNullOrEmpty(final)) final = "Setup exited with code " + exit + ".";
            status.Text = final;
            if (exit == Setup.Canceled) install.Enabled = true;
            if (exit != 0 && exit != 3010 && exit != Setup.Canceled)
            {
                MessageBox.Show(this, final, "ONEXTweaks Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        catch (Exception error) { ExitCode = 1; status.Text = error.Message; }
        finally
        {
            running = false;
            progress.Visible = false;
            close.Enabled = true;
            close.Focus();
        }
    }
}
