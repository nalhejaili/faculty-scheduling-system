using System;
using System.IO;
using System.Text.Json;

namespace TrainerScheduler.Network;

public static class LanSettingsStore
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string GetPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TrainerScheduler");

        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "lan-settings.json");
    }

    public static LanSettings LoadOrDefault()
    {
        try
        {
            var path = GetPath();
            if (!File.Exists(path))
                return new LanSettings();

            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<LanSettings>(json, _jsonOptions);
            if (settings is null)
                return new LanSettings();

            if (settings.Port <= 0 || settings.Port > 65535)
                settings.Port = 5178;

            settings.ServerHost = string.IsNullOrWhiteSpace(settings.ServerHost) ? "127.0.0.1" : settings.ServerHost.Trim();
            return settings;
        }
        catch
        {
            return new LanSettings();
        }
    }

    public static void Save(LanSettings settings)
    {
        if (settings is null) throw new ArgumentNullException(nameof(settings));

        settings.UpdatedUtc = DateTime.UtcNow;
        var path = GetPath();
        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        File.WriteAllText(path, json);
    }
}
