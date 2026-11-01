using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace THSSS.ConfigLauncher;

internal static class Program
{
    private static int Main(string[] args)
    {
        string root = AppContext.BaseDirectory;
        string executable = Path.Combine(root, "ConfigApp", OperatingSystem.IsWindows() ? "Config.exe" : "Config");
        if (!File.Exists(executable))
        {
            Console.Error.WriteLine($"Config launcher: missing configuration application: {executable}. Restore the complete package, including ConfigApp.");
            return 1;
        }

        string? inheritedDataRoot = Environment.GetEnvironmentVariable("THSSS_DATA_ROOT");
        string dataRoot = string.IsNullOrEmpty(inheritedDataRoot) ? root : Path.GetFullPath(inheritedDataRoot);
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = dataRoot };
        foreach (string argument in args)
            start.ArgumentList.Add(argument);
        // Resolve relative paths once, before changing the child working directory.
        start.Environment["THSSS_DATA_ROOT"] = dataRoot;

        try
        {
            using Process child = Process.Start(start) ?? throw new InvalidOperationException("Process.Start returned no child process.");
            child.WaitForExit();
            return child.ExitCode;
        }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Config launcher: cannot run {executable}: {error.Message}");
            return 1;
        }
    }
}
