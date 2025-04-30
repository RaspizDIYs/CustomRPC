using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Web; // Для HttpUtility.UrlEncode
using CustomMediaRPC.Models;
using CustomMediaRPC.Utils;

namespace CustomMediaRPC.Services
{
    // DTOs для ответа Deezer API (упрощенные)
    internal class DeezerSearchResult
    {
        [JsonPropertyName("data")]
        public DeezerTrack[]? Data { get; set; }
    }

    internal class DeezerTrack
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("artist")]
        public DeezerArtist? Artist { get; set; }

        [JsonPropertyName("album")]
        public DeezerAlbum? Album { get; set; }
    }
    
    internal class DeezerArtist
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    internal class DeezerAlbum
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("cover_small")]
        public string? CoverSmall { get; set; } // 56x56
        
        [JsonPropertyName("cover_medium")]
        public string? CoverMedium { get; set; } // 250x250
        
        [JsonPropertyName("cover_big")]
        public string? CoverBig { get; set; } // 500x500
        
        [JsonPropertyName("cover_xl")]
        public string? CoverXl { get; set; } // 1000x1000
    }

    public class DeezerService
    {
        private readonly AppSettings _settings; // Пока не используется, но может пригодиться
        private readonly ConcurrentDictionary<string, (AlbumArtInfo? Info, DateTime Expiry)> _cache;
        private static readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(Constants.LastFm.CACHE_DURATION_MINUTES); // Переиспользуем значение
        private static readonly TimeSpan _notFoundCacheDuration = TimeSpan.FromMinutes(15); // Переиспользуем значение
        private const string DeezerApiBaseUrl = "https://api.deezer.com";

        // Статический HttpClient для переиспользования с таймаутом
        private static readonly HttpClient _httpClient = new HttpClient()
        {
            Timeout = TimeSpan.FromSeconds(3) // Таймаут 3 секунды
        };

        public DeezerService(AppSettings settings)
        {
            _settings = settings;
            _cache = new ConcurrentDictionary<string, (AlbumArtInfo?, DateTime)>();
            DebugLogger.Log("DeezerService initialized.");
        }

        public async Task<AlbumArtInfo?> GetAlbumArtInfoAsync(
            string artist,
            string track,
            string? album, // Альбом Deezer ищет сам по треку
            Stopwatch? stopwatch = null,
            CancellationToken cancellationToken = default)
        {
            stopwatch ??= Stopwatch.StartNew();
            var initialElapsedMs = stopwatch.ElapsedMilliseconds;
            DebugLogger.Log($"[DEEZER {initialElapsedMs}ms] GetAlbumArtInfoAsync called for Artist: '{artist}', Track: '{track}'.");

            if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(track))
            {
                DebugLogger.Log($"[DEEZER {stopwatch.ElapsedMilliseconds}ms] Skip: Artist or track is empty.");
                return null;
            }

            // Deezer обычно хорошо ищет по артисту и треку, ключ кеша делаем по ним
            string cacheKey = $"track_{artist}_{track}".ToLowerInvariant();

            if (_cache.TryGetValue(cacheKey, out var cached) && cached.Expiry > DateTime.UtcNow)
            {
                DebugLogger.Log($"[DEEZER {stopwatch.ElapsedMilliseconds}ms] Cache HIT for key '{cacheKey}'. Returning cached info.");
                return cached.Info;
            }

            DebugLogger.Log($"[DEEZER {stopwatch.ElapsedMilliseconds}ms] Cache MISS for key '{cacheKey}'. Performing API lookup.");
            cancellationToken.ThrowIfCancellationRequested();

            AlbumArtInfo? result = null;
            long apiCallStartMs = -1, apiCallEndMs = -1;

            try
            {
                // Формируем запрос: ищем трек по артисту и названию
                // Используем кавычки для точного совпадения фраз
                string query = $"artist:\"{HttpUtility.UrlEncode(artist)}\" track:\"{HttpUtility.UrlEncode(track)}\"";
                string requestUrl = $"{DeezerApiBaseUrl}/search?q={query}";

                DebugLogger.Log($"[DEEZER {stopwatch.ElapsedMilliseconds}ms] Performing search with URL: {requestUrl}");
                apiCallStartMs = stopwatch.ElapsedMilliseconds;
                
                HttpResponseMessage response = await _httpClient.GetAsync(requestUrl, cancellationToken);
                apiCallEndMs = stopwatch.ElapsedMilliseconds;
                DebugLogger.Log($"[DEEZER {apiCallEndMs}ms] API Search finished. Status: {response.StatusCode}. Time taken: {apiCallEndMs - apiCallStartMs}ms.");

                response.EnsureSuccessStatusCode(); // Выбросит исключение, если статус не 2xx

                string jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);
                var searchResult = JsonSerializer.Deserialize<DeezerSearchResult>(jsonResponse);

                if (searchResult?.Data != null && searchResult.Data.Length > 0)
                {
                    // Берем первый результат
                    var foundTrack = searchResult.Data[0];
                    if (foundTrack.Album != null)
                    {
                        // Выбираем лучший URL обложки (XL > Big > Medium > Small)
                        string? imageUrl = foundTrack.Album.CoverXl ?? foundTrack.Album.CoverBig ?? foundTrack.Album.CoverMedium ?? foundTrack.Album.CoverSmall;
                        string? foundAlbumTitle = foundTrack.Album.Title;
                        
                        if (!string.IsNullOrWhiteSpace(imageUrl))
                        {
                             result = new AlbumArtInfo(imageUrl, foundAlbumTitle);
                            DebugLogger.Log($"[DEEZER {stopwatch.ElapsedMilliseconds}ms] Found result: Image={result.ImageUrl}, Album={result.AlbumTitle ?? "null"}");
                        }
                         else if (!string.IsNullOrWhiteSpace(foundAlbumTitle))
                        {
                            // Нашли альбом, но без картинки
                            result = new AlbumArtInfo(null, foundAlbumTitle);
                            DebugLogger.Log($"[DEEZER {stopwatch.ElapsedMilliseconds}ms] Found album info ('{foundAlbumTitle}') but no image URL.");
                        }
                        else {
                             DebugLogger.Log($"[DEEZER {stopwatch.ElapsedMilliseconds}ms] Found track but no suitable album info.");
                        }
                    }
                    else
                    {
                        DebugLogger.Log($"[DEEZER {stopwatch.ElapsedMilliseconds}ms] Found track but no album data.");
                    }
                }
                else
                {
                     DebugLogger.Log($"[DEEZER {stopwatch.ElapsedMilliseconds}ms] No results found for the search query.");
                }
            }
            catch (OperationCanceledException)
            {
                // apiCallEndMs может быть не установлен, если отмена произошла до ответа
                apiCallEndMs = stopwatch.ElapsedMilliseconds;
                DebugLogger.Log($"[DEEZER {apiCallEndMs}ms] API search cancelled. Time elapsed: {(apiCallStartMs >= 0 ? apiCallEndMs - apiCallStartMs : -1)}ms");
                result = null;
            }
            catch (HttpRequestException ex)
            {
                // apiCallEndMs может быть не установлен, если ошибка произошла до ответа (например, таймаут)
                apiCallEndMs = stopwatch.ElapsedMilliseconds;
                 // Проверяем на таймаут
                 if (ex.InnerException is TaskCanceledException tce && !tce.CancellationToken.IsCancellationRequested)
                 {
                      DebugLogger.Log($"[DEEZER {apiCallEndMs}ms] Deezer API Error: HttpClient Timeout. Time elapsed: {(apiCallStartMs >= 0 ? apiCallEndMs - apiCallStartMs : -1)}ms");
                 }
                 else
                 {
                      DebugLogger.Log($"[DEEZER {apiCallEndMs}ms] Deezer API Error during search: {ex.Message} (StatusCode: {ex.StatusCode}). Time elapsed: {(apiCallStartMs >= 0 ? apiCallEndMs - apiCallStartMs : -1)}ms");
                 }
                 result = null;
            }
             catch (JsonException ex)
            {
                 apiCallEndMs = stopwatch.ElapsedMilliseconds; // Ошибка произошла после получения ответа
                 DebugLogger.Log($"[DEEZER {apiCallEndMs}ms] Deezer API Error: Failed to parse JSON response. Time elapsed: {(apiCallStartMs >= 0 ? apiCallEndMs - apiCallStartMs : -1)}ms", ex);
                 result = null;
            }
            catch (Exception ex)
            {
                apiCallEndMs = stopwatch.ElapsedMilliseconds;
                DebugLogger.Log($"[DEEZER {apiCallEndMs}ms] Deezer Service Error during API lookup: {ex.Message}. Time elapsed: {(apiCallStartMs >= 0 ? apiCallEndMs - apiCallStartMs : -1)}ms", ex);
                result = null;
            }

            // Кешируем результат (включая null, если не нашли)
            var cacheDuration = (result?.ImageUrl != null) ? _cacheDuration : _notFoundCacheDuration;
            var expiryTime = DateTime.UtcNow.Add(cacheDuration);
            _cache[cacheKey] = (result, expiryTime);
            DebugLogger.Log($"[DEEZER {stopwatch.ElapsedMilliseconds}ms] Cached result for key '{cacheKey}'. Expiry: {expiryTime}");

            var finalElapsedMs = stopwatch.ElapsedMilliseconds;
            DebugLogger.Log($"[DEEZER {finalElapsedMs}ms] GetAlbumArtInfoAsync finished. Total time: {finalElapsedMs - initialElapsedMs}ms");
            return result;
        }
    }
} 