using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace RepoWorkshop.Services;

/// <summary>Locates and launches WinMerge.</summary>
public sealed class WinMergeService(Func<string?> configuredPath)
{
    private const string ExeName = "WinMergeU.exe";

    /// <summary>The executable that will be used: the configured path if valid, otherwise an auto-detected one.</summary>
    public string? ResolveExecutable()
    {
        var configured = configuredPath();
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;
        return Detect();
    }

    /// <summary>Finds a WinMerge install via the registry, common install folders, then PATH.</summary>
    public static string? Detect()
    {
        foreach (var candidate in Candidates())
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    private static IEnumerable<string?> Candidates()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                foreach (var key in new[] { @"SOFTWARE\Thingamahoochie\WinMerge", @"SOFTWARE\WOW6432Node\Thingamahoochie\WinMerge" })
                {
                    using var k = hive.OpenSubKey(key);
                    yield return k?.GetValue("Executable") as string;
                }
            }
        }

        foreach (var folder in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
                 })
        {
            if (folder.Length > 0)
                yield return Path.Combine(folder, "WinMerge", ExeName);
        }

        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            yield return Path.Combine(dir.Trim(), ExeName);
    }

    public void CompareFiles(string left, string right, string leftTitle, string rightTitle, bool leftReadOnly, bool rightReadOnly)
    {
        var args = new List<string> { "/e", "/u" };
        if (leftReadOnly) args.Add("/wl");
        if (rightReadOnly) args.Add("/wr");
        args.AddRange(["/dl", leftTitle, "/dr", rightTitle, left, right]);
        Launch(args);
    }

    public void CompareFolders(string left, string right, string leftTitle, string rightTitle)
    {
        Launch(["/r", "/e", "/u", "/wl", "/wr", "/dl", leftTitle, "/dr", rightTitle, left, right]);
    }

    private void Launch(IEnumerable<string> args)
    {
        var exe = ResolveExecutable()
                  ?? throw new InvalidOperationException("WinMerge was not found. Install it or set its location in Settings.");
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        Process.Start(psi)?.Dispose();
    }
}
