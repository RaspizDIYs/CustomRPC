using CustomMediaRPC.Models;
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

    public AppSettings CurrentSettings { get; private set; } = new();

    public async Task LoadSettingsAsync()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = await File.ReadAllTextAsync(SettingsFilePath);
                CurrentSettings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            else
            {
                CurrentSettings = new AppSettings(); // Load defaults if file doesn't exist
            }
        }
        catch (Exception ex)
        {
            // Log error (implement proper logging later)
            Console.WriteLine($"Error loading settings: {ex.Message}");
            CurrentSettings = new AppSettings(); // Fallback to defaults on error
        }
    }

    public async Task SaveSettingsAsync()
    {
        try
        {
            var directoryPath = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(CurrentSettings, options);
            await File.WriteAllTextAsync(SettingsFilePath, json);
        }
        catch (Exception ex)
        {
            // Log error (implement proper logging later)
            Console.WriteLine($"Error saving settings: {ex.Message}");
        }
    }

    // Convenience method to trigger save when a property changes
    public void RegisterSettingsSaveOnPropertyChange()
    {
        CurrentSettings.PropertyChanged += async (sender, args) => await SaveSettingsAsync();
    }
} 