using System;
using System.IO;
using System.Text.Json;
using RepoWorkshop.Models;

namespace RepoWorkshop.Services;

/// <summary>Loads and saves <see cref="AppSettings"/> as JSON under %APPDATA%\RepoWorkshop.</summary>
public sealed class SettingsStore {
    /// <summary>Serializer settings for the settings file.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Creates a store for <paramref name="filePath"/>, or for REPOWORKSHOP_SETTINGS (dev/test runs),
    /// or the default %APPDATA% location.
    /// </summary>
    public SettingsStore(string? filePath = null) {
        FilePath = filePath ?? Environment.GetEnvironmentVariable("REPOWORKSHOP_SETTINGS") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RepoWorkshop",
            "settings.json");
    }

    /// <summary>The settings file.</summary>
    public string FilePath { get; }

    /// <summary>Reads the settings, or returns defaults if the file is missing or unreadable.</summary>
    public AppSettings Load() {
        if (!File.Exists(FilePath)) {
            return new AppSettings();
        }

        try {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new AppSettings();
        } catch (JsonException) {
            // Keep the unreadable file around rather than silently overwriting the user's projects.
            File.Copy(FilePath, FilePath + ".corrupt", overwrite: true);
            return new AppSettings();
        }
    }

    /// <summary>Writes the settings atomically (via a temporary file).</summary>
    public void Save(AppSettings settings) {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(tempPath, FilePath, overwrite: true);
    }
}
