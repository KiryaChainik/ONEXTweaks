// SPDX-License-Identifier: AGPL-3.0-only
using System.Diagnostics;
using System.Text.Json;

namespace ONEXTweaks.WidgetBridge;

internal sealed record PowerShellPolicySnapshot(string Effective, string MachinePolicy, string UserPolicy,
    string Process, string CurrentUser, string LocalMachine);

// The launcher's policy choice is confined to the new child. Never modify registry,
// GPO, the parent environment, or any explicitly configured policy scope.
internal static class PowerShellScriptLaunch
{
    internal const int PolicyQueryTimeoutMilliseconds = 2500;
    internal const int PolicyQueryCharacterLimit = 4096;
    internal const string PolicyQueryCommand =
        "$ErrorActionPreference='Stop';$s=@{};foreach($n in @('MachinePolicy','UserPolicy','Process','CurrentUser','LocalMachine')){$s[$n]=(Microsoft.PowerShell.Security\\Get-ExecutionPolicy -Scope $n).ToString()};@{effective=(Microsoft.PowerShell.Security\\Get-ExecutionPolicy).ToString();scopes=$s}|ConvertTo-Json -Depth 4 -Compress";

    internal static bool UseRemoteSigned(PowerShellPolicySnapshot? value, bool processPreferenceConfigured) =>
        !processPreferenceConfigured && value is
        { Effective: "Restricted", MachinePolicy: "Undefined", UserPolicy: "Undefined", Process: "Undefined",
            CurrentUser: "Undefined", LocalMachine: "Undefined" };

    internal static PowerShellPolicySnapshot? ParseSnapshot(string json)
    {
        if (json.Length > PolicyQueryCharacterLimit) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("effective", out var effective) ||
                effective.ValueKind != JsonValueKind.String || !root.TryGetProperty("scopes", out var scopes) ||
                scopes.ValueKind != JsonValueKind.Object || scopes.EnumerateObject().Count() != 5) return null;
            string? Value(string key) => scopes.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
            string? machine = Value("MachinePolicy"), user = Value("UserPolicy"), process = Value("Process"), current = Value("CurrentUser"), local = Value("LocalMachine");
            string? active = effective.GetString();
            bool Known(string? text) => text is "Undefined" or "Restricted" or "AllSigned" or "RemoteSigned" or "Unrestricted" or "Bypass" or "Default";
            return Known(active) && Known(machine) && Known(user) && Known(process) && Known(current) && Known(local)
                ? new(active!, machine!, user!, process!, current!, local!) : null;
        }
        catch (JsonException) { return null; }
    }

    internal static ProcessStartInfo ScriptStartInfo(string powershell, string script, bool remoteSigned)
    {
        var result = new ProcessStartInfo(powershell) { UseShellExecute = false };
        result.ArgumentList.Add("-NoProfile");
        if (remoteSigned) { result.ArgumentList.Add("-ExecutionPolicy"); result.ArgumentList.Add("RemoteSigned"); }
        result.ArgumentList.Add("-File"); result.ArgumentList.Add(script);
        return result;
    }

    internal static ProcessStartInfo PolicyQueryStartInfo(string powershell)
    {
        var result = new ProcessStartInfo(powershell)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string value in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", PolicyQueryCommand }) result.ArgumentList.Add(value);
        return result;
    }

    internal static async Task<ProcessStartInfo> CreateAsync(string script)
    {
        string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
        bool configured = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PSExecutionPolicyPreference"));
        var snapshot = configured ? null : await ReadPolicyAsync(powershell);
        return ScriptStartInfo(powershell, script, UseRemoteSigned(snapshot, configured));
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellation)
    {
        var text = new System.Text.StringBuilder(); var buffer = new char[256];
        while (true)
        {
            int count = await reader.ReadAsync(buffer.AsMemory(), cancellation);
            if (count == 0) return text.ToString();
            if (text.Length + count > PolicyQueryCharacterLimit) throw new IOException("Own policy query output limit exceeded");
            text.Append(buffer, 0, count);
        }
    }

    private static async Task<PowerShellPolicySnapshot?> ReadPolicyAsync(string powershell)
    {
        // Only this constant, read-only policy query is owned by this method.
        // The selected user script is started later by the existing DesktopActions path.
        using var query = new Process { StartInfo = PolicyQueryStartInfo(powershell) };
        try
        {
            if (!query.Start()) return null;
            using var cancellation = new CancellationTokenSource(PolicyQueryTimeoutMilliseconds);
            var output = ReadBoundedAsync(query.StandardOutput, cancellation.Token);
            var error = ReadBoundedAsync(query.StandardError, cancellation.Token);
            await Task.WhenAll(output, error, query.WaitForExitAsync(cancellation.Token));
            if (query.ExitCode != 0 || error.Result.Length != 0) return null;
            return ParseSnapshot(output.Result);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or
            System.ComponentModel.Win32Exception or OperationCanceledException or UnauthorizedAccessException)
        {
            try { if (!query.HasExited) { query.Kill(entireProcessTree: false); await query.WaitForExitAsync(); } }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            return null;
        }
    }
}
