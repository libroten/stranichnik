using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Stranichnik.Diagnostics;

namespace Stranichnik.Sync.Credentials;

public sealed class LinuxSecretToolSyncCredentialStore : ISystemSyncCredentialStore
{
    private const string ServiceAttribute = "service";
    private const string ServiceAttributeValue = "Stranichnik.WebDAV";
    private const string UsernameAttribute = "username";
    private readonly Action<string> _log;

    public LinuxSecretToolSyncCredentialStore(Action<string>? log = null)
    {
        _log = log ?? Logs.Print;
    }

    public SyncCredentials? Load(string username)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        if (!OperatingSystem.IsLinux())
            return null;

        var result = RunSecretTool(
            ["lookup", ServiceAttribute, ServiceAttributeValue, UsernameAttribute, username],
            passwordInput: null);
        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.Output))
            return null;

        _log("Sync credentials loaded from Linux Secret Service.");
        return new SyncCredentials(result.Output.TrimEnd('\r', '\n'));
    }

    public bool Save(string username, SyncCredentials credentials)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(credentials);

        if (!OperatingSystem.IsLinux())
            return false;

        var result = RunSecretTool(
            ["store", "--label=Stranichnik WebDAV", ServiceAttribute, ServiceAttributeValue, UsernameAttribute, username],
            credentials.Password);
        return result.Succeeded;
    }

    public void Delete(string username)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        if (!OperatingSystem.IsLinux())
            return;

        RunSecretTool(
            ["clear", ServiceAttribute, ServiceAttributeValue, UsernameAttribute, username],
            passwordInput: null);
    }

    private SecretToolResult RunSecretTool(string[] arguments, string? passwordInput)
    {
        try
        {
            using var process = new Process();
            process.StartInfo.FileName = "secret-tool";
            foreach (var argument in arguments)
                process.StartInfo.ArgumentList.Add(argument);
            process.StartInfo.RedirectStandardInput = passwordInput is not null;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
            process.StartInfo.UseShellExecute = false;

            if (!process.Start())
                return new SecretToolResult(false, string.Empty);

            if (passwordInput is not null)
            {
                process.StandardInput.Write(passwordInput);
                process.StandardInput.Close();
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(milliseconds: 5000);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                _log("Linux Secret Service credential operation timed out.");
                return new SecretToolResult(false, string.Empty);
            }

            return new SecretToolResult(process.ExitCode == 0, output);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            _log("Linux Secret Service credential operation is unavailable.");
            return new SecretToolResult(false, string.Empty);
        }
    }

    private sealed record SecretToolResult(bool Succeeded, string Output);
}
