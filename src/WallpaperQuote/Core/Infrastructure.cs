using System;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace WallpaperQuote.Core
{
    public static class Paths
    {
        public const string AppFolderName = "WallpaperQuote";

        /// <summary>%AppData%\WallpaperQuote — cấu hình, quote cá nhân.</summary>
        public static string DataDir => Ensure(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName));

        /// <summary>%LocalAppData%\WallpaperQuote — hình nền đã ghép, cache, log.</summary>
        public static string LocalDir => Ensure(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName));

        public static string SettingsFile => Path.Combine(DataDir, "settings.json");
        public static string UserQuotesFile => Path.Combine(DataDir, "quotes.json");
        public static string OnlineQuotesFile => Path.Combine(LocalDir, "quotes-online.json");
        public static string OnlineIndexFile => Path.Combine(LocalDir, "online-images.json");
        public static string OnlineCacheDir => Ensure(Path.Combine(LocalDir, "cache", "online"));
        public static string LinkCacheDir => Ensure(Path.Combine(LocalDir, "cache", "links"));
        public static string CacheRoot => Ensure(Path.Combine(LocalDir, "cache"));
        public static string LogFile => Path.Combine(LocalDir, "app.log");

        /// <summary>Luân phiên 2 file để Windows không dùng lại ảnh cũ trong bộ nhớ đệm.</summary>
        public static string WallpaperFile(int slot) => Path.Combine(LocalDir, $"wallpaper_{slot}.jpg");

        private static string Ensure(string dir)
        {
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Nhật ký lỗi cục bộ (G5). Không gửi đi đâu. Không ghi nội dung quote cá nhân.</summary>
    public static class Log
    {
        private const long MaxBytes = 1024 * 1024;
        private static readonly object Gate = new object();

        public static void Info(string message) => Write("INFO", message);
        public static void Warn(string message) => Write("WARN", message);
        public static void Error(string message, Exception? ex = null) =>
            Write("ERROR", ex == null ? message : $"{message} | {ex.GetType().Name}: {ex.Message}");

        private static void Write(string level, string message)
        {
            try
            {
                lock (Gate)
                {
                    var file = Paths.LogFile;
                    var info = new FileInfo(file);
                    if (info.Exists && info.Length > MaxBytes)
                    {
                        var old = file + ".1";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(file, old);
                    }
                    File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}", Encoding.UTF8);
                }
            }
            catch
            {
                // Ghi log không bao giờ được làm app dừng.
            }
        }
    }

    public static class Json
    {
        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
    }

    /// <summary>Đọc/ghi file JSON an toàn: ghi file tạm rồi thay thế, lỗi thì trả mặc định.</summary>
    public static class JsonFile
    {
        public static T? Read<T>(string file) where T : class
        {
            try
            {
                if (!File.Exists(file)) return null;
                return JsonSerializer.Deserialize<T>(File.ReadAllText(file, Encoding.UTF8), Json.Options);
            }
            catch (Exception ex)
            {
                Log.Error($"Không đọc được {Path.GetFileName(file)}", ex);
                try { File.Copy(file, file + ".broken", true); } catch { }
                return null;
            }
        }

        public static void Write<T>(string file, T value)
        {
            try
            {
                var temp = file + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(value, Json.Options), Encoding.UTF8);
                if (File.Exists(file)) File.Replace(temp, file, null);
                else File.Move(temp, file);
            }
            catch (Exception ex)
            {
                Log.Error($"Không lưu được {Path.GetFileName(file)}", ex);
            }
        }
    }

    public static class SettingsStore
    {
        public static AppSettings Load()
        {
            var file = Paths.SettingsFile;
            try
            {
                if (File.Exists(file))
                {
                    var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file, Encoding.UTF8), Json.Options);
                    if (settings != null)
                    {
                        settings.Normalize();
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                // Cấu hình hỏng → sao lưu file lỗi, dùng mặc định (BR G1).
                Log.Error("Không đọc được settings.json, dùng cấu hình mặc định", ex);
                try { File.Copy(file, file + ".broken", true); } catch { }
            }
            var fresh = new AppSettings();
            fresh.Normalize();
            return fresh;
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                var file = Paths.SettingsFile;
                var temp = file + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(settings, Json.Options), Encoding.UTF8);
                if (File.Exists(file)) File.Replace(temp, file, null);
                else File.Move(temp, file);
            }
            catch (Exception ex)
            {
                Log.Error("Không lưu được settings.json", ex);
            }
        }
    }
}
