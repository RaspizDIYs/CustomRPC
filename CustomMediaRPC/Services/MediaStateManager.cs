using System;
using System.Threading.Tasks;
using Windows.Media.Control;
using DiscordRPC;
using System.Diagnostics;
using CustomMediaRPC.Models;
using CustomMediaRPC.Utils;

namespace CustomMediaRPC.Services;

public class MediaStateManager
{
    private readonly LastFmService _lastFmService;
    private MediaState _currentState;
    private string? _selectedSourceAppId;
    private DateTime _lastPresenceUpdateTime = DateTime.MinValue;
    private RichPresence? _lastSentPresence;
    private readonly TimeSpan _presenceUpdateThrottle = TimeSpan.FromMilliseconds(1000);
    private DateTime _lastStateChangeTime = DateTime.MinValue;
    private DateTime? _currentTrackStartTime;

    public MediaStateManager(LastFmService lastFmService)
    {
        _lastFmService = lastFmService ?? throw new ArgumentNullException(nameof(lastFmService));
        _currentState = new MediaState();
        Debug.WriteLine($"MediaStateManager initialized with LastFmService: {lastFmService.GetHashCode()}");
    }

    public string? SelectedSourceAppId
    {
        get => _selectedSourceAppId;
        set
        {
            if (_selectedSourceAppId != value)
            {
                Debug.WriteLine($"SelectedSourceAppId changing from '{_selectedSourceAppId}' to '{value}'");
                _selectedSourceAppId = value;
                // Сбрасываем состояние при смене источника
                _currentState = new MediaState { SourceAppId = value };
                _lastSentPresence = null;
                _lastPresenceUpdateTime = DateTime.MinValue;
                _lastStateChangeTime = DateTime.MinValue;
                Debug.WriteLine($"State reset after source change. New state: {_currentState}");
            }
        }
    }

    public MediaState CurrentState => _currentState;

