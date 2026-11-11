using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WallpaperQuote.Core;

namespace WallpaperQuote.Platform
{
    public sealed class DownloadException : Exception
    {
        public DownloadException(string message, bool offline = false) : base(message) { Offline = offline; }
        public bool Offline { get; }
    }

    public sealed class RateLimitException : Exception
    {
        public RateLimitException() : base("Vượt hạn mức gọi API") { }
    }

    /// <summary>Một ảnh online tìm được, chưa tải về.</summary>
    public sealed class OnlineCandidate
    {
        public string Id { get; set; } = "";
        public string Source { get; set; } = "";
        public string ImageUrl { get; set; } = "";
        /// <summary>Link ảnh nhỏ hơn, dùng khi ảnh HD tải lỗi hoặc quá 30MB.</summary>
        public string? FallbackUrl { get; set; }
        public string Title { get; set; } = "";
        public string? Author { get; set; }
        public string License { get; set; } = "";
        public string? PageUrl { get; set; }
        public string? Date { get; set; }
    }

    public sealed class FetchResult
    {
        public List<OnlineCandidate> Accepted { get; } = new List<OnlineCandidate>();
        public int RejectedLicense { get; set; }
        public int RejectedMedia { get; set; }
    }

    /// <summary>Gọi mạng (G3). Chỉ tải dữ liệu vào; không gửi dữ liệu người dùng ra ngoài.</summary>
    public static class Net
    {
        public const string UserAgent = "WallpaperQuote/0.2 (personal desktop app; https://github.com/trangngnghien/universal-quote)";
        public const string QuoteCatalogUrl = "https://raw.githubusercontent.com/trangngnghien/universal-quote/main/catalog/quotes.json";
        public const long MaxImageBytes = 30L * 1024 * 1024;

        private static readonly HttpClient Api = Create(TimeSpan.FromSeconds(15));
        /// <summary>API NASA có lúc phản hồi chậm 15–40 giây.</summary>
        private static readonly HttpClient Slow = Create(TimeSpan.FromSeconds(60));
        private static readonly HttpClient Files = Create(TimeSpan.FromSeconds(90));

        private static HttpClient Create(TimeSpan timeout)
        {
            var client = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = timeout };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            return client;
        }

        public static bool IsOnline()
        {
            try { return System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable(); }
            catch { return true; }
        }

        // ------------------------------------------------------------ NASA APOD

