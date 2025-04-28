using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Linq;
using CustomMediaRPC.Utils;
using System.Diagnostics;

namespace CustomMediaRPC
{
    public record AlbumArtInfo(string? ImageUrl, string? AlbumTitle);

    public class LastFmService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly ConcurrentDictionary<string, (AlbumArtInfo? Info, DateTime Expiry)> _cache;
        private readonly ConcurrentQueue<DateTime> _requestTimestamps;
        private static readonly TimeSpan _notFoundCacheDuration = TimeSpan.FromMinutes(15);

        public LastFmService(HttpClient httpClient, string apiKey)
        {
            _httpClient = httpClient;
            _apiKey = apiKey;
            _httpClient.DefaultRequestHeaders.Add("User-Agent", Constants.LastFm.USER_AGENT);
            _cache = new ConcurrentDictionary<string, (AlbumArtInfo?, DateTime)>();
            _requestTimestamps = new ConcurrentQueue<DateTime>();
        }

        public async Task<AlbumArtInfo?> GetAlbumArtInfoAsync(string artist, string track, string? album)
        {
            if (string.IsNullOrEmpty(_apiKey) || _apiKey.Length < Constants.LastFm.MIN_API_KEY_LENGTH)
            {
                Debug.WriteLine("LastFmService Error: API key is not configured properly");
                return null;
            }
            
            if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(track))
            {
                Debug.WriteLine("LastFmService Skip: Artist or track is empty.");
                return null;
            }

            string cacheKey = !string.IsNullOrWhiteSpace(album) 
                ? $"album_{artist}_{album}" 
                : $"track_{artist}_{track}";
                
            cacheKey = cacheKey.ToLowerInvariant();

            if (_cache.TryGetValue(cacheKey, out var cached) && cached.Expiry > DateTime.UtcNow)
            {
                Debug.WriteLine($"LastFmService Cache HIT for key '{cacheKey}'. Returning cached info: {cached.Info?.ImageUrl ?? "null"}");
                return cached.Info;
            }
            
            Debug.WriteLine($"LastFmService Cache MISS for key '{cacheKey}'. Performing API lookup.");