    public async Task<RichPresence?> BuildRichPresenceAsync(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            Debug.WriteLine($"BuildRichPresenceAsync called for session: {session.SourceAppUserModelId}");
            var playbackInfo = session.GetPlaybackInfo();
            var mediaProperties = await session.TryGetMediaPropertiesAsync();

            if (mediaProperties == null)
            {
                Debug.WriteLine("BuildRichPresenceAsync: MediaProperties is null");
                return null;
            }

            Debug.WriteLine($"BuildRichPresenceAsync: MediaProperties - Title: '{mediaProperties.Title}', Artist: '{mediaProperties.Artist}', Album: '{mediaProperties.AlbumTitle}'");
            Debug.WriteLine($"BuildRichPresenceAsync: PlaybackInfo - Status: {playbackInfo?.PlaybackStatus}");

            var newState = new MediaState
            {
                Title = mediaProperties.Title ?? Constants.Media.UNKNOWN_TITLE,
                Artist = mediaProperties.Artist ?? Constants.Media.UNKNOWN_ARTIST,
                Album = mediaProperties.AlbumTitle ?? string.Empty,
                SourceAppId = session.SourceAppUserModelId,
                Status = playbackInfo?.PlaybackStatus switch
                {
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => MediaPlaybackStatus.Playing,
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => MediaPlaybackStatus.Paused,
                    _ => MediaPlaybackStatus.Stopped
                }
            };

            Debug.WriteLine($"BuildRichPresenceAsync: Created new state from session: {newState}");

            // Сравниваем новое состояние с текущим
            var now = DateTime.UtcNow;
            if (!AreStatesEqual(_currentState, newState))
            {
                Debug.WriteLine($"BuildRichPresenceAsync: State changed! Previous: {_currentState}, New: {newState}. Time since last change: {(now - _lastStateChangeTime).TotalMilliseconds}ms");
                _currentState = newState;
                _lastSentPresence = null;
                _lastStateChangeTime = now;
                
                // --- Управление временем начала трека ---
                if (_currentState.Status == MediaPlaybackStatus.Playing)
                {
                    _currentTrackStartTime = now; // Запоминаем время начала
                    Debug.WriteLine($"BuildRichPresenceAsync: Track started playing. StartTime set to {_currentTrackStartTime}");
                }
                else
                {
                    _currentTrackStartTime = null; // Сбрасываем время, если не играет
                    Debug.WriteLine("BuildRichPresenceAsync: Track is not playing. StartTime reset.");
                }
                // ----------------------------------------
            }
            else
            {
                Debug.WriteLine($"BuildRichPresenceAsync: State hasn't changed. Current: {_currentState}. Time since last change: {(now - _lastStateChangeTime).TotalMilliseconds}ms");
                // Если состояние не изменилось, нет смысла пересоздавать presence,
                // но мы должны вернуть _lastSentPresence, если он есть, чтобы ShouldUpdatePresence мог работать корректно
                 // Если _lastSentPresence не null, значит мы уже успешно отправляли это состояние
                // Если null, значит состояние хоть и не изменилось, но мы его еще не отправляли (например, после сброса)
                // В этом случае нужно все равно построить presence
                if (_lastSentPresence != null) {
                     Debug.WriteLine($"BuildRichPresenceAsync: Returning cached presence as state hasn't changed.");
                     return _lastSentPresence;
                }
                 Debug.WriteLine($"BuildRichPresenceAsync: State unchanged but no cached presence, proceeding to build.");

            }

            // Строим RichPresence на основе *актуального* _currentState
            string detailsText = _currentState.GetDisplayTitle();
            string stateText = _currentState.GetStatusText();
            string largeImageUrl = Constants.Media.DEFAULT_IMAGE_URL;
            string largeImageText = _currentState.Album ?? _currentState.Title ?? string.Empty;

            // Пытаемся получить информацию об обложке
            if (!string.IsNullOrWhiteSpace(_currentState.Artist) && 
                !string.IsNullOrWhiteSpace(_currentState.Title) && // Теперь проверяем и Title
                _currentState.Artist != Constants.Media.UNKNOWN_ARTIST &&
                _currentState.Title != Constants.Media.UNKNOWN_TITLE)
            {
                 Debug.WriteLine($"BuildRichPresenceAsync: Attempting to fetch album art for Artist='{_currentState.Artist}', Track='{_currentState.Title}', Album='{_currentState.Album ?? "N/A"}'");
                 // Вызываем новый метод GetAlbumArtInfoAsync
                var albumArtInfo = await _lastFmService.GetAlbumArtInfoAsync(_currentState.Artist, _currentState.Title, _currentState.Album);
                
                if (!string.IsNullOrEmpty(albumArtInfo?.ImageUrl))
                {
                    largeImageUrl = albumArtInfo.ImageUrl; // Используем найденный URL
                    // Используем найденное название альбома, если есть, иначе исходное, иначе трек
                    largeImageText = albumArtInfo.AlbumTitle ?? _currentState.Album ?? _currentState.Title ?? string.Empty;
                    Debug.WriteLine($"BuildRichPresenceAsync: Found album art via Last.fm: {largeImageUrl}, Album: {albumArtInfo.AlbumTitle ?? "Unknown"}");
                }
                else
                {
                    // Если albumArtInfo не null, но ImageUrl пустой, значит Last.fm вернул инфо об альбоме, но без картинки
                    if (albumArtInfo?.AlbumTitle != null)
                    {
                         largeImageText = albumArtInfo.AlbumTitle; // Используем найденное название альбома для текста
                         Debug.WriteLine($"BuildRichPresenceAsync: Last.fm found album info ('{albumArtInfo.AlbumTitle}') but no suitable image URL.");
                    }
                    else
                    {   // Если и albumArtInfo null, значит Last.fm ничего не нашел
                        largeImageText = _currentState.Album ?? _currentState.Title ?? string.Empty; // Возвращаемся к исходному альбому/треку для текста
                        Debug.WriteLine("BuildRichPresenceAsync: No album art or info found via Last.fm.");
                    }
                }
            }
            else
            {
                 Debug.WriteLine($"BuildRichPresenceAsync: Skipping Last.fm lookup (Artist or Title is missing/unknown).");
            }

            string safeDetails = StringUtils.TruncateStringByBytesUtf8(detailsText, Constants.Media.MAX_PRESENCE_TEXT_LENGTH);
            string safeState = StringUtils.TruncateStringByBytesUtf8(stateText, Constants.Media.MAX_PRESENCE_TEXT_LENGTH);
            string safeLargeImageText = StringUtils.TruncateStringByBytesUtf8(largeImageText, Constants.Media.MAX_PRESENCE_TEXT_LENGTH);

            // --- Создание Timestamps --- 
            Timestamps? timestamps = null;
            if (_currentState.Status == MediaPlaybackStatus.Playing && _currentTrackStartTime.HasValue)
            {
                timestamps = new Timestamps { Start = _currentTrackStartTime.Value };
                Debug.WriteLine($"BuildRichPresenceAsync: Setting timestamp Start={timestamps.Start}");
            }
            else
            {
                 Debug.WriteLine("BuildRichPresenceAsync: Not setting timestamps (not playing or startTime is null).");
            }
            // ---------------------------

            var presence = new RichPresence
            {
                Details = safeDetails,
                State = safeState,
                Assets = new Assets
                {
                    LargeImageKey = largeImageUrl,
                    LargeImageText = safeLargeImageText
                },
                Timestamps = timestamps
            };

            Debug.WriteLine($"BuildRichPresenceAsync: Created presence - Details: '{safeDetails}', State: '{safeState}', ImageKey: '{largeImageUrl}', ImageText: '{safeLargeImageText}'");
            return presence;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error building RichPresence: {ex}");
            return null;
        }
    }

    public bool ShouldUpdatePresence(RichPresence? newPresence)
    {
        if (newPresence == null)
        {
            Debug.WriteLine("ShouldUpdatePresence: New presence is null");
            return false;
        }

        var now = DateTime.UtcNow;
        if (now - _lastPresenceUpdateTime < _presenceUpdateThrottle)
        {
            Debug.WriteLine($"ShouldUpdatePresence: Throttled, last update was {(now - _lastPresenceUpdateTime).TotalMilliseconds}ms ago");
            return false;
        }

        if (AreRichPresenceEqual(newPresence, _lastSentPresence))
        {
            Debug.WriteLine($"ShouldUpdatePresence: Presence hasn't changed (compared to last sent). Details: {newPresence.Details}");
            return false;
        }

        Debug.WriteLine($"ShouldUpdatePresence: Presence has changed or needs update. Throttled: {now - _lastPresenceUpdateTime < _presenceUpdateThrottle}. Old: {_lastSentPresence?.Details ?? "null"}, New: {newPresence.Details}");
        _lastPresenceUpdateTime = now;
        _lastSentPresence = newPresence; // Обновляем кэш ПОСЛЕ принятия решения об отправке
        return true;
    }

    private bool AreRichPresenceEqual(RichPresence? p1, RichPresence? p2)
    {
        if (p1 == null && p2 == null) return true;
        if (p1 == null || p2 == null) return false;

        if (p1.Details != p2.Details || p1.State != p2.State) return false;

        var a1 = p1.Assets;
        var a2 = p2.Assets;
        if (a1 == null && a2 == null) return true;
        if (a1 == null || a2 == null) return false;
        if (a1.LargeImageKey != a2.LargeImageKey || a1.LargeImageText != a2.LargeImageText) return false;
        
        // --- Сравнение Timestamps --- 
        var t1 = p1.Timestamps;
        var t2 = p2.Timestamps;
        if (t1 == null && t2 == null) return true; // Оба null - равны
        if (t1 == null || t2 == null) return false; // Один null, другой нет - не равны
        if (t1.Start != t2.Start || t1.End != t2.End) return false; // Сравниваем значения Start и End
        // ----------------------------

        return true;
    }

    private bool AreStatesEqual(MediaState s1, MediaState s2)
    {
        if (s1 == null && s2 == null) return true;
        if (s1 == null || s2 == null) return false;

        return s1.Title == s2.Title &&
               s1.Artist == s2.Artist &&
               s1.Album == s2.Album &&
               s1.Status == s2.Status &&
               s1.SourceAppId == s2.SourceAppId;
    }
} 