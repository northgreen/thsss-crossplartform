using System;
using System.Diagnostics;
using System.IO;

namespace THSSS.Config;

/// <summary>
/// Locates the data directory (the one holding Setting.INI) and launches the game
/// next to this executable, mirroring the original "应用并启动" behaviour of starting
/// THSSS.exe from the working directory.
/// </summary>
internal sealed class ConfigWorkspace
{
    public static readonly string[] GameExecutableCandidates = { "THSSS.exe", "THSSS", "THSSS.dll" };

    public ConfigWorkspace(string dataRoot, string? applicationDirectory = null)
    {
        DataRoot = Path.GetFullPath(dataRoot);
        _applicationDirectory = Path.GetFullPath(applicationDirectory ?? AppContext.BaseDirectory);
    }

    private readonly string _applicationDirectory;

    public string DataRoot { get; }
    public string IniPath => Path.Combine(DataRoot, ConfigSettings.FileName);

    public static string ResolveDataRoot(string? explicitRoot)
    {
        if (!string.IsNullOrEmpty(explicitRoot)) return Path.GetFullPath(explicitRoot);
        string baseDirectory = AppContext.BaseDirectory;
        foreach (string candidate in new[] { baseDirectory, Path.Combine(baseDirectory, "game") })
            if (File.Exists(Path.Combine(candidate, ConfigSettings.FileName))) return Path.GetFullPath(candidate);
        return Path.GetFullPath(baseDirectory);
    }

    public string? FindGameExecutable()
    {
        // Packaged Avalonia lives below the BCL launcher; an external player-data
        // directory must not hide the game beside that launcher.
        string parent = Path.GetFileName(Path.TrimEndingDirectorySeparator(_applicationDirectory))
            .Equals("ConfigApp", StringComparison.OrdinalIgnoreCase)
            ? Directory.GetParent(Path.TrimEndingDirectorySeparator(_applicationDirectory))!.FullName
            : _applicationDirectory;
        foreach (string directory in new[] { DataRoot, _applicationDirectory, parent })
            foreach (string candidate in GameExecutableCandidates)
            {
                string path = Path.Combine(directory, candidate);
                if (File.Exists(path)) return path;
            }
        return null;
    }

    /// <summary>Starts the ported game. Returns false when no executable exists.</summary>
    public bool LaunchGame(out string message)
    {
        string? executable = FindGameExecutable();
        if (executable == null)
        {
            message = "找不到游戏程序（THSSS.exe / THSSS / THSSS.dll），未启动。";
            return false;
        }
        var info = new ProcessStartInfo(executable) { WorkingDirectory = Path.GetDirectoryName(executable)! };
        if (executable.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            info.FileName = "dotnet";
            info.ArgumentList.Add(executable);
        }
        info.Environment["THSSS_DATA_ROOT"] = DataRoot;
        Process.Start(info);
        message = $"已启动 {Path.GetFileName(executable)}";
        return true;
    }
}
