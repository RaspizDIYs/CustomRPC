using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Tasks;
using CustomMediaRPC.Models; // Понадобится для AlbumArtInfo
using CustomMediaRPC.Utils; // Понадобится для Constants
using SpotifyAPI.Web; // Добавили через NuGet
using System.Threading; // Добавлено для CancellationToken
using System.Net.Http; // <-- Добавляем для HttpClient
using SpotifyAPI.Web.Http; // <-- Добавляем для IHTTPClient и NetHttpClient

namespace CustomMediaRPC.Services
{
    // Используем тот же record, что и в LastFmService для совместимости
    // public record AlbumArtInfo(string? ImageUrl, string? AlbumTitle); // Убираем дублирующее определение

    public class SpotifyService
    {
        // Возвращаем хардкод ключей API
        private const string SpotifyClientId = "373a628bcfc04a2a908a82c3b7671304"; // TODO: Вынести в настройки!
        private const string SpotifyClientSecret = "750d08523d8b4b798145ae128f26bb55"; // TODO: Вынести в настройки!

        private readonly AppSettings _settings;
        private SpotifyClient? _spotifyClient;
        private readonly ConcurrentDictionary<string, (AlbumArtInfo? Info, DateTime Expiry)> _cache;
        private static readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(Constants.LastFm.CACHE_DURATION_MINUTES); // Используем константу из LastFm для начала
        private static readonly TimeSpan _notFoundCacheDuration = TimeSpan.FromMinutes(15); // Как в LastFm
        private SemaphoreSlim _clientInitSemaphore = new SemaphoreSlim(1, 1); // Для безопасной инициализации клиента

        // Статический HttpClient для переиспользования
        // Устанавливаем разумный таймаут
        private static readonly HttpClient _httpClient = new HttpClient()
        {
             Timeout = TimeSpan.FromSeconds(3) // <-- Таймаут 3 секунды
        };

        public SpotifyService(AppSettings settings) // Добавили AppSettings
        {
            _settings = settings;
            _cache = new ConcurrentDictionary<string, (AlbumArtInfo?, DateTime)>();
            DebugLogger.Log("SpotifyService initialized.");
        }

        // Новый метод для синхронной проверки кеша
        public bool TryGetCachedAlbumArtInfo(string artist, string track, string? album, out AlbumArtInfo? info)
        {
            info = null;
            if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(track))
            {
                return false; // Невалидные входные данные
            }

            string cacheKey = !string.IsNullOrWhiteSpace(album)
                ? $"album_{artist}_{album}"
                : $"track_{artist}_{track}";
            cacheKey = cacheKey.ToLowerInvariant();

            if (_cache.TryGetValue(cacheKey, out var cached) && cached.Expiry > DateTime.UtcNow)
            {
                DebugLogger.Log($"SpotifyService Sync Cache HIT for key '{cacheKey}'. Info: {cached.Info?.ImageUrl ?? "null"}");
                info = cached.Info;
                return true;
            }

            DebugLogger.Log($"SpotifyService Sync Cache MISS for key '{cacheKey}'.");
            return false;
        }

