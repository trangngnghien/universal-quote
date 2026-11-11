using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace WallpaperQuote.Core
{
    public sealed class BulkImportResult
    {
        public int Added { get; set; }
        public int Duplicates { get; set; }
        public int Invalid { get; set; }
        public List<string> InvalidLines { get; } = new List<string>();
    }

    public sealed class OnlineQuoteCatalog
    {
        public int Version { get; set; }
        public List<Quote> Quotes { get; set; } = new List<Quote>();
    }

    /// <summary>
    /// Thư viện quote (W2.1, W2.2): 3 nhóm Mặc định / Cá nhân / Online, lọc ngôn ngữ,
    /// thêm–sửa–xóa quote cá nhân, chống trùng theo nội dung.
    /// </summary>
    public sealed class QuoteLibrary
    {
        public const int MaxTextLength = 300;
        public const int MaxAuthorLength = 100;
        public const int MaxSourceLength = 100;

        private readonly List<Quote> _builtIn;
        private readonly List<Quote> _user;
        private OnlineQuoteCatalog _online;

        public QuoteLibrary()
        {
            _builtIn = LoadBuiltIn();
            _user = LoadUser();
            _online = JsonFile.Read<OnlineQuoteCatalog>(Paths.OnlineQuotesFile) ?? new OnlineQuoteCatalog();
            Tag(_online.Quotes, QuoteGroup.Online);
        }

        public IReadOnlyList<Quote> BuiltIn => _builtIn;
        public IReadOnlyList<Quote> User => _user;
        public IReadOnlyList<Quote> Online => _online.Quotes;
        public int OnlineVersion => _online.Version;

        public IReadOnlyList<Quote> All => _builtIn.Concat(_user).Concat(_online.Quotes).ToList();

        /// <summary>Quote được phép random theo nhóm đang bật và ngôn ngữ đã chọn.</summary>
        public IReadOnlyList<Quote> Active(AppSettings s)
        {
            var list = new List<Quote>();
            if (s.UseBuiltInQuotes) list.AddRange(_builtIn);
            if (s.UseUserQuotes) list.AddRange(_user);
            if (s.UseOnlineQuotes) list.AddRange(_online.Quotes);
            if (s.QuoteLanguage == QuoteLanguage.Vietnamese || s.QuoteLanguage == QuoteLanguage.English)
                list = list.Where(q => q.Lang == s.QuoteLanguage).ToList();
            return list;
        }

        /// <summary>Kiểm tra quote nhập tay. Trả về câu báo lỗi, hoặc null nếu hợp lệ.</summary>
        public string? Validate(string text, string? author, string? source, string? editingId)
        {
            var t = (text ?? "").Trim();
            if (t.Length == 0) return "Vui lòng nhập nội dung quote.";
            if (t.Length > MaxTextLength) return $"Nội dung tối đa {MaxTextLength} ký tự (hiện {t.Length}).";
            if ((author ?? "").Trim().Length > MaxAuthorLength) return $"Tác giả tối đa {MaxAuthorLength} ký tự.";
            var src = (source ?? "").Trim();
            if (src.Length > MaxSourceLength) return $"Nguồn trích dẫn tối đa {MaxSourceLength} ký tự.";
            if (src.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || src.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return "Nguồn trích dẫn nhập dạng chữ (tên sách, tác phẩm…), không nhập link.";
            var key = NormalizeForCompare(t);
            if (All.Any(q => q.Id != editingId && NormalizeForCompare(q.Text) == key))
                return "Quote này đã có trong thư viện.";
            return null;
        }

        public Quote AddUser(string text, string? author, string? source, string lang)
        {
            var q = new Quote
            {
                Id = Guid.NewGuid().ToString("N"),
                Text = text.Trim(),
                Author = Clean(author),
                Source = Clean(source),
                Lang = lang == QuoteLanguage.English ? QuoteLanguage.English : QuoteLanguage.Vietnamese,
                Group = QuoteGroup.User
            };
            _user.Add(q);
            SaveUser();
            return q;
        }

        /// <summary>
        /// Nhập nhiều quote cùng lúc. Mỗi dòng 1 quote: "Nội dung | Tác giả | Nguồn | vi/en"
        /// (dấu | hoặc Tab; các phần sau nội dung không bắt buộc). File .csv: các cột cách nhau bởi dấu phẩy.
        /// <paramref name="defaultLang"/>: "vi", "en" hoặc "auto" (tự nhận theo dấu tiếng Việt).
        /// </summary>
        public BulkImportResult AddUserBulk(string content, string defaultLang, bool csv)
        {
            var result = new BulkImportResult();
            var lines = (content ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var raw = lines[i].Trim();
                if (raw.Length == 0 || raw.StartsWith("#")) continue;

                var parts = SplitLine(raw, csv);
                if (i == 0 && IsHeader(parts[0])) continue;

                var text = StripQuotes(parts[0]);
                var author = parts.Count > 1 ? StripQuotes(parts[1]) : null;
                var source = parts.Count > 2 ? StripQuotes(parts[2]) : null;
                var lang = parts.Count > 3 ? parts[3].Trim().ToLowerInvariant() : "";
                if (lang != QuoteLanguage.Vietnamese && lang != QuoteLanguage.English)
                    lang = defaultLang == QuoteLanguage.Vietnamese || defaultLang == QuoteLanguage.English ? defaultLang : DetectLanguage(text);

                var error = Validate(text, author, source, null);
                if (error == "Quote này đã có trong thư viện.") { result.Duplicates++; continue; }
                if (error != null)
                {
                    result.Invalid++;
                    if (result.InvalidLines.Count < 5) result.InvalidLines.Add($"Dòng {i + 1}: {error}");
                    continue;
                }
                _user.Add(new Quote
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Text = text.Trim(),
                    Author = Clean(author),
                    Source = Clean(source),
                    Lang = lang,
                    Group = QuoteGroup.User
                });
                result.Added++;
            }
            if (result.Added > 0) SaveUser();
            return result;
        }

        private static List<string> SplitLine(string line, bool csv)
        {
            if (line.Contains('|')) return line.Split('|').Select(p => p.Trim()).ToList();
            if (line.Contains('\t')) return line.Split('\t').Select(p => p.Trim()).ToList();
            if (!csv) return new List<string> { line };

            // CSV đơn giản: phẩy hoặc chấm phẩy ngăn cột, hỗ trợ ô trong ngoặc kép.
            var sep = line.Contains(';') && !line.Contains(',') ? ';' : ',';
            var fields = new List<string>();
            var current = new System.Text.StringBuilder();
            var inQuotes = false;
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                    else inQuotes = !inQuotes;
                }
                else if (c == sep && !inQuotes)
                {
                    fields.Add(current.ToString().Trim());
                    current.Clear();
                }
                else current.Append(c);
            }
            fields.Add(current.ToString().Trim());
            return fields;
        }

        private static bool IsHeader(string first)
        {
            var f = StripQuotes(first).ToLowerInvariant();
            return f == "nội dung" || f == "noi dung" || f == "quote" || f == "text" || f == "content";
        }

        private static string StripQuotes(string s)
        {
            var t = (s ?? "").Trim();
            if (t.Length >= 2 && ((t[0] == '"' && t[t.Length - 1] == '"') || (t[0] == '“' && t[t.Length - 1] == '”')))
                t = t.Substring(1, t.Length - 2).Trim();
            return t;
        }

        private const string VietnameseLetters = "ăâđêôơưáàảãạắằẳẵặấầẩẫậéèẻẽẹếềểễệíìỉĩịóòỏõọốồổỗộớờởỡợúùủũụứừửữựýỳỷỹỵ";

        /// <summary>Có chữ cái tiếng Việt có dấu → "vi", ngược lại "en".</summary>
        public static string DetectLanguage(string text) =>
            (text ?? "").ToLowerInvariant().Any(c => VietnameseLetters.IndexOf(c) >= 0) ? QuoteLanguage.Vietnamese : QuoteLanguage.English;

        public void UpdateUser(string id, string text, string? author, string? source, string lang)
        {
            var q = _user.FirstOrDefault(x => x.Id == id);
            if (q == null) return;
            q.Text = text.Trim();
            q.Author = Clean(author);
            q.Source = Clean(source);
            q.Lang = lang == QuoteLanguage.English ? QuoteLanguage.English : QuoteLanguage.Vietnamese;
            SaveUser();
        }

        public void DeleteUser(string id)
        {
            if (_user.RemoveAll(x => x.Id == id) > 0) SaveUser();
        }

        /// <summary>Gộp danh mục quote online mới tải về (chỉ khi phiên bản mới hơn), loại trùng với quote đã có.</summary>
        public bool ApplyOnlineCatalog(OnlineQuoteCatalog catalog)
        {
            if (catalog.Version <= _online.Version && _online.Quotes.Count > 0) return false;
            var existing = new HashSet<string>(_builtIn.Concat(_user).Select(q => NormalizeForCompare(q.Text)));
            var clean = new List<Quote>();
            var i = 0;
            foreach (var q in catalog.Quotes ?? new List<Quote>())
            {
                if (!IsValid(q)) continue;
                if (!existing.Add(NormalizeForCompare(q.Text))) continue;
                q.Text = q.Text.Trim();
                if (string.IsNullOrWhiteSpace(q.Id)) q.Id = $"online-{catalog.Version}-{i}";
                i++;
                clean.Add(q);
            }
            Tag(clean, QuoteGroup.Online);
            _online = new OnlineQuoteCatalog { Version = catalog.Version, Quotes = clean };
            JsonFile.Write(Paths.OnlineQuotesFile, _online);
            return true;
        }

        public static bool IsValid(Quote q) =>
            !string.IsNullOrWhiteSpace(q.Text)
            && q.Text.Trim().Length <= MaxTextLength
            && (q.Author == null || q.Author.Length <= MaxAuthorLength)
            && (q.Source == null || q.Source.Length <= MaxSourceLength)
            && (q.Lang == QuoteLanguage.Vietnamese || q.Lang == QuoteLanguage.English);

        /// <summary>So trùng chỉ theo nội dung, bỏ khoảng trắng thừa, không phân biệt hoa thường (không so tác giả, nguồn).</summary>
        public static string NormalizeForCompare(string text) =>
            string.Join(" ", (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

        private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private static void Tag(IEnumerable<Quote> quotes, string group)
        {
            foreach (var q in quotes) q.Group = group;
        }

        private void SaveUser() => JsonFile.Write(Paths.UserQuotesFile, _user);

        private static List<Quote> LoadUser()
        {
            var list = JsonFile.Read<List<Quote>>(Paths.UserQuotesFile) ?? new List<Quote>();
            foreach (var q in list)
            {
                if (string.IsNullOrWhiteSpace(q.Id)) q.Id = Guid.NewGuid().ToString("N");
                q.Group = QuoteGroup.User;
            }
            return list.Where(q => !string.IsNullOrWhiteSpace(q.Text)).ToList();
        }

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
                var i = 0;
                foreach (var q in list)
                {
                    if (!IsValid(q)) continue;
                    if (!seen.Add(NormalizeForCompare(q.Text))) continue;
                    q.Text = q.Text.Trim();
                    q.Id = $"builtin-{i++}";
                    q.Group = QuoteGroup.BuiltIn;
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

    /// <summary>Danh sách ảnh online đã tải (W1.3) + dọn bộ nhớ đệm khi vượt giới hạn (G2).</summary>
    public sealed class OnlineImageStore
    {
        private List<OnlineImage> _items;

        public OnlineImageStore()
        {
            _items = JsonFile.Read<List<OnlineImage>>(Paths.OnlineIndexFile) ?? new List<OnlineImage>();
            _items = _items.Where(i => File.Exists(i.File)).ToList();
        }

        public IReadOnlyList<OnlineImage> Items => _items;

        public bool Contains(string id) => _items.Any(i => i.Id == id);

        public OnlineImage? FindByFile(string file) =>
            _items.FirstOrDefault(i => string.Equals(i.File, file, StringComparison.OrdinalIgnoreCase));

        public int Count(string source) => _items.Count(i => i.Source == source);

        public void Add(OnlineImage image)
        {
            _items.RemoveAll(i => i.Id == image.Id);
            _items.Add(image);
            Save();
        }

        public void Remove(string file)
        {
            if (_items.RemoveAll(i => string.Equals(i.File, file, StringComparison.OrdinalIgnoreCase)) > 0) Save();
        }

        /// <summary>Xóa ảnh tải về cũ nhất cho tới khi bộ nhớ đệm dưới giới hạn. Không xóa ảnh đang làm hình nền.</summary>
        public void Evict(long limitBytes, string? protectFile, IEnumerable<string> extraCacheFiles)
        {
            try
            {
                var files = new DirectoryInfo(Paths.CacheRoot).GetFiles("*", SearchOption.AllDirectories)
                    .OrderBy(f => f.LastWriteTimeUtc).ToList();
                var total = files.Sum(f => f.Length);
                var keep = new HashSet<string>(extraCacheFiles, StringComparer.OrdinalIgnoreCase);
                foreach (var f in files)
                {
                    if (total <= limitBytes) break;
                    if (string.Equals(f.FullName, protectFile, StringComparison.OrdinalIgnoreCase)) continue;
                    if (keep.Contains(f.FullName)) continue;   // ảnh link do người dùng thêm: giữ lại
                    total -= f.Length;
                    try { f.Delete(); } catch { }
                    Remove(f.FullName);
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Không dọn được bộ nhớ đệm: {ex.Message}");
            }
        }

        public void Clear(string? protectFile)
        {
            foreach (var item in _items.ToList())
            {
                if (string.Equals(item.File, protectFile, StringComparison.OrdinalIgnoreCase)) continue;
                try { File.Delete(item.File); } catch { }
                _items.Remove(item);
            }
            Save();
        }

        public static long CacheSizeBytes()
        {
            try
            {
                return new DirectoryInfo(Paths.CacheRoot).GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            }
            catch
            {
                return 0;
            }
        }

        private void Save() => JsonFile.Write(Paths.OnlineIndexFile, _items);
    }

    public sealed class ScanResult
    {
        public List<string> Ready { get; } = new List<string>();
        public int TooSmall { get; set; }
        public int Unreadable { get; set; }
        public int Heic { get; set; }
        public List<string> MissingFolders { get; } = new List<string>();
        public Dictionary<string, int> PerFolder { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Quét ảnh trong các thư mục (W1.1). Không sửa, di chuyển, xóa file gốc.</summary>
    public static class ImageScanner
    {
        public static readonly string[] Extensions =
            { ".jpg", ".jpeg", ".jfif", ".png", ".bmp", ".webp", ".gif", ".tif", ".tiff", ".heic", ".heif" };

        /// <param name="probeWidth">Hàm đọc chiều ngang ảnh (pixel); trả null nếu không đọc được.</param>
        public static ScanResult Scan(IEnumerable<string> folders, bool recursive, int minWidth, Func<string, int?> probeWidth)
        {
            var result = new ScanResult();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in folders)
            {
                result.PerFolder[folder] = 0;
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
                    if (width == null)
                    {
                        if (ext == ".heic" || ext == ".heif") result.Heic++;
                        else result.Unreadable++;
                        continue;
                    }
                    if (width.Value < minWidth) { result.TooSmall++; continue; }
                    result.Ready.Add(full);
                    result.PerFolder[folder] = result.PerFolder[folder] + 1;
                }
            }
            return result;
        }
    }
}
