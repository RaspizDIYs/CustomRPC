using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CustomMediaRPC.Models;

public class AppSettings : INotifyPropertyChanged
{
    private bool _loadSpotifyCover = true;
    private bool _useCustomDefaultCover;
    private string _customDefaultCoverUrl = string.Empty;
    private string _lastFmApiKey = string.Empty;
    private string _spotifyClientId = string.Empty;
    private string _spotifyClientSecret = string.Empty;
    private bool _startMinimized;
    private bool _connectOnStartup;
    private bool _autoDetectGame = true; // Existing setting, assuming default is true

    public bool LoadSpotifyCover
    {
        get => _loadSpotifyCover;
        set => SetField(ref _loadSpotifyCover, value);
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