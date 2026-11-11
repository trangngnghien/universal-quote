using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace WallpaperQuote.Core
{
    /// <summary>Thư viện quote (W2.1). M1: bộ quote mặc định đóng gói sẵn + lọc ngôn ngữ.</summary>
    public sealed class QuoteLibrary
    {
        public const int MaxTextLength = 300;
        public const int MaxAuthorLength = 100;
        public const int MaxSourceLength = 100;

        private readonly List<Quote> _builtIn;

        public QuoteLibrary()
        {
            _builtIn = LoadBuiltIn();
        }

        public int BuiltInCount => _builtIn.Count;

        public IReadOnlyList<Quote> Active(string language)
        {
            IEnumerable<Quote> all = _builtIn;
            if (language == QuoteLanguage.Vietnamese || language == QuoteLanguage.English)
                all = all.Where(q => q.Lang == language);
            return all.ToList();
        }

        public static bool IsValid(Quote q) =>
            !string.IsNullOrWhiteSpace(q.Text)
            && q.Text.Trim().Length <= MaxTextLength
            && (q.Author == null || q.Author.Length <= MaxAuthorLength)
            && (q.Source == null || q.Source.Length <= MaxSourceLength)
            && (q.Lang == QuoteLanguage.Vietnamese || q.Lang == QuoteLanguage.English);

        /// <summary>So trùng chỉ theo nội dung, bỏ khoảng trắng thừa, không phân biệt hoa thường (không so tác giả, nguồn).</summary>
        public static string NormalizeForCompare(string text) =>
            string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

        private static List<Quote> LoadBuiltIn()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using var stream = asm.GetManifestResourceStream("default-quotes.json");
                if (stream == null)
                {
                    Log.Error("Không tìm thấy bộ quote mặc định trong file exe");
                    return new List<Quote>();
                }
                var list = JsonSerializer.Deserialize<List<Quote>>(stream, Json.Options) ?? new List<Quote>();
                var seen = new HashSet<string>();
                var result = new List<Quote>();
                foreach (var q in list)
                {
                    if (!IsValid(q)) continue;
                    if (!seen.Add(NormalizeForCompare(q.Text))) continue;
                    q.Text = q.Text.Trim();
                    result.Add(q);
                }
                return result;
            }
            catch (Exception ex)
            {
                Log.Error("Lỗi đọc bộ quote mặc định", ex);
                return new List<Quote>();
            }
        }
    }

    public sealed class ScanResult
    {
        public List<string> Ready { get; } = new List<string>();
        public int TooSmall { get; set; }
        public int Unreadable { get; set; }
        public List<string> MissingFolders { get; } = new List<string>();
    }

    /// <summary>Quét ảnh trong các thư mục (W1.1). Không sửa, di chuyển, xóa file gốc.</summary>
    public static class ImageScanner
    {
        public static readonly string[] Extensions = { ".jpg", ".jpeg", ".png", ".bmp", ".webp" };

        /// <param name="probeWidth">Hàm đọc chiều ngang ảnh (pixel); trả null nếu không đọc được.</param>
        public static ScanResult Scan(IEnumerable<string> folders, bool recursive, int minWidth, Func<string, int?> probeWidth)
        {
            var result = new ScanResult();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder))
                {
                    result.MissingFolders.Add(folder);
                    continue;
                }
                IEnumerable<string> files;
                try
                {
                    var options = new EnumerationOptions
                    {
                        RecurseSubdirectories = recursive,
                        IgnoreInaccessible = true,
                        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
                    };
                    files = Directory.EnumerateFiles(folder, "*", options);
                }
                catch (Exception ex)
                {
                    Log.Error($"Không đọc được thư mục {folder}", ex);
                    result.MissingFolders.Add(folder);
                    continue;
                }

                foreach (var file in files)
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    if (Array.IndexOf(Extensions, ext) < 0) continue;
                    var full = Path.GetFullPath(file);
                    if (!seen.Add(full)) continue; // một file chỉ tính 1 lần (BR W1.1)

                    var width = probeWidth(full);
                    if (width == null) { result.Unreadable++; continue; }
                    if (width.Value < minWidth) { result.TooSmall++; continue; }
                    result.Ready.Add(full);
                }
            }
            return result;
        }
    }
}
