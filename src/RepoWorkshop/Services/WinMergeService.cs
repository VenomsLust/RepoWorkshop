using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace RepoWorkshop.Services;

/// <summary>Locates and launches WinMerge.</summary>
public sealed class WinMergeService {
    /// <summary>The WinMerge executable's file name.</summary>
    private const string ExeName = "WinMergeU.exe";

    /// <summary>Reads the path configured in settings, if any.</summary>
    private readonly Func<string?> _configuredPath;

    /// <summary>Creates the service; <paramref name="configuredPath"/> is read each time WinMerge is needed.</summary>
    public WinMergeService(Func<string?> configuredPath) {
        _configuredPath = configuredPath;
    }

    /// <summary>Finds a WinMerge install via the registry, common install folders, then PATH.</summary>
    public static string? Detect() {
        foreach (string? candidate in Candidates()) {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate)) {
                return candidate;
            }
        }
        return null;
    }

    /// <summary>The executable that will be used: the configured path if valid, otherwise an auto-detected one.</summary>
    public string? ResolveExecutable() {
        string? configured = _configuredPath();
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) {
            return configured;
        }
        return Detect();
    }

    /// <summary>Opens a two-file comparison.</summary>
    public void CompareFiles(string left, string right, string leftTitle, string rightTitle, bool leftReadOnly, bool rightReadOnly) {
        List<string> args = ["/e", "/u"];
        if (leftReadOnly) {
            args.Add("/wl");
        }
        if (rightReadOnly) {
            args.Add("/wr");
        }
        args.AddRange(["/dl", leftTitle, "/dr", rightTitle, left, right]);
        Launch(args);
    }

    /// <summary>Opens a recursive, read-only folder comparison.</summary>
    public void CompareFolders(string left, string right, string leftTitle, string rightTitle) {
        Launch(["/r", "/e", "/u", "/wl", "/wr", "/dl", leftTitle, "/dr", rightTitle, left, right]);
    }

    /// <summary>Starts WinMerge with the given arguments without waiting for it.</summary>
    private void Launch(IEnumerable<string> args) {
        string exe = ResolveExecutable()
                     ?? throw new InvalidOperationException("WinMerge was not found. Install it or set its location in Settings.");
        ProcessStartInfo psi = new(exe) { UseShellExecute = false };
        foreach (string arg in args) {
            psi.ArgumentList.Add(arg);
        }
        Process.Start(psi)?.Dispose();
    }

    /// <summary>Possible WinMergeU.exe locations, most likely first.</summary>
    private static IEnumerable<string?> Candidates() {
        if (OperatingSystem.IsWindows()) {
            RegistryKey[] hives = [Registry.CurrentUser, Registry.LocalMachine];
            string[] keys = [@"SOFTWARE\Thingamahoochie\WinMerge", @"SOFTWARE\WOW6432Node\Thingamahoochie\WinMerge"];
            foreach (RegistryKey hive in hives) {
                foreach (string key in keys) {
                    using RegistryKey? k = hive.OpenSubKey(key);
                    yield return k?.GetValue("Executable") as string;
                }
            }
        }

        string[] folders = [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
        ];
        foreach (string folder in folders) {
            if (folder.Length > 0) {
                yield return Path.Combine(folder, "WinMerge", ExeName);
            }
        }

        string pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (string dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)) {
            yield return Path.Combine(dir.Trim(), ExeName);
        }
    }
}