            AlbumArtInfo? result = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(album))
                {
                    Debug.WriteLine($"LastFmService: Album provided ('{album}'). Calling GetAlbumInfoDirectlyAsync.");
                    result = await GetAlbumInfoDirectlyAsync(artist, album);
                }
                else
                {
                    Debug.WriteLine($"LastFmService: Album not provided. Calling GetAlbumInfoFromTrackAsync for track '{track}'.");
                    result = await GetAlbumInfoFromTrackAsync(artist, track);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LastFmService Error during API lookup: {ex.Message}");
                result = null;
            }

            var cacheDuration = result?.ImageUrl != null 
                ? TimeSpan.FromMinutes(Constants.LastFm.CACHE_DURATION_MINUTES) 
                : _notFoundCacheDuration;
                
            var expiryTime = DateTime.UtcNow.Add(cacheDuration);
            _cache[cacheKey] = (result, expiryTime);
            Debug.WriteLine($"LastFmService Cached result for key '{cacheKey}' with expiry {expiryTime}. Result: {result?.ImageUrl ?? "null"}, Album: {result?.AlbumTitle ?? "null"}");

            return result;
        }
        
        private async Task<AlbumArtInfo?> GetAlbumInfoDirectlyAsync(string artist, string album)
        {
            await EnforceRateLimit();
            var url = $"{Constants.LastFm.API_BASE_URL}?method=album.getInfo&api_key={_apiKey}&artist={Uri.EscapeDataString(artist)}&album={Uri.EscapeDataString(album)}&format=json&autocorrect=1";
            Debug.WriteLine($"LastFmService: Requesting Album Info: {url}");
            
            using var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine($"LastFmService Error: album.getInfo API error: {response.StatusCode}");
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            
            try 
            {
                using var json = JsonDocument.Parse(content);
                if (!json.RootElement.TryGetProperty("album", out var albumElement))
                {
                    Debug.WriteLine("LastFmService Warning: album.getInfo response does not contain 'album' element.");
                    return null;
                }
                
                string? foundAlbumTitle = albumElement.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : album;
                string? imageUrl = ParseImageUrl(albumElement);
                
                if(imageUrl == null)
                {
                     Debug.WriteLine($"LastFmService: No 'large' image found in album.getInfo for {artist} - {album}");
                     return new AlbumArtInfo(null, foundAlbumTitle);
                }
                
                Debug.WriteLine($"LastFmService: Found image via album.getInfo: {imageUrl}, Album: {foundAlbumTitle}");
                return new AlbumArtInfo(imageUrl, foundAlbumTitle);
            }
            catch (JsonException jsonEx)
            {
                Debug.WriteLine($"LastFmService Error: Failed to parse album.getInfo JSON: {jsonEx.Message}");
                return null;
            } 
        }
        
        private async Task<AlbumArtInfo?> GetAlbumInfoFromTrackAsync(string artist, string track)
        {
             await EnforceRateLimit();
            var url = $"{Constants.LastFm.API_BASE_URL}?method=track.getInfo&api_key={_apiKey}&artist={Uri.EscapeDataString(artist)}&track={Uri.EscapeDataString(track)}&format=json&autocorrect=1";
            Debug.WriteLine($"LastFmService: Requesting Track Info: {url}");

            using var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine($"LastFmService Error: track.getInfo API error: {response.StatusCode}");
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();

            try
            {
                using var json = JsonDocument.Parse(content);
                if (!json.RootElement.TryGetProperty("track", out var trackElement) || !trackElement.TryGetProperty("album", out var albumElement))
                {
                    Debug.WriteLine($"LastFmService: No album info found in track.getInfo response for {artist} - {track}");
                    return null;
                }

                string? foundAlbumTitle = albumElement.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : null;
                string? foundArtist = albumElement.TryGetProperty("artist", out var artistEl) ? artistEl.GetString() : artist;

                if (string.IsNullOrWhiteSpace(foundAlbumTitle) || string.IsNullOrWhiteSpace(foundArtist))
                {
                     Debug.WriteLine($"LastFmService: Album title or artist missing in track.getInfo/album section for {artist} - {track}");
                     return null;
                }

                Debug.WriteLine($"LastFmService: Found album '{foundAlbumTitle}' via track.getInfo. Now calling GetAlbumInfoDirectlyAsync.");
                return await GetAlbumInfoDirectlyAsync(foundArtist, foundAlbumTitle); 
            }
            catch (JsonException jsonEx)
            {
                Debug.WriteLine($"LastFmService Error: Failed to parse track.getInfo JSON: {jsonEx.Message}");
                return null;
            }
        }
        
        private string? ParseImageUrl(JsonElement albumElement)
        {
             if (albumElement.TryGetProperty("image", out var images) && images.ValueKind == JsonValueKind.Array)
             {
                  string[] sizesToTry = ["large", "extralarge", "medium"]; 
                  foreach (var size in sizesToTry)
                  {
                       var img = images.EnumerateArray().FirstOrDefault(x => x.TryGetProperty("size", out var sizeEl) && sizeEl.GetString() == size);
                       if (img.ValueKind != JsonValueKind.Undefined)
                       {
                           if (img.TryGetProperty("#text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
                           {
                               string? url = textEl.GetString();
                               if (!string.IsNullOrWhiteSpace(url))
                               {
                                   Debug.WriteLine($"LastFmService: Parsed image URL (size: {size}): {url}");
                                   return url;
                               }
                           }
                       }
                  }
                   Debug.WriteLine($"LastFmService Warning: No valid image URL found for sizes {string.Join(',', sizesToTry)} in image array.");
             }
             else
             {
                  Debug.WriteLine("LastFmService Warning: 'image' property not found or not an array in album element.");
             }
             return null;
        }

        private async Task EnforceRateLimit()
        {
            var now = DateTime.UtcNow;
            while (_requestTimestamps.TryPeek(out var oldest) && 
                   (now - oldest).TotalMinutes >= 1)
            {
                _requestTimestamps.TryDequeue(out _);
            }

            if (_requestTimestamps.Count >= Constants.LastFm.MAX_REQUESTS_PER_MINUTE)
            {
                var oldest = _requestTimestamps.First();
                var delay = TimeSpan.FromMinutes(1) - (now - oldest);
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay);
                }
            }

            _requestTimestamps.Enqueue(now);
        }
    }
} 