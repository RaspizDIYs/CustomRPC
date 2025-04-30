using System;
using System.Threading.Tasks;
using Windows.Media.Control;
using DiscordRPC;
using System.Diagnostics;
using CustomMediaRPC.Models;
using CustomMediaRPC.Utils;
using System.Threading;

namespace CustomMediaRPC.Services;

public class MediaStateManager
{
    private readonly SpotifyService _spotifyService;
    private readonly DeezerService _deezerService;
    private readonly AppSettings _settings;
    private MediaState _currentState;
    private string? _selectedSourceAppId;
    private DateTime _lastPresenceUpdateTime = DateTime.MinValue;
    private RichPresence? _lastSentPresence;
    private readonly TimeSpan _presenceUpdateThrottle = TimeSpan.FromMilliseconds(1000);
    private DateTime _lastStateChangeTime = DateTime.MinValue;
    private DateTime? _currentTrackStartTime;
    private CancellationTokenSource? _presenceBuildCts;

    // Делаем публичное свойство для доступа к времени старта трека
    public DateTime? CurrentTrackStartTime => _currentTrackStartTime;

    public MediaStateManager(SpotifyService spotifyService, DeezerService deezerService, AppSettings settings)
    {
        _spotifyService = spotifyService ?? throw new ArgumentNullException(nameof(spotifyService));
        _deezerService = deezerService ?? throw new ArgumentNullException(nameof(deezerService));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _currentState = new MediaState();
        DebugLogger.Log($"MediaStateManager initialized with SpotifyService: {spotifyService.GetHashCode()}, DeezerService: {deezerService.GetHashCode()}");
    }

    public string? SelectedSourceAppId
    {
        get => _selectedSourceAppId;
        set
        {
            if (_selectedSourceAppId != value)
            {
                DebugLogger.Log($"SelectedSourceAppId changing from '{_selectedSourceAppId}' to '{value}'");
                _selectedSourceAppId = value;
                // Сбрасываем состояние при смене источника
                _currentState = new MediaState { SourceAppId = value };
                _lastSentPresence = null;
                _lastPresenceUpdateTime = DateTime.MinValue;
                _lastStateChangeTime = DateTime.MinValue;
                CancelPreviousPresenceBuild();
                DebugLogger.Log($"State reset after source change. New state: {_currentState}");
            }
        }
    }

    public MediaState CurrentState => _currentState;

