using System;
using System.IO;
using System.Text.Json;
using RepoWorkshop.Models;

namespace RepoWorkshop.Services;

/// <summary>Loads and saves <see cref="AppSettings"/> as JSON under %APPDATA%\RepoWorkshop.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public SettingsStore(string? filePath = null)
    {
        // REPOWORKSHOP_SETTINGS lets a dev/test run use a separate settings file.
        FilePath = filePath ?? Environment.GetEnvironmentVariable("REPOWORKSHOP_SETTINGS") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RepoWorkshop",
            "settings.json");
    }

    public string FilePath { get; }

    public AppSettings Load()
    {
        if (!File.Exists(FilePath))
            return new AppSettings();

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new AppSettings();
        }
        catch (JsonException)
        {
            // Keep the unreadable file around rather than silently overwriting the user's projects.
            File.Copy(FilePath, FilePath + ".corrupt", overwrite: true);
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(tempPath, FilePath, overwrite: true);
    }
}
