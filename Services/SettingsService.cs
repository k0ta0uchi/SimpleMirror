using System.IO;
using System.Text.Json;
using SimpleMirror.Models;

namespace SimpleMirror.Services;

/// <summary>
/// アプリケーション設定の読み込み・保存を担うサービス
/// </summary>
public class SettingsService
{
    private static readonly string SettingsDirectory = 
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SimpleMirror");
    private static readonly string SettingsFilePath = 
        Path.Combine(SettingsDirectory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public AppSettings CurrentSettings { get; private set; } = new();

    public SettingsService()
    {
        Load();
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings != null)
                {
                    CurrentSettings = settings;
                    return CurrentSettings;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsService] Failed to load settings: {ex.Message}");
        }

        CurrentSettings = new AppSettings();
        Save();
        return CurrentSettings;
    }

    public void Save()
    {
        try
        {
            if (!Directory.Exists(SettingsDirectory))
            {
                Directory.CreateDirectory(SettingsDirectory);
            }

            var json = JsonSerializer.Serialize(CurrentSettings, JsonOptions);
            File.WriteAllText(SettingsFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsService] Failed to save settings: {ex.Message}");
        }
    }
}
