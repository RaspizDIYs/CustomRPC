using CustomMediaRPC.Models;
using CustomMediaRPC.Utils;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace CustomMediaRPC.Services;

public class SettingsService
{
    private static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CustomMediaRPC",
        "settings.json");

    private readonly AppSettings _appSettings;

    public SettingsService(AppSettings appSettings)
    {
        _appSettings = appSettings ?? throw new ArgumentNullException(nameof(appSettings));
        DebugLogger.Log("SettingsService initialized with injected AppSettings.");
    }

    public async Task LoadSettingsAsync()
    {
        AppSettings? loadedSettings = null;
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                DebugLogger.Log($"SettingsService: Loading settings from {SettingsFilePath}");
                var json = await File.ReadAllTextAsync(SettingsFilePath);
                loadedSettings = JsonSerializer.Deserialize<AppSettings>(json);
            }
            else
            {
                DebugLogger.Log($"SettingsService: Settings file not found at {SettingsFilePath}. Using default settings provided at startup.");
                return;
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"Error loading settings: {ex.Message}. Using default settings.");
            return;
        }

        if (loadedSettings != null)
        {
            DebugLogger.Log("SettingsService: Applying loaded settings to the singleton instance.");
            _appSettings.EnableCoverArtFetching = loadedSettings.EnableCoverArtFetching;
            _appSettings.CoverArtSource = loadedSettings.CoverArtSource;
            _appSettings.UseCustomDefaultCover = loadedSettings.UseCustomDefaultCover;
            _appSettings.CustomDefaultCoverUrl = loadedSettings.CustomDefaultCoverUrl;
            _appSettings.LastFmApiKey = loadedSettings.LastFmApiKey;
            _appSettings.SpotifyClientId = loadedSettings.SpotifyClientId;
            _appSettings.SpotifyClientSecret = loadedSettings.SpotifyClientSecret;
            _appSettings.StartMinimized = loadedSettings.StartMinimized;
            _appSettings.ConnectOnStartup = loadedSettings.ConnectOnStartup;
            _appSettings.AutoDetectGame = loadedSettings.AutoDetectGame;
            _appSettings.EnableFloatingPlayer = loadedSettings.EnableFloatingPlayer;
            _appSettings.PlayerAlwaysOnTop = loadedSettings.PlayerAlwaysOnTop;
            _appSettings.LaunchOnStartup = loadedSettings.LaunchOnStartup;
            _appSettings.AutoCheckForUpdates = loadedSettings.AutoCheckForUpdates;
            _appSettings.SilentAutoUpdates = loadedSettings.SilentAutoUpdates;
            _appSettings.SelectedLinkButtonSites = new List<string>(loadedSettings.SelectedLinkButtonSites);
            _appSettings.Language = loadedSettings.Language;
            DebugLogger.Log("SettingsService: Finished applying loaded settings.");
        }
    }

    public async Task SaveSettingsAsync()
    {
        try
        {
            DebugLogger.Log($"SettingsService: Saving current settings to {SettingsFilePath}");
            var directoryPath = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(_appSettings, options);
            await File.WriteAllTextAsync(SettingsFilePath, json);
            DebugLogger.Log("SettingsService: Settings saved successfully.");
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"Error saving settings: {ex.Message}");
        }
    }

    public void RegisterSettingsSaveOnPropertyChange()
    {
        DebugLogger.Log("SettingsService: Automatic saving on property change is DISABLED.");
    }
} 