        private async Task InitializeClientAsync(CancellationToken cancellationToken = default) // Добавили CancellationToken
        {
            if (_spotifyClient != null) return; // Уже инициализирован

            // Проверяем наличие ключей (теперь используем константы)
            if (string.IsNullOrWhiteSpace(SpotifyClientId) || string.IsNullOrWhiteSpace(SpotifyClientSecret))
            {
                DebugLogger.Log("SpotifyService Error: Hardcoded Spotify Client ID or Secret is missing.");
                return;
            }

            await _clientInitSemaphore.WaitAsync(cancellationToken); // Ждем семафор
            try
            {
                if (_spotifyClient != null) return; // Еще одна проверка внутри лока

                DebugLogger.Log("SpotifyService: Initializing SpotifyClient...");

                // Создаем IHTTPClient с нашим HttpClient
                IHTTPClient spotifyHttpClient = new NetHttpClient(_httpClient);

                // Создаем конфиг и передаем ему наш IHTTPClient
                var config = SpotifyClientConfig.CreateDefault()
                    .WithHTTPClient(spotifyHttpClient);

                // Используем константы вместо _settings
                var request = new ClientCredentialsRequest(SpotifyClientId, SpotifyClientSecret);
                
                cancellationToken.ThrowIfCancellationRequested(); // Проверка перед запросом
                
                // OAuthClient не поддерживает CancellationToken напрямую в RequestToken
                // Используем стандартный HttpClient с таймаутом или оберткой Task.Run, если нужно строгое прерывание
                // Но чаще всего достаточно проверок до и после
                var response = await new OAuthClient(config).RequestToken(request);
                // TODO: Add timeout or CancellationToken to OAuthClient request if library supports it or via HttpClient
                
                cancellationToken.ThrowIfCancellationRequested(); // Проверка после запроса

                _spotifyClient = new SpotifyClient(config.WithToken(response.AccessToken));
                DebugLogger.Log("SpotifyService: SpotifyClient initialized successfully.");
            }
            catch (OperationCanceledException)
            {
                DebugLogger.Log("SpotifyService: SpotifyClient initialization cancelled.");
                _spotifyClient = null;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"SpotifyService Error: Failed to initialize SpotifyClient: {ex.Message}");
                // Проверяем, не связана ли ошибка с таймаутом HttpClient
                if (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested) {
                    DebugLogger.Log("SpotifyService: Initialization likely failed due to HttpClient timeout.");
                }
                _spotifyClient = null; // Убедимся, что клиент не используется, если инициализация не удалась
            }
            finally
            {
                 _clientInitSemaphore.Release(); // Освобождаем семафор
            }
        }

        public async Task<AlbumArtInfo?> GetAlbumArtInfoAsync(
            string artist, 
            string track, 
            string? album, 
            Stopwatch? stopwatch = null,
            CancellationToken cancellationToken = default)
        {
            stopwatch ??= Stopwatch.StartNew(); // Если не передан, создаем свой
            var initialElapsedMs = stopwatch.ElapsedMilliseconds;
            DebugLogger.Log($"[SPOTIFY {initialElapsedMs}ms] GetAlbumArtInfoAsync called for Artist: '{artist}', Track: '{track}'.");

            if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(track))
            {
                DebugLogger.Log($"[SPOTIFY {stopwatch.ElapsedMilliseconds}ms] Skip: Artist or track is empty.");
                return null;
            }
            
            // Используем похожий ключ кеша, как в LastFmService
            // Отдаем приоритет альбому, если он есть
            string cacheKey = !string.IsNullOrWhiteSpace(album) 
                ? $"album_{artist}_{album}" 
                : $"track_{artist}_{track}";
            cacheKey = cacheKey.ToLowerInvariant();

            // Проверяем кеш еще раз перед async вызовом (на случай если между TryGet и этим вызовом что-то попало в кеш)
            if (_cache.TryGetValue(cacheKey, out var cached) && cached.Expiry > DateTime.UtcNow)
            {
                DebugLogger.Log($"[SPOTIFY {stopwatch.ElapsedMilliseconds}ms] Async Cache HIT for key '{cacheKey}'. Returning cached info.");
                return cached.Info;
            }

            DebugLogger.Log($"[SPOTIFY {stopwatch.ElapsedMilliseconds}ms] Async Cache MISS for key '{cacheKey}'. Performing API lookup.");
            
            cancellationToken.ThrowIfCancellationRequested();

            // Инициализация клиента (Stopwatch сюда не передаем, она быстрая или использует Semaphore)
            await InitializeClientAsync(cancellationToken);
            if (_spotifyClient == null)
            {
                 DebugLogger.Log($"[SPOTIFY {stopwatch.ElapsedMilliseconds}ms] Error: Spotify client is not initialized. Cannot perform search.");
                 return null;
            }

            AlbumArtInfo? result = null;
            long apiCallStartMs = -1, apiCallEndMs = -1; // Для замера времени API вызова
            try
            {
                // TODO: Реализовать логику поиска в Spotify API
                // 1. Попробовать найти трек по `artist` и `track`.
                // 2. Если нашли трек, взять обложку его альбома.
                // 3. Если `album` задан, можно попробовать найти альбом напрямую по `artist` и `album`.

                DebugLogger.Log($"[SPOTIFY {stopwatch.ElapsedMilliseconds}ms] Performing search for Artist: '{artist}', Track: '{track}', Album: '{album ?? "N/A"}'");
                
                // Примерный плейсхолдер поиска (нужно доработать)
                 SearchRequest searchRequest;
                 if (!string.IsNullOrWhiteSpace(album))
                 {
                     // Ищем альбом, если он указан
                     searchRequest = new SearchRequest(SearchRequest.Types.Album, $"artist:{artist} album:{album}");
                 }
                 else
                 {
                     // Иначе ищем трек
                     searchRequest = new SearchRequest(SearchRequest.Types.Track, $"artist:{artist} track:{track}");
                 }

                 DebugLogger.Log($"[SPOTIFY {stopwatch.ElapsedMilliseconds}ms] Performing search with query: '{searchRequest.Query}', Type: {searchRequest.Type}");
                 apiCallStartMs = stopwatch.ElapsedMilliseconds;
                 var searchResponse = await _spotifyClient.Search.Item(searchRequest, cancellationToken);
                 apiCallEndMs = stopwatch.ElapsedMilliseconds;
                 DebugLogger.Log($"[SPOTIFY {apiCallEndMs}ms] API Search finished. Time taken: {apiCallEndMs - apiCallStartMs}ms.");

                // Обработка результатов поиска (упрощенная)
                 string? imageUrl = null;
                 string? foundAlbumTitle = null;

                 if (searchResponse.Albums?.Items != null && searchResponse.Albums.Items.Count > 0)
                 {
                     var foundAlbum = searchResponse.Albums.Items[0];
                     if (foundAlbum.Images != null && foundAlbum.Images.Count > 0)
                     {
                         // Берем самую большую картинку (первая обычно самая большая)
                         imageUrl = foundAlbum.Images[0].Url; 
                     }
                     foundAlbumTitle = foundAlbum.Name;
                     DebugLogger.Log($"[SPOTIFY {stopwatch.ElapsedMilliseconds}ms] Found album '{foundAlbumTitle}' directly. Image: {imageUrl ?? "none"}");
                 }
                 else if (searchResponse.Tracks?.Items != null && searchResponse.Tracks.Items.Count > 0)
                 {
                     var foundTrack = searchResponse.Tracks.Items[0];
                     if (foundTrack.Album?.Images != null && foundTrack.Album.Images.Count > 0)
                     {
                        // Берем самую большую картинку альбома трека
                         imageUrl = foundTrack.Album.Images[0].Url; 
                     }
                     foundAlbumTitle = foundTrack.Album?.Name;
                     DebugLogger.Log($"[SPOTIFY {stopwatch.ElapsedMilliseconds}ms] Found track '{foundTrack.Name}', album '{foundAlbumTitle}'. Image: {imageUrl ?? "none"}");
                 }
                 else
                 {
                     DebugLogger.Log($"[SPOTIFY {stopwatch.ElapsedMilliseconds}ms] No results found for the search query.");
                 }

                 if (!string.IsNullOrWhiteSpace(imageUrl))
                 {
                     result = new AlbumArtInfo(imageUrl, foundAlbumTitle);
                 }
                 else if (!string.IsNullOrWhiteSpace(foundAlbumTitle))
                 {
                     // Нашли альбом, но без картинки
                      result = new AlbumArtInfo(null, foundAlbumTitle);
                 }
                 else 
                 {
                     result = null; // Ничего не нашли
                 }

                 if (result != null)
                 {
                     DebugLogger.Log($"[SPOTIFY {stopwatch.ElapsedMilliseconds}ms] Found result: Image={result.ImageUrl ?? "null"}, Album={result.AlbumTitle ?? "null"}");
                 }
                 else
                 {
                      DebugLogger.Log($"[SPOTIFY {stopwatch.ElapsedMilliseconds}ms] No suitable result found.");
                 }

            }
            catch (OperationCanceledException)
            {
                apiCallEndMs = stopwatch.ElapsedMilliseconds;
                DebugLogger.Log($"[SPOTIFY {apiCallEndMs}ms] API search cancelled. Time elapsed: {(apiCallStartMs >= 0 ? apiCallEndMs - apiCallStartMs : -1)}ms");
                result = null;
            }
            catch (APIException ex)
            {
                apiCallEndMs = stopwatch.ElapsedMilliseconds;
                 DebugLogger.Log($"[SPOTIFY {apiCallEndMs}ms] Spotify API Error during search: {ex.Message}. Time elapsed: {(apiCallStartMs >= 0 ? apiCallEndMs - apiCallStartMs : -1)}ms");
                 // Возможно, токен истек? Попробуем обновить (но базовая Client Credentials Flow обычно долгоживущая)
                 if (ex.Response?.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                 {
                     DebugLogger.Log("SpotifyService: Unauthorized error, possibly expired token. Clearing client for reinitialization on next call.");
                     _spotifyClient = null; // Сбросим клиента, чтобы пересоздать при след. вызове
                 }
                 // Проверяем, не связана ли ошибка с таймаутом HttpClient
                 else if (ex.InnerException is TaskCanceledException && !cancellationToken.IsCancellationRequested) {
                    DebugLogger.Log("SpotifyService: Search likely failed due to HttpClient timeout.");
                 }
                 result = null;
            }
            catch (Exception ex)
            {
                apiCallEndMs = stopwatch.ElapsedMilliseconds;
                 DebugLogger.Log($"[SPOTIFY {apiCallEndMs}ms] Spotify Error during API lookup: {ex.Message}. Time elapsed: {(apiCallStartMs >= 0 ? apiCallEndMs - apiCallStartMs : -1)}ms");
                result = null;
            }

            // Логика кеширования результата (включая null как "не найдено")
            var cacheDuration = result?.ImageUrl != null 
                ? _cacheDuration 
                : _notFoundCacheDuration; 
            var expiryTime = DateTime.UtcNow.Add(cacheDuration);
            _cache[cacheKey] = (result, expiryTime);
            DebugLogger.Log($"[SPOTIFY {stopwatch.ElapsedMilliseconds}ms] Cached result for key '{cacheKey}'. Expiry: {expiryTime}");

            var finalElapsedMs = stopwatch.ElapsedMilliseconds;
            DebugLogger.Log($"[SPOTIFY {finalElapsedMs}ms] GetAlbumArtInfoAsync finished. Total time: {finalElapsedMs - initialElapsedMs}ms");
            return result;
        }
    }
} 