        /// <summary>
        /// Lấy ảnh NASA APOD trong <paramref name="days"/> ngày gần nhất (1 lần gọi, theo khoảng ngày).
        /// Chỉ nhận ảnh (không video) có trường copyright trống (BR W5.1). Ảnh mới nhất trước.
        /// </summary>
        public static async Task<FetchResult> FetchNasaAsync(string apiKey, int days, ISet<string> skipIds, CancellationToken ct)
        {
            // Giờ Mỹ (APOD ra ảnh theo giờ miền Đông Mỹ) → lùi 1 ngày để không hỏi ngày chưa có ảnh.
            var end = DateTime.UtcNow.AddHours(-5).Date.AddDays(-1);
            var start = end.AddDays(-(days - 1));
            var url = $"https://api.nasa.gov/planetary/apod?api_key={Uri.EscapeDataString(apiKey)}" +
                      $"&start_date={start:yyyy-MM-dd}&end_date={end:yyyy-MM-dd}&thumbs=false";
            using var response = await Slow.GetAsync(url, ct);
            if ((int)response.StatusCode == 429) throw new RateLimitException();
            if ((int)response.StatusCode == 403 || (int)response.StatusCode == 401)
                throw new DownloadException("API key NASA không hợp lệ");
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

            var result = new FetchResult();
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;
            var items = new List<JsonElement>();
            foreach (var item in doc.RootElement.EnumerateArray()) items.Add(item);
            items.Reverse();   // mới nhất trước
            foreach (var item in items)
            {
                if (skipIds.Contains("nasa-" + (Str(item, "date") ?? ""))) continue;
                var media = Str(item, "media_type");
                if (media != "image") { result.RejectedMedia++; continue; }
                var copyright = Str(item, "copyright");
                if (!string.IsNullOrWhiteSpace(copyright)) { result.RejectedLicense++; continue; }
                var hd = Str(item, "hdurl");
                var normal = Str(item, "url");
                var image = hd ?? normal;
                var date = Str(item, "date");
                if (string.IsNullOrWhiteSpace(image) || string.IsNullOrWhiteSpace(date)) { result.RejectedMedia++; continue; }

                string? page = null;
                if (DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                    page = $"https://apod.nasa.gov/apod/ap{d:yyMMdd}.html";

                result.Accepted.Add(new OnlineCandidate
                {
                    Id = "nasa-" + date,
                    Source = OnlineSource.Nasa,
                    ImageUrl = image!,
                    FallbackUrl = hd != null && normal != null && normal != hd ? normal : null,
                    Title = Str(item, "title") ?? "Ảnh thiên văn NASA",
                    Author = "NASA",
                    License = "Public Domain",
                    PageUrl = page,
                    Date = date
                });
            }
            return result;
        }

        // ------------------------------------------------------------ Wikimedia Commons

        private static readonly Regex ThumbWidth = new Regex(@"/\d+px-", RegexOptions.Compiled);

        /// <summary>Ảnh trong ngày của Wikimedia Commons, <paramref name="days"/> ngày gần nhất. Chỉ nhận Public Domain / CC0 (BR W5.1).</summary>
        public static async Task<FetchResult> FetchWikimediaAsync(int days, ISet<string> skipIds, CancellationToken ct)
        {
            var result = new FetchResult();
            for (var i = 0; i < days; i++)
            {
                var day = DateTime.UtcNow.Date.AddDays(-i);
                var id = $"wiki-{day:yyyy-MM-dd}";
                if (skipIds.Contains(id)) continue;

                var url = $"https://en.wikipedia.org/api/rest_v1/feed/featured/{day:yyyy}/{day:MM}/{day:dd}";
                using var response = await Slow.GetAsync(url, ct);
                if ((int)response.StatusCode == 429) throw new RateLimitException();
                if (!response.IsSuccessStatusCode) continue;
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                if (!doc.RootElement.TryGetProperty("image", out var image)) continue;

                var licenseType = "";
                var licenseCode = "";
                if (image.TryGetProperty("license", out var license))
                {
                    licenseType = Str(license, "type") ?? "";
                    licenseCode = (Str(license, "code") ?? "").ToLowerInvariant();
                }
                var publicDomain = licenseCode == "cc0" || licenseCode.StartsWith("pd")
                    || licenseType.IndexOf("public domain", StringComparison.OrdinalIgnoreCase) >= 0
                    || licenseType.IndexOf("cc0", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!publicDomain) { result.RejectedLicense++; continue; }

                string? source = null;
                var width = 0;
                if (image.TryGetProperty("image", out var original))
                {
                    source = Str(original, "source");
                    if (original.TryGetProperty("width", out var w) && w.ValueKind == JsonValueKind.Number) width = w.GetInt32();
                }
                // Ảnh gốc quá lớn → lấy bản thu nhỏ 3840px cho nhẹ.
                if (width > 3840 && image.TryGetProperty("thumbnail", out var thumb))
                {
                    var t = Str(thumb, "source");
                    if (!string.IsNullOrEmpty(t) && ThumbWidth.IsMatch(t)) source = ThumbWidth.Replace(t, "/3840px-");
                }
                if (string.IsNullOrWhiteSpace(source)) continue;

                string? author = null;
                if (image.TryGetProperty("artist", out var artist)) author = Str(artist, "text");
                var title = (Str(image, "title") ?? "Ảnh Wikimedia Commons").Replace("File:", "");
                title = Path.GetFileNameWithoutExtension(title).Replace('_', ' ');

                result.Accepted.Add(new OnlineCandidate
                {
                    Id = id,
                    Source = OnlineSource.Wikimedia,
                    ImageUrl = source!,
                    Title = title,
                    Author = string.IsNullOrWhiteSpace(author) ? null : author!.Trim(),
                    License = string.IsNullOrWhiteSpace(licenseType) ? "Public Domain" : licenseType,
                    PageUrl = Str(image, "file_page"),
                    Date = day.ToString("yyyy-MM-dd")
                });
            }
            return result;
        }

        // ------------------------------------------------------------ Quote online

        public static async Task<OnlineQuoteCatalog?> FetchQuoteCatalogAsync(CancellationToken ct)
        {
            using var response = await Api.GetAsync(QuoteCatalogUrl, ct);
            if (!response.IsSuccessStatusCode) return null;
            var json = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<OnlineQuoteCatalog>(json, Json.Options);
        }

        // ------------------------------------------------------------ Tải ảnh

        /// <summary>
        /// Tải ảnh về <paramref name="destWithoutExtension"/> + đuôi theo loại ảnh.
        /// Link phải trả về file ảnh, tối đa 30MB (BR W1.2). Trả về đường dẫn file đã lưu.
        /// </summary>
        public static async Task<string> DownloadImageAsync(string url, string destWithoutExtension, CancellationToken ct)
        {
            HttpResponseMessage response;
            try
            {
                response = await Files.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            }
            catch (HttpRequestException ex)
            {
                throw new DownloadException(IsOnline() ? $"Không kết nối được tới máy chủ ({ex.Message})" : "Đang offline", offline: !IsOnline());
            }
            catch (TaskCanceledException)
            {
                throw new DownloadException("Hết thời gian chờ tải ảnh");
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                    throw new DownloadException($"Máy chủ trả lỗi {(int)response.StatusCode}");

                var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
                if (!mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    throw new DownloadException("Link không trả về file ảnh");
                var length = response.Content.Headers.ContentLength;
                if (length.HasValue && length.Value > MaxImageBytes)
                    throw new DownloadException("Ảnh lớn hơn 30MB");

                var ext = ExtensionFor(mediaType);
                var dest = destWithoutExtension + ext;
                var temp = dest + ".part";
                try
                {
                    using (var input = await response.Content.ReadAsStreamAsync(ct))
                    using (var output = File.Create(temp))
                    {
                        var buffer = new byte[81920];
                        long total = 0;
                        int read;
                        while ((read = await input.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                        {
                            total += read;
                            if (total > MaxImageBytes) throw new DownloadException("Ảnh lớn hơn 30MB");
                            await output.WriteAsync(buffer, 0, read, ct);
                        }
                    }
                    if (File.Exists(dest)) File.Delete(dest);
                    File.Move(temp, dest);
                    return dest;
                }
                catch (DownloadException)
                {
                    TryDelete(temp);
                    throw;
                }
                catch (Exception ex) when (ex is IOException || ex is HttpRequestException || ex is TaskCanceledException)
                {
                    TryDelete(temp);
                    throw new DownloadException("Tải ảnh bị gián đoạn");
                }
            }
        }

        private static string ExtensionFor(string mediaType)
        {
            switch (mediaType.ToLowerInvariant())
            {
                case "image/png": return ".png";
                case "image/webp": return ".webp";
                case "image/gif": return ".gif";
                case "image/bmp": return ".bmp";
                case "image/tiff": return ".tif";
                case "image/heic":
                case "image/heif": return ".heic";
                default: return ".jpg";
            }
        }

        public static string HashName(string text)
        {
            using var sha = System.Security.Cryptography.SHA1.Create();
            var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text));
            return BitConverter.ToString(bytes, 0, 10).Replace("-", "").ToLowerInvariant();
        }

        private static void TryDelete(string file)
        {
            try { if (File.Exists(file)) File.Delete(file); } catch { }
        }

        private static string? Str(JsonElement e, string name) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }
}
