using System;
using System.Globalization;
using System.IO;
using Stranichnik.Settings;

namespace Stranichnik.Diagnostics;

public static class Logs
{
    private static readonly object SyncRoot = new();
    private static bool _printToConsole;
    private static bool _isConfigured;

    public static void Configure(bool printToConsole)
    {
        lock (SyncRoot)
        {
            _printToConsole = printToConsole;
            _isConfigured = true;

            try
            {
                Directory.CreateDirectory(AppDataPaths.AppDataDirectory);
                File.WriteAllText(AppDataPaths.LogPath, string.Empty);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            WriteLineCore("Log started.");
        }
    }

    public static void Print(string message)
    {
        lock (SyncRoot)
        {
            if (!_isConfigured)
                Configure(printToConsole: false);

            WriteLineCore(message);
        }
    }

    private static void WriteLineCore(string message)
    {
        var line = $"{DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture)} {message}";

        try
        {
            File.AppendAllText(AppDataPaths.LogPath, line + Environment.NewLine);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        if (!_printToConsole)
            return;

        try
        {
            Console.WriteLine(line);
        }
        catch (IOException)
        {
        }
    }
}
