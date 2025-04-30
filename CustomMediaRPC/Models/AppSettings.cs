using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

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

    // Список выбранных кнопок-ссылок
    private List<string> _selectedLinkButtonSites = new List<string>();

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