using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Tasks;
using CustomMediaRPC.Models; // Понадобится для AlbumArtInfo
using CustomMediaRPC.Utils; // Понадобится для Constants
using SpotifyAPI.Web; // Добавили через NuGet

namespace CustomMediaRPC.Services
{
    // Используем тот же record, что и в LastFmService для совместимости
    // public record AlbumArtInfo(string? ImageUrl, string? AlbumTitle); // Убираем дублирующее определение

    public class SpotifyService
    {
        // ВНИМАНИЕ: Хранение ключей API прямо в коде не рекомендуется по соображениям безопасности.
        // Лучше использовать конфигурационные файлы или переменные окружения.
        private const string SpotifyClientId = "373a628bcfc04a2a908a82c3b7671304";
        private const string SpotifyClientSecret = "750d08523d8b4b798145ae128f26bb55";

        private SpotifyClient? _spotifyClient;
        private readonly ConcurrentDictionary<string, (AlbumArtInfo? Info, DateTime Expiry)> _cache;
        private static readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(Constants.LastFm.CACHE_DURATION_MINUTES); // Используем константу из LastFm для начала
        private static readonly TimeSpan _notFoundCacheDuration = TimeSpan.FromMinutes(15); // Как в LastFm

        public SpotifyService()
        {
            _cache = new ConcurrentDictionary<string, (AlbumArtInfo?, DateTime)>();
            // Инициализация клиента будет чуть позже, при первом запросе или в конструкторе
            Debug.WriteLine("SpotifyService initialized.");
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
                Debug.WriteLine($"SpotifyService Sync Cache HIT for key '{cacheKey}'. Info: {cached.Info?.ImageUrl ?? "null"}");
                info = cached.Info;
                return true;
            }

            Debug.WriteLine($"SpotifyService Sync Cache MISS for key '{cacheKey}'.");
            return false;
        }

        private async Task InitializeClientAsync()
        {
            if (_spotifyClient == null)
            {
                try
                {
                    var config = SpotifyClientConfig.CreateDefault();
                    var request = new ClientCredentialsRequest(SpotifyClientId, SpotifyClientSecret);
                    var response = await new OAuthClient(config).RequestToken(request);

                    _spotifyClient = new SpotifyClient(config.WithToken(response.AccessToken));
                    Debug.WriteLine("SpotifyService: SpotifyClient initialized successfully.");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SpotifyService Error: Failed to initialize SpotifyClient: {ex.Message}");
                    _spotifyClient = null; // Убедимся, что клиент не используется, если инициализация не удалась
                }
            }
        }

        public async Task<AlbumArtInfo?> GetAlbumArtInfoAsync(string artist, string track, string? album)
        {
             if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(track))
            {
                Debug.WriteLine("SpotifyService Skip: Artist or track is empty.");
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
                Debug.WriteLine($"SpotifyService Async Cache HIT for key '{cacheKey}'. Returning cached info: {cached.Info?.ImageUrl ?? "null"}");
                return cached.Info;
            }

            Debug.WriteLine($"SpotifyService Async Cache MISS for key '{cacheKey}'. Performing API lookup.");

            await InitializeClientAsync(); // Убедимся, что клиент инициализирован
            if (_spotifyClient == null)
            {
                 Debug.WriteLine("SpotifyService Error: Spotify client is not initialized. Cannot perform search.");
                 return null; // Не можем выполнить поиск без клиента
            }

            AlbumArtInfo? result = null;
            try
            {
                // TODO: Реализовать логику поиска в Spotify API
                // 1. Попробовать найти трек по `artist` и `track`.
                // 2. Если нашли трек, взять обложку его альбома.
                // 3. Если `album` задан, можно попробовать найти альбом напрямую по `artist` и `album`.

                Debug.WriteLine($"SpotifyService: Performing search for Artist: '{artist}', Track: '{track}', Album: '{album ?? "N/A"}'");
                
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

                 var searchResponse = await _spotifyClient.Search.Item(searchRequest);

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
                     Debug.WriteLine($"SpotifyService: Found album '{foundAlbumTitle}' directly. Image: {imageUrl ?? "none"}");
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
                     Debug.WriteLine($"SpotifyService: Found track '{foundTrack.Name}', album '{foundAlbumTitle}'. Image: {imageUrl ?? "none"}");
                 }
                 else
                 {
                     Debug.WriteLine($"SpotifyService: No results found for the search query.");
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

            }
            catch (APIException ex)
            {
                 Debug.WriteLine($"SpotifyService API Error during search: {ex.Message}");
                 // Возможно, токен истек? Попробуем обновить (но базовая Client Credentials Flow обычно долгоживущая)
                 if (ex.Response?.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                 {
                     Debug.WriteLine("SpotifyService: Unauthorized error, possibly expired token. Clearing client for reinitialization on next call.");
                     _spotifyClient = null; // Сбросим клиента, чтобы пересоздать при след. вызове
                 }
                 result = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SpotifyService Error during API lookup: {ex.Message}");
                result = null;
            }

            // Логика кеширования результата (включая null как "не найдено")
            var cacheDuration = result?.ImageUrl != null 
                ? _cacheDuration 
                : _notFoundCacheDuration; 
            var expiryTime = DateTime.UtcNow.Add(cacheDuration);
            _cache[cacheKey] = (result, expiryTime);
            Debug.WriteLine($"SpotifyService Cached result for key '{cacheKey}' with expiry {expiryTime}. Result: {result?.ImageUrl ?? "null"}, Album: {result?.AlbumTitle ?? "null"}");

            return result;
        }
    }
} 