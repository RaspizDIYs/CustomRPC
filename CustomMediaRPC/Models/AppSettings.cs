using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CustomMediaRPC.Models;

public class AppSettings : INotifyPropertyChanged
{
    private bool _enableCoverArtFetching = true;
    private string _coverArtSource = "Spotify";
    private bool _useCustomDefaultCover;
    private string _customDefaultCoverUrl = string.Empty;
    private string _lastFmApiKey = string.Empty;
    private string _spotifyClientId = string.Empty;
    private string _spotifyClientSecret = string.Empty;
    private bool _startMinimized;
    private bool _connectOnStartup;
    private bool _autoDetectGame = true; // Existing setting, assuming default is true

    // Новые настройки для плавающего плеера
    private bool _enableFloatingPlayer;
    private bool _playerAlwaysOnTop = true; // По умолчанию включено, если плеер активен

    // Новые общие настройки
    private bool _launchOnStartup;
    private bool _autoCheckForUpdates = true; // По умолчанию включено
    private bool _silentAutoUpdates = false; // По умолчанию выключено - НОВОЕ ПОЛЕ

    // Список выбранных кнопок-ссылок
    private List<string> _selectedLinkButtonSites = new List<string>();

    [JsonIgnore] // This property is determined at runtime and should not be saved
    public bool FirstLaunch { get; set; }

    public bool EnableCoverArtFetching
    {
        get => _enableCoverArtFetching;
        set => SetField(ref _enableCoverArtFetching, value);
    }

    public string CoverArtSource
    {
        get => _coverArtSource;
        set => SetField(ref _coverArtSource, value);
    }

    public bool UseCustomDefaultCover
    {
        get => _useCustomDefaultCover;
        set => SetField(ref _useCustomDefaultCover, value);
    }

    public string CustomDefaultCoverUrl
    {
        get => _customDefaultCoverUrl;
        set => SetField(ref _customDefaultCoverUrl, value);
    }

    public string LastFmApiKey // Existing setting
    {
        get => _lastFmApiKey;
        set => SetField(ref _lastFmApiKey, value);
    }

    public string SpotifyClientId // Existing setting
    {
        get => _spotifyClientId;
        set => SetField(ref _spotifyClientId, value);
    }

    public string SpotifyClientSecret // Existing setting
    {
        get => _spotifyClientSecret;
        set => SetField(ref _spotifyClientSecret, value);
    }

    public bool StartMinimized // Existing setting
    {
        get => _startMinimized;
        set => SetField(ref _startMinimized, value);
    }

    public bool ConnectOnStartup // Existing setting
    {
        get => _connectOnStartup;
        set => SetField(ref _connectOnStartup, value);
    }

    public bool AutoDetectGame // Existing setting
    {
        get => _autoDetectGame;
        set => SetField(ref _autoDetectGame, value);
    }

    public List<string> SelectedLinkButtonSites
    {
        get => _selectedLinkButtonSites;
        // Используем new List<string>(value), чтобы SetField обнаружил изменение, 
        // если просто модифицировать существующий список, он не сработает.
        set => SetField(ref _selectedLinkButtonSites, new List<string>(value)); 
    }

    // Свойства для новых настроек
    public bool EnableFloatingPlayer
    {
        get => _enableFloatingPlayer;
        set => SetField(ref _enableFloatingPlayer, value);
    }

    public bool PlayerAlwaysOnTop
    {
        get => _playerAlwaysOnTop;
        set => SetField(ref _playerAlwaysOnTop, value);
    }

    // Свойства для новых общих настроек
    public bool LaunchOnStartup
    {
        get => _launchOnStartup;
        set => SetField(ref _launchOnStartup, value);
    }

    public bool AutoCheckForUpdates
    {
        get => _autoCheckForUpdates;
        set => SetField(ref _autoCheckForUpdates, value);
    }

    // НОВОЕ СВОЙСТВО
    public bool SilentAutoUpdates 
    {
        get => _silentAutoUpdates;
        set => SetField(ref _silentAutoUpdates, value);
    }

    public string Language { get; set; } = "ru-RU"; // Added language setting, default to Russian

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
} 