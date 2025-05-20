using System;
using System.Threading.Tasks;
using Windows.Media.Control;
using DiscordRPC;
using System.Diagnostics;
using CustomMediaRPC.Models;
using CustomMediaRPC.Utils;
using System.Threading;
using System.Collections.Generic;
using Windows.Storage.Streams;
using System.Text;
using CustomMediaRPC.Views;

namespace CustomMediaRPC.Services;

public class MediaStateManager
{
    // Singleton pattern
    private static readonly Lazy<MediaStateManager> _lazyInstance = new Lazy<MediaStateManager>(() => new MediaStateManager());
    public static MediaStateManager Instance => _lazyInstance.Value;

    private SpotifyService? _spotifyService;
    private DeezerService? _deezerService;
    private AppSettings? _settings;
    private MediaState _currentState;
    private string? _selectedSourceAppId;
    private DateTime _lastPresenceUpdateTime = DateTime.MinValue;
    private RichPresence? _lastSentPresence;
    private readonly TimeSpan _presenceUpdateThrottle = TimeSpan.FromMilliseconds(1000);
    private DateTime _lastStateChangeTime = DateTime.MinValue;
    private CancellationTokenSource? _presenceBuildCts;

    // Список выбранных сайтов для кнопок (получаем извне)
    public List<string> SelectedLinkSites { get; set; } = new List<string>();

    private MediaStateManager() // Сделал конструктор приватным
    {
        _currentState = new MediaState();
        // Инициализация здесь, если что-то нужно сделать до получения зависимостей
        DebugLogger.Log("MediaStateManager Singleton instance created.");
    }

