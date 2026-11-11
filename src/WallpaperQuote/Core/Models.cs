using System.Collections.Generic;

namespace WallpaperQuote.Core
{
    /// <summary>Một câu quote. Tác giả và nguồn trích dẫn không bắt buộc (BR W2.1).</summary>
    public sealed class Quote
    {
        public string Text { get; set; } = "";
        public string? Author { get; set; }
        public string? Source { get; set; }
        /// <summary>"vi" hoặc "en".</summary>
        public string Lang { get; set; } = "vi";

        /// <summary>Dòng ghi nguồn: "— Tác giả, Nguồn". Null nếu không có cả hai.</summary>
        public string? AttributionLine(out int sourceStart, out int sourceLength)
        {
            sourceStart = -1;
            sourceLength = 0;
            var author = string.IsNullOrWhiteSpace(Author) ? null : Author!.Trim();
            var source = string.IsNullOrWhiteSpace(Source) ? null : Source!.Trim();
            if (author == null && source == null) return null;

            var line = "— ";
            if (author != null) line += author;
            if (source != null)
            {
                if (author != null) line += ", ";
                sourceStart = line.Length;
                sourceLength = source.Length;
                line += source;
            }
            return line;
        }
    }

    public static class QuoteLanguage
    {
        public const string Vietnamese = "vi";
        public const string English = "en";
        public const string All = "all";
    }

    public static class FitMode
    {
        public const string Fill = "fill";
        public const string Fit = "fit";
        public const string Stretch = "stretch";
    }

    /// <summary>Cấu hình lưu tại %AppData%\WallpaperQuote\settings.json.</summary>
    public sealed class AppSettings
    {
        public List<string> Folders { get; set; } = new List<string>();
        public bool ScanSubfolders { get; set; } = true;

        public int ImageIntervalMinutes { get; set; } = 60;
        public int QuoteIntervalMinutes { get; set; } = 15;
        public bool ChangeOnStartup { get; set; } = true;
        public bool Paused { get; set; }

        public string QuoteLanguage { get; set; } = Core.QuoteLanguage.All;
        public string FitMode { get; set; } = Core.FitMode.Fill;
        public int MinImageWidth { get; set; } = 1280;

        /// <summary>Hình nền của người dùng trước lần chạy đầu, để khôi phục khi gỡ cài.</summary>
        public string? OriginalWallpaper { get; set; }
        public bool OriginalWallpaperSaved { get; set; }

        /// <summary>Đã hiện thông báo chào mừng lần chạy đầu.</summary>
        public bool WelcomeShown { get; set; }

        public static readonly int[] AllowedIntervals = { 5, 15, 30, 60, 180, 360, 720, 1440 };

        public void Normalize()
        {
            Folders ??= new List<string>();
            if (System.Array.IndexOf(AllowedIntervals, ImageIntervalMinutes) < 0) ImageIntervalMinutes = 60;
            if (System.Array.IndexOf(AllowedIntervals, QuoteIntervalMinutes) < 0) QuoteIntervalMinutes = 15;
            if (QuoteLanguage != Core.QuoteLanguage.Vietnamese && QuoteLanguage != Core.QuoteLanguage.English)
                QuoteLanguage = Core.QuoteLanguage.All;
            if (FitMode != Core.FitMode.Fit && FitMode != Core.FitMode.Stretch) FitMode = Core.FitMode.Fill;
            if (MinImageWidth < 0) MinImageWidth = 1280;
        }
    }
}