    public async Task<RichPresence?> BuildRichPresenceAsync(GlobalSystemMediaTransportControlsSession session, Stopwatch? stopwatch = null)
    {
        stopwatch ??= Stopwatch.StartNew(); // Если stopwatch не передан, начинаем свой
        var initialElapsedMs = stopwatch.ElapsedMilliseconds;
        DebugLogger.Log($"[BUILD {initialElapsedMs}ms] BuildRichPresenceAsync started.");

        CancelPreviousPresenceBuild();
        _presenceBuildCts = new CancellationTokenSource();
        var cancellationToken = _presenceBuildCts.Token;
        
        try
        {
            DebugLogger.Log($"[BUILD {stopwatch.ElapsedMilliseconds}ms] BuildRichPresenceAsync called for session: {session.SourceAppUserModelId}");
            var playbackInfo = session.GetPlaybackInfo();
            var mediaProperties = await session.TryGetMediaPropertiesAsync();

            if (mediaProperties == null)
            {
                DebugLogger.Log("BuildRichPresenceAsync: MediaProperties is null");
                return null;
            }

            DebugLogger.Log($"[BUILD {stopwatch.ElapsedMilliseconds}ms] Fetched session properties.");
            DebugLogger.Log($"[BUILD {stopwatch.ElapsedMilliseconds}ms] MediaProperties - Title: '{mediaProperties.Title}', Artist: '{mediaProperties.Artist}', Album: '{mediaProperties.AlbumTitle}'");
            DebugLogger.Log($"[BUILD {stopwatch.ElapsedMilliseconds}ms] PlaybackInfo - Status: {playbackInfo?.PlaybackStatus}");

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
                Debug.WriteLine($"[BUILD {stopwatch.ElapsedMilliseconds}ms] State changed! Previous: {_currentState}, New: {newState}. Time since last change: {(now - _lastStateChangeTime).TotalMilliseconds}ms");
                _currentState = newState;
                _lastSentPresence = null;
                _lastStateChangeTime = now;
                
                // --- Управление временем начала трека ---
                if (_currentState.Status == MediaPlaybackStatus.Playing)
                {
                    _currentTrackStartTime = now; // Запоминаем время начала
                    Debug.WriteLine($"[BUILD {stopwatch.ElapsedMilliseconds}ms] Track started playing. StartTime set to {_currentTrackStartTime}");
                }
                else
                {
                    _currentTrackStartTime = null; // Сбрасываем время, если не играет
                    Debug.WriteLine("[BUILD {stopwatch.ElapsedMilliseconds}ms] Track is not playing. StartTime reset.");
                }
                // ----------------------------------------
            }
            else
            {
                Debug.WriteLine($"[BUILD {stopwatch.ElapsedMilliseconds}ms] State hasn't changed. Current: {_currentState}. Time since last change: {(now - _lastStateChangeTime).TotalMilliseconds}ms");
                // Если состояние не изменилось, нет смысла пересоздавать presence,
                // но мы должны вернуть _lastSentPresence, если он есть, чтобы ShouldUpdatePresence мог работать корректно
                 // Если _lastSentPresence не null, значит мы уже успешно отправляли это состояние
                // Если null, значит состояние хоть и не изменилось, но мы его еще не отправляли (например, после сброса)
                // В этом случае нужно все равно построить presence
                if (_lastSentPresence != null) {
                     Debug.WriteLine($"[BUILD {stopwatch.ElapsedMilliseconds}ms] Returning cached presence as state hasn't changed.");
                     return _lastSentPresence;
                }
                 Debug.WriteLine($"[BUILD {stopwatch.ElapsedMilliseconds}ms] State unchanged but no cached presence, proceeding to build.");

            }

            // Строим RichPresence на основе *актуального* _currentState
            string detailsText = _currentState.GetDisplayTitle();
            string stateText = _currentState.GetStatusText();
            string largeImageUrl = GetDefaultImageUrl();
            string largeImageText = _currentState.Album ?? _currentState.Title ?? string.Empty;

            // Пытаемся получить информацию об обложке, если включено
            if (_settings.EnableCoverArtFetching && 
                !string.IsNullOrWhiteSpace(_currentState.Artist) && 
                !string.IsNullOrWhiteSpace(_currentState.Title) && 
                _currentState.Artist != Constants.Media.UNKNOWN_ARTIST &&
                _currentState.Title != Constants.Media.UNKNOWN_TITLE)
            {
                 AlbumArtInfo? albumArtInfo = null;
                 string selectedSource = _settings.CoverArtSource ?? "Spotify"; // Источник по умолчанию - Spotify
                 DebugLogger.Log($"[BUILD {stopwatch.ElapsedMilliseconds}ms] Attempting to fetch album art from {selectedSource}...");
                 cancellationToken.ThrowIfCancellationRequested();

                 try
                 {
                    if (selectedSource.Equals("Deezer", StringComparison.OrdinalIgnoreCase))
                    {
                        // Передаем stopwatch в DeezerService
                        albumArtInfo = await _deezerService.GetAlbumArtInfoAsync(_currentState.Artist, _currentState.Title, _currentState.Album, stopwatch, cancellationToken);
                    }
                    else // По умолчанию используем Spotify
                    {
                         // Передаем stopwatch в SpotifyService
                        albumArtInfo = await _spotifyService.GetAlbumArtInfoAsync(_currentState.Artist, _currentState.Title, _currentState.Album, stopwatch, cancellationToken);
                    }
                 }
                 catch (Exception serviceEx)
                 {
                      DebugLogger.Log($"[BUILD {stopwatch.ElapsedMilliseconds}ms] Error fetching cover art from {selectedSource}", serviceEx);
                      albumArtInfo = null; // Считаем, что обложку не получили
                 }
                 
                 DebugLogger.Log($"[BUILD {stopwatch.ElapsedMilliseconds}ms] {selectedSource} fetch finished (Cancelled: {cancellationToken.IsCancellationRequested}). Found image: {!string.IsNullOrEmpty(albumArtInfo?.ImageUrl)}");

                 if (cancellationToken.IsCancellationRequested)
                 {
                     DebugLogger.Log($"[BUILD {stopwatch.ElapsedMilliseconds}ms] Cover art fetch was cancelled after call.");
                 }
                 else if (albumArtInfo != null) // Обрабатываем результат, если не отменено
                 {
                    if (!string.IsNullOrEmpty(albumArtInfo.ImageUrl))
                    {
                        largeImageUrl = albumArtInfo.ImageUrl; 
                        largeImageText = albumArtInfo.AlbumTitle ?? _currentState.Album ?? _currentState.Title ?? string.Empty;
                        DebugLogger.Log($"[BUILD {stopwatch.ElapsedMilliseconds}ms] Used album art from {selectedSource}: {largeImageUrl}");
                    }
                    else if (albumArtInfo.AlbumTitle != null) // Нашли только название альбома
                    {   
                        largeImageText = albumArtInfo.AlbumTitle; 
                         DebugLogger.Log($"[BUILD {stopwatch.ElapsedMilliseconds}ms] {selectedSource} found album info ('{albumArtInfo.AlbumTitle}') but no image. Using default image URL with {selectedSource} album title.");
                    } 
                    else 
                    {
                         DebugLogger.Log($"[BUILD {stopwatch.ElapsedMilliseconds}ms] {selectedSource} returned null info. Using default image/text.");
                    }
                 }
                 // Если albumArtInfo == null (и не отменено), значит сервис вернул null (ошибка или не найдено)
            }
            else
            {
                 DebugLogger.Log($"[BUILD {stopwatch.ElapsedMilliseconds}ms] Skipping Cover Art lookup (disabled or missing info).");
            }

            // --- Проверка длины largeImageText --- 
            if (largeImageText.Length < 2)
            {
                 Debug.WriteLine($"[BUILD {stopwatch.ElapsedMilliseconds}ms] Initial largeImageText ('{largeImageText}') is too short (< 2 chars).");
                 // Пытаемся использовать название трека
                 if (!string.IsNullOrEmpty(_currentState.Title) && _currentState.Title.Length >= 2)
                 {
                     largeImageText = _currentState.Title;
                     Debug.WriteLine($"[BUILD {stopwatch.ElapsedMilliseconds}ms] Falling back to Title: '{largeImageText}'");
                 }
                 // Если трек тоже короткий или пустой, используем Details (Artist - Title)
                 else if (!string.IsNullOrEmpty(detailsText) && detailsText.Length >= 2)
                 {
                     largeImageText = detailsText;
                     Debug.WriteLine($"[BUILD {stopwatch.ElapsedMilliseconds}ms] Falling back to Details: '{largeImageText}'");
                 }
                 // В крайнем случае, используем дефолт
                 else
                 {
                     largeImageText = "Album Art"; 
                     Debug.WriteLine($"[BUILD {stopwatch.ElapsedMilliseconds}ms] Falling back to default 'Album Art'");
                 }
            }
            // --- Конец проверки длины --- 

            string safeDetails = StringUtils.TruncateStringByBytesUtf8(detailsText, Constants.Media.MAX_PRESENCE_TEXT_LENGTH);
            string safeState = StringUtils.TruncateStringByBytesUtf8(stateText, Constants.Media.MAX_PRESENCE_TEXT_LENGTH);
            string safeLargeImageText = StringUtils.TruncateStringByBytesUtf8(largeImageText, Constants.Media.MAX_PRESENCE_TEXT_LENGTH);

            // --- Создание Timestamps ---
            Timestamps? timestamps = null;
            if (_currentState.Status == MediaPlaybackStatus.Playing && _currentTrackStartTime.HasValue)
            {
                var timelineProperties = session.GetTimelineProperties();
                if (timelineProperties != null && timelineProperties.EndTime > TimeSpan.Zero)
                {
                    // Рассчитываем время окончания трека
                    var duration = timelineProperties.EndTime;
                    var endTimeUtc = _currentTrackStartTime.Value.Add(duration);
                    timestamps = new Timestamps
                    {
                        Start = _currentTrackStartTime.Value,
                        End = endTimeUtc
                    };
                    Debug.WriteLine($"[BUILD {stopwatch.ElapsedMilliseconds}ms] Setting timestamps Start={timestamps.Start}, End={timestamps.End} (Duration: {duration})");
                }
                else
                {
                    // Если длительность неизвестна, ставим только время начала
                    timestamps = new Timestamps { Start = _currentTrackStartTime.Value };
                    Debug.WriteLine($"[BUILD {stopwatch.ElapsedMilliseconds}ms] Setting timestamp Start={timestamps.Start} (End time unavailable)");
                }
            }
            else
            {
                 Debug.WriteLine("[BUILD {stopwatch.ElapsedMilliseconds}ms] Not setting timestamps (not playing or startTime is null).");
            }
            // ---------------------------

            DebugLogger.Log($"[BUILD {stopwatch.ElapsedMilliseconds}ms] Timestamps created/checked.");

            var presence = new RichPresence
            {
                Details = safeDetails,
                State = safeState,
                Type = ActivityType.Listening,
                Assets = new Assets
                {
                    LargeImageKey = largeImageUrl,
                    LargeImageText = safeLargeImageText
                },
                Timestamps = timestamps
            };

            Debug.WriteLine($"[BUILD {stopwatch.ElapsedMilliseconds}ms] BuildRichPresenceAsync: Created presence - Details: '{safeDetails}', State: '{safeState}', ImageKey: '{largeImageUrl}', ImageText: '{safeLargeImageText}'");
            Debug.WriteLine($"[BUILD {stopwatch.ElapsedMilliseconds}ms] BuildRichPresenceAsync: Successfully built new presence: {presence}");

            var finalElapsedMs = stopwatch.ElapsedMilliseconds;
            DebugLogger.Log($"[BUILD {finalElapsedMs}ms] BuildRichPresenceAsync finished successfully. Total time: {finalElapsedMs - initialElapsedMs}ms");
            return presence;
        }
        catch (OperationCanceledException)
        {
             DebugLogger.Log($"[BUILD {stopwatch.ElapsedMilliseconds}ms] BuildRichPresenceAsync: Operation was cancelled.");
            return null;
        }
        catch (Exception ex)
        {
             DebugLogger.Log($"[BUILD {stopwatch.ElapsedMilliseconds}ms] BuildRichPresenceAsync Error", ex);
            return null;
        }
        finally
        {
             // Не останавливаем stopwatch здесь, он управляется вызывающим кодом
        }
    }

    public bool ShouldUpdatePresence(RichPresence? newPresence, Stopwatch? stopwatch = null)
    {
        stopwatch ??= Stopwatch.StartNew();
        DebugLogger.Log($"[SHOULD {stopwatch.ElapsedMilliseconds}ms] ShouldUpdatePresence called.");

        if (newPresence == null)
        {
            DebugLogger.Log($"[SHOULD {stopwatch.ElapsedMilliseconds}ms] ShouldUpdatePresence: New presence is null. -> FALSE");
            return false;
        }

        var now = DateTime.UtcNow;
        // Убираем троттлинг по времени, т.к. Discord сам имеет rate limit, а нам важнее скорость первого обновления
        // if (now - _lastPresenceUpdateTime < _presenceUpdateThrottle)
        // {
        //     DebugLogger.Log($"[SHOULD {stopwatch.ElapsedMilliseconds}ms] ShouldUpdatePresence: Throttled. -> FALSE");
        //     return false;
        // }

        if (AreRichPresenceEqual(newPresence, _lastSentPresence))
        {
            DebugLogger.Log($"[SHOULD {stopwatch.ElapsedMilliseconds}ms] ShouldUpdatePresence: Presence hasn't changed. -> FALSE");
            return false;
        }

         DebugLogger.Log($"[SHOULD {stopwatch.ElapsedMilliseconds}ms] ShouldUpdatePresence: Presence changed or needs update. -> TRUE");
        _lastPresenceUpdateTime = now; // Обновляем время последнего *успешного* обновления
        _lastSentPresence = newPresence; // Сохраняем то, что собираемся отправить
        return true;
    }

    private bool AreRichPresenceEqual(RichPresence? p1, RichPresence? p2)
    {
        // Базовые проверки на null
        if (ReferenceEquals(p1, p2)) { DebugLogger.Log("AreRichPresenceEqual: Same instance."); return true; } // Оптимизация: одна и та же ссылка
        if (p1 is null || p2 is null) { DebugLogger.Log($"AreRichPresenceEqual: One is null (p1: {p1 == null}, p2: {p2 == null})."); return false; } 

        bool detailsEqual = p1.Details == p2.Details;
        if (!detailsEqual) { DebugLogger.Log($"AreRichPresenceEqual: Details differ ('{p1.Details}' != '{p2.Details}')."); return false; }

        bool stateEqual = p1.State == p2.State;
        if (!stateEqual) { DebugLogger.Log($"AreRichPresenceEqual: State differ ('{p1.State}' != '{p2.State}')."); return false; }

        // Сравнение Assets
        var a1 = p1.Assets;
        var a2 = p2.Assets;
        bool assetsEqual = false;
        if (ReferenceEquals(a1, a2)) { assetsEqual = true; }
        else if (a1 is null || a2 is null) { assetsEqual = false; }
        else { assetsEqual = a1.LargeImageKey == a2.LargeImageKey && a1.LargeImageText == a2.LargeImageText; }
        
        if (!assetsEqual) 
        { 
            string a1Str = a1 == null ? "null" : $"LKey='{a1.LargeImageKey}', LText='{a1.LargeImageText}'";
            string a2Str = a2 == null ? "null" : $"LKey='{a2.LargeImageKey}', LText='{a2.LargeImageText}'";
            DebugLogger.Log($"AreRichPresenceEqual: Assets differ ({a1Str} != {a2Str})."); 
            return false; 
        }
        
        // Сравнение Timestamps
        var t1 = p1.Timestamps;
        var t2 = p2.Timestamps;
        bool timestampsEqual = false;
        if (ReferenceEquals(t1, t2)) { timestampsEqual = true; } // Оба null или одна ссылка
        else if (t1 is null || t2 is null) { timestampsEqual = false; } // Один null, другой нет
        else { timestampsEqual = t1.Start == t2.Start && t1.End == t2.End; } // Сравниваем значения

        if (!timestampsEqual) 
        { 
            string t1Str = t1 == null ? "null" : $"Start={t1.Start?.ToString() ?? "null"}, End={t1.End?.ToString() ?? "null"}";
            string t2Str = t2 == null ? "null" : $"Start={t2.Start?.ToString() ?? "null"}, End={t2.End?.ToString() ?? "null"}";
            DebugLogger.Log($"AreRichPresenceEqual: Timestamps differ ({t1Str} != {t2Str})."); 
            return false; 
        }

        // Если все проверки пройдены
        DebugLogger.Log("AreRichPresenceEqual: All compared properties are equal.");
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

    public void CancelPreviousPresenceBuild()
    {
        if (_presenceBuildCts != null)
        {
            Debug.WriteLine("MediaStateManager: Cancelling previous presence build task.");
            _presenceBuildCts.Cancel();
            _presenceBuildCts.Dispose();
            _presenceBuildCts = null;
        }
    }

    private string GetDefaultImageUrl()
    {
        if (_settings.UseCustomDefaultCover && IsValidImageUrl(_settings.CustomDefaultCoverUrl))
        {
             Debug.WriteLine($"Using custom default cover URL: {_settings.CustomDefaultCoverUrl}");
            return _settings.CustomDefaultCoverUrl;
        }
         Debug.WriteLine($"Using hardcoded default cover URL: {Constants.Media.DEFAULT_IMAGE_URL}");
        return Constants.Media.DEFAULT_IMAGE_URL;
    }

    private bool IsValidImageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        // Убираем проверку на расширения, достаточно http/https
        return (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
                url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
    }

    // ВОССТАНОВЛЕННЫЙ метод для сброса кэша извне (нужен для реконнекта)
    public void SetLastSentPresence(RichPresence? presence)
    {
        _lastSentPresence = presence;
        DebugLogger.Log($"SetLastSentPresence explicitly set. Details: {presence?.Details ?? "null"}");
    }
} 