    // Метод для инициализации зависимостей
    public void Initialize(SpotifyService spotifyService, DeezerService deezerService, AppSettings settings)
    {
        _spotifyService = spotifyService ?? throw new ArgumentNullException(nameof(spotifyService));
        _deezerService = deezerService ?? throw new ArgumentNullException(nameof(deezerService));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        DebugLogger.Log($"MediaStateManager initialized with dependencies: Spotify={_spotifyService.GetHashCode()}, Deezer={_deezerService.GetHashCode()}");
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
        // --- Добавлена проверка зависимостей --- 
        if (_spotifyService == null || _deezerService == null || _settings == null)
        {
            DebugLogger.Log("[BUILD ---ms] BuildRichPresenceAsync skipped: Services or settings not initialized.");
            return null;
        }
        // ----------------------------------------

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
            var mediaTimeline = session.GetTimelineProperties();

            if (mediaProperties == null)
            {
                DebugLogger.Log("BuildRichPresenceAsync: MediaProperties is null");
                return null;
            }

            DebugLogger.Log($"[BUILD {stopwatch.ElapsedMilliseconds}ms] Fetched session properties.");
            DebugLogger.Log($"[BUILD {stopwatch.ElapsedMilliseconds}ms] MediaProperties - Title: '{mediaProperties.Title}', Artist: '{mediaProperties.Artist}', Album: '{mediaProperties.AlbumTitle}'");
            DebugLogger.Log($"[BUILD {stopwatch.ElapsedMilliseconds}ms] PlaybackInfo - Status: {playbackInfo?.PlaybackStatus}");

            // Получаем позицию и длительность из таймлайна
            var currentPosition = mediaTimeline?.Position;
            var totalDuration = mediaTimeline?.EndTime; // EndTime обычно представляет общую длительность

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
                },
                CurrentPosition = currentPosition, // Сохраняем позицию
                TotalDuration = totalDuration,     // Сохраняем длительность
                CoverArtThumbnail = mediaProperties.Thumbnail // Сохраняем миниатюру
            };

            Debug.WriteLine($"BuildRichPresenceAsync: Created new state from session: {newState}");
            Debug.WriteLine($"BuildRichPresenceAsync: Thumbnail present: {newState.CoverArtThumbnail != null}"); // Логируем наличие миниатюры
            Debug.WriteLine($"BuildRichPresenceAsync: Timeline: Pos={currentPosition}, End={totalDuration}"); // Логируем время

            // Сравниваем новое состояние с текущим
            var now = DateTime.UtcNow;
            
            // AreStatesEqual сравнивает только Title, Artist, Album, Status, SourceAppId
            bool majorContentChanged = !AreStatesEqual(_currentState, newState);

            // Всегда обновляем _currentState последними данными из SMTC
            // Это важно для корректного отображения времени и обложки, даже если "основной" трек не сменился
            _currentState = newState;

            if (majorContentChanged)
            {
                Debug.WriteLine($"[BUILD {stopwatch.ElapsedMilliseconds}ms] Major content changed! Previous state for AreStatesEqual: {{Title='{_currentState.Title}', Artist='{_currentState.Artist}', Album='{_currentState.Album}', Status='{_currentState.Status}'}}, New state: {{Title='{newState.Title}', Artist='{newState.Artist}', Album='{newState.Album}', Status='{newState.Status}'}}. Time since last change: {(now - _lastStateChangeTime).TotalMilliseconds}ms");
                // Сбрасываем _lastSentPresence только если изменился основной контент (трек, альбом, исполнитель, статус).
                // Это позволит AreRichPresenceEqual в ShouldUpdatePresence корректно сравнить новый presence 
                // с предыдущим для того же трека (но, возможно, с обновленным Timestamp).
                _lastSentPresence = null; 
                _lastStateChangeTime = now;
            }
            else
            {
                Debug.WriteLine($"[BUILD {stopwatch.ElapsedMilliseconds}ms] Major content hasn't changed. Current state (now updated): {_currentState}. Time since last change: {(now - _lastStateChangeTime).TotalMilliseconds}ms");
                // Если основной контент не изменился, _lastSentPresence НЕ сбрасываем.
                // Это позволяет AreRichPresenceEqual отфильтровать отправку, если даже Timestamp не сильно изменился.
                // Однако, если _lastSentPresence был null (например, сразу после запуска или смены источника), 
                // то мы всё равно должны построить presence.
                // AreRichPresenceEqual в ShouldUpdatePresence позаботится о том, чтобы не отправлять лишнего.
                // Убрана проверка `if (_lastSentPresence != null) return _lastSentPresence;`
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

            // Сохраняем полученный URL обложки в состояние (даже если это дефолтный)
            _currentState.CoverArtUrl = largeImageUrl; // URL для Discord
            // _currentState.CoverArtThumbnail уже установлен при создании newState
            DebugLogger.Log($"[BUILD {stopwatch.ElapsedMilliseconds}ms] Stored CoverArtUrl in CurrentState: {_currentState.CoverArtUrl}");

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

            // --- Новая логика создания Timestamps на основе TimelineProperties ---
            Timestamps? timestamps = null;

            if (playbackInfo?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing && mediaTimeline != null)
            {
                var position = mediaTimeline.Position;
                var startTimeForDiscord = now - position;
                DateTime? endTimeForDiscord = null;

                if (mediaTimeline.EndTime > TimeSpan.Zero)
                {
                    endTimeForDiscord = startTimeForDiscord + mediaTimeline.EndTime;
                }

                timestamps = new Timestamps
                {
                    Start = startTimeForDiscord,
                    End = endTimeForDiscord 
                };
                DebugLogger.Log($"[BUILD {stopwatch.ElapsedMilliseconds}ms] Setting timestamps based on SMTC: Position={position}, Calculated Start={startTimeForDiscord}, Calculated End={endTimeForDiscord?.ToString() ?? "null"}");
            }
            else
            {
                 DebugLogger.Log($"[BUILD {stopwatch.ElapsedMilliseconds}ms] Not setting timestamps (Not playing or timeline properties unavailable). PlaybackStatus: {playbackInfo?.PlaybackStatus}");
            }
            // -----------------------------------------------------------------

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
                Timestamps = timestamps,
                Buttons = BuildLinkButtons(stopwatch)
            };

            Debug.WriteLine($"[BUILD {stopwatch.ElapsedMilliseconds}ms] BuildRichPresenceAsync: Created presence - Details: '{safeDetails}', State: '{safeState}', ImageKey: '{largeImageUrl}', ImageText: '{safeLargeImageText}'");
            Debug.WriteLine($"[BUILD {stopwatch.ElapsedMilliseconds}ms] BuildRichPresenceAsync: Successfully built new presence: {presence}");

            var finalElapsedMs = stopwatch.ElapsedMilliseconds;
            DebugLogger.Log($"[BUILD {finalElapsedMs}ms] BuildRichPresenceAsync finished successfully. Total time: {finalElapsedMs - initialElapsedMs}ms");

            // Обновляем Floating Player, если он активен
            if (_settings.EnableFloatingPlayer && _currentState != null)
            {
                FloatingPlayerService.Instance.UpdateContent(
                    _currentState.GetDisplayTitle(), 
                    _currentState.Artist ?? "Unknown Artist", 
                    _currentState.Status, 
                    _currentState.CoverArtThumbnail, 
                    _currentState.CurrentPosition,  // Передаем CurrentPosition
                    _currentState.TotalDuration     // Передаем TotalDuration
                );
            }

            DebugLogger.Log("[BUILD] Finished BuildRichPresenceAsync");
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
            DebugLogger.Log($"[SHOULD {stopwatch?.ElapsedMilliseconds}ms] ShouldUpdatePresence: Presence hasn't changed. -> FALSE");
            return false;
        }

        DebugLogger.Log($"[SHOULD {stopwatch?.ElapsedMilliseconds}ms] ShouldUpdatePresence: Presence changed (Old: '{_lastSentPresence?.Details ?? "null"}' New: '{newPresence?.Details ?? "null"}') or needs update. -> TRUE");
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

        // Сравнение Кнопок (добавлено)
        bool buttonsEqual = AreButtonsEqual(p1.Buttons, p2.Buttons);
        if (!buttonsEqual) 
        { 
            DebugLogger.Log($"AreRichPresenceEqual: Buttons differ."); 
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

    // Новый метод для создания кнопок ссылок
    private Button[]? BuildLinkButtons(Stopwatch? stopwatch = null)
    {
        stopwatch ??= Stopwatch.StartNew(); 
        DebugLogger.Log($"[BUTTONS {stopwatch.ElapsedMilliseconds}ms] Entering BuildLinkButtons.");

        // SelectedLinkSites теперь содержит ВНУТРЕННИЕ ключи
        if (SelectedLinkSites == null || SelectedLinkSites.Count == 0) 
        {
            DebugLogger.Log($"[BUTTONS {stopwatch.ElapsedMilliseconds}ms] No sites selected (SelectedLinkSites is null or empty), returning null buttons.");
            return null;
        }

        DebugLogger.Log($"[BUTTONS {stopwatch.ElapsedMilliseconds}ms] CurrentState before button loop: Artist='{_currentState?.Artist ?? "null"}', Title='{_currentState?.Title ?? "null"}'");

        List<Button> buttons = new List<Button>();
        DebugLogger.Log($"[BUTTONS {stopwatch.ElapsedMilliseconds}ms] Building buttons for selected internal site keys: [{string.Join(", ", SelectedLinkSites)}]");

        // siteInternalKey будет содержать "GitHub", "VkMusic" и т.д.
        foreach (var siteInternalKey in SelectedLinkSites)
        {
            DebugLogger.Log($"[BUTTONS {stopwatch.ElapsedMilliseconds}ms] Processing internal key: '{siteInternalKey}'.");

            // Передаем ВНУТРЕННИЙ ключ в GetTrackUrlForService
            string? trackUrl = GetTrackUrlForService(siteInternalKey, _currentState?.Artist, _currentState?.Title, stopwatch);

            if (!string.IsNullOrEmpty(trackUrl))
            {
                string buttonLabel;
                string localizedSiteName = LocalizationManager.GetString($"LinkButton_{siteInternalKey}") ?? siteInternalKey;

                // Определяем текст кнопки на основе ВНУТРЕННЕГО ключа
                if (siteInternalKey.Equals(LinkButtonKeys.Genius, StringComparison.OrdinalIgnoreCase))
                {
                    buttonLabel = "Text on Genius"; // Или взять из ресурсов
                }
                else if (siteInternalKey.Equals(LinkButtonKeys.GitHub, StringComparison.OrdinalIgnoreCase))
                {
                    // Используем специфичный текст для GitHub
                    buttonLabel = "Open on Github"; // Или создать ключ ресурса типа OpenOnButtonFormat?
                }
                // Сюда можно добавить другие особые случаи по ВНУТРЕННЕМУ ключу
                else
                {
                    buttonLabel = $"Listen on {localizedSiteName}"; 
                    DebugLogger.Log($"[BUTTONS {stopwatch.ElapsedMilliseconds}ms] Using simple label format: '{buttonLabel}' (Localized name: '{localizedSiteName}', InternalKey: '{siteInternalKey}')");
                }
                
                if (Encoding.UTF8.GetByteCount(buttonLabel) > 32)
                {
                    DebugLogger.Log($"[BUTTONS {stopwatch.ElapsedMilliseconds}ms] Button label '{buttonLabel}' for internal key '{siteInternalKey}' is too long, trimming...");
                    int safeLength = buttonLabel.Length;
                    while (Encoding.UTF8.GetByteCount(buttonLabel.Substring(0, safeLength)) > 32 && safeLength > 0) { safeLength--; }
                    buttonLabel = buttonLabel.Substring(0, safeLength);
                    DebugLogger.Log($"[BUTTONS {stopwatch.ElapsedMilliseconds}ms] Trimmed label: '{buttonLabel}'");
                }

                buttons.Add(new Button
                {
                    Label = buttonLabel, 
                    Url = trackUrl
                });
                DebugLogger.Log($"[BUTTONS {stopwatch.ElapsedMilliseconds}ms] Successfully added button '{buttonLabel}' for internal key '{siteInternalKey}' with URL: {trackUrl}");
            }
            else
            {
                 DebugLogger.Log($"[BUTTONS {stopwatch.ElapsedMilliseconds}ms] GetTrackUrlForService returned null or empty for internal key '{siteInternalKey}', skipping button.");
            }
            
            if (buttons.Count >= 2)
            {
                 DebugLogger.Log($"[BUTTONS {stopwatch.ElapsedMilliseconds}ms] Reached button limit (2). Stopping loop.");
                 break; 
            }
        }
        
        DebugLogger.Log($"[BUTTONS {stopwatch.ElapsedMilliseconds}ms] Finished building buttons. Count: {buttons.Count}");
        return buttons.Count > 0 ? buttons.ToArray() : null;
    }
    
    // Теперь serviceName - это ВНУТРЕННИЙ ключ (например, "VkMusic")
    private string? GetTrackUrlForService(string serviceName, string? artist, string? title, Stopwatch? stopwatch = null)
    {
        stopwatch ??= Stopwatch.StartNew();
        DebugLogger.Log($"[URL {stopwatch.ElapsedMilliseconds}ms] Entering GetTrackUrlForService for internal key '{serviceName}'. Artist: '{artist}', Title: '{title}'");

        if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(title) || artist == Constants.Media.UNKNOWN_ARTIST || title == Constants.Media.UNKNOWN_TITLE)
        {
            DebugLogger.Log($"[URL {stopwatch.ElapsedMilliseconds}ms] Cannot get URL for '{serviceName}': Artist ('{artist}') or Title ('{title}') is missing or unknown.");
            return null;
        } 
        
        string query;
        try {
             query = Uri.EscapeDataString($"{artist!} {title!}"); 
             // Сохраним время перед логированием
             long elapsedMs = stopwatch.ElapsedMilliseconds;
             DebugLogger.Log($"[URL {elapsedMs}ms] Generated query for '{serviceName}': '{query}'");
        } catch (UriFormatException ex) {
             // Сохраним время перед логированием
             long elapsedMs = stopwatch.ElapsedMilliseconds;
             DebugLogger.Log($"[URL {elapsedMs}ms] Error escaping query for '{serviceName}' ('{artist} {title}')", ex);
             return null;
        }

        string? url = null;
        // Убираем генерацию serviceKey, сравниваем serviceName напрямую с константами LinkButtonKeys

        // TODO: Добавить реальные API вызовы или более умный поиск вместо прямого формирования URL

        if (serviceName.Equals(LinkButtonKeys.Spotify, StringComparison.OrdinalIgnoreCase))
        {
            url = $"https://open.spotify.com/search/{query}";
        }
        else if (serviceName.Equals(LinkButtonKeys.YouTubeMusic, StringComparison.OrdinalIgnoreCase))
        {
            url = $"https://music.youtube.com/search?q={query}";
        }
        else if (serviceName.Equals(LinkButtonKeys.AppleMusic, StringComparison.OrdinalIgnoreCase))
        {
            url = $"https://music.apple.com/us/search?term={query}";
        }
        else if (serviceName.Equals(LinkButtonKeys.YandexMusic, StringComparison.OrdinalIgnoreCase))
        {
            url = $"https://music.yandex.ru/search?text={query}";
        }
        else if (serviceName.Equals(LinkButtonKeys.Deezer, StringComparison.OrdinalIgnoreCase))
        {
            url = $"https://www.deezer.com/search/{query}";
        }
        else if (serviceName.Equals(LinkButtonKeys.VkMusic, StringComparison.OrdinalIgnoreCase))
        {
            url = $"https://vk.com/audio?q={query}";
        }
        else if (serviceName.Equals(LinkButtonKeys.Genius, StringComparison.OrdinalIgnoreCase))
        {
            url = $"https://genius.com/search?q={query}";
        }
        else if (serviceName.Equals(LinkButtonKeys.GitHub, StringComparison.OrdinalIgnoreCase))
        {
            // GitHub/Project Download не зависит от трека, используем константный URL
            url = "https://github.com/RaspizDIYs/CustomRPC"; // Или взять из настроек/констант
        }
        // Другие сервисы можно добавить по аналогии
        
        if (url != null)
        {
            DebugLogger.Log($"[URL {stopwatch.ElapsedMilliseconds}ms] Successfully generated URL for internal key '{serviceName}': {url}");
        }
        else
        {
            DebugLogger.Log($"[URL {stopwatch.ElapsedMilliseconds}ms] No URL generation logic found for internal key '{serviceName}'. Returning null.");
        }

        return url;
    }

    // Новый вспомогательный метод для сравнения массивов кнопок
    private bool AreButtonsEqual(Button[]? b1, Button[]? b2)
    {
        if (ReferenceEquals(b1, b2)) return true; // Оба null или одна ссылка
        if (b1 is null || b2 is null) return false; // Один null, другой нет
        if (b1.Length != b2.Length) return false; // Разная длина

        for (int i = 0; i < b1.Length; i++)
        {
            // Добавляем null-conditional операторы (?.) при доступе к свойствам
            // и проверяем, что оба элемента не null перед сравнением
            var button1 = b1[i];
            var button2 = b2[i];

            if (button1 == null || button2 == null) // Если один из элементов null (маловероятно, но возможно)
            {
                 if (button1 == button2) continue; // Оба null, продолжаем
                 return false; // Один null, другой нет - не равны
            }
            
            // Сравниваем Label и Url каждой кнопки, теперь button1 и button2 точно не null
            if (button1.Label != button2.Label || button1.Url != button2.Url)
            {
                return false;
            }
        }

        return true; // Все кнопки идентичны
    }

    public MediaState? GetCurrentState()
    {
        return _currentState;
    }

    public void InitializeAsync()
    {
        // Метод оставлен пустым, но теперь он не async
    }
} 