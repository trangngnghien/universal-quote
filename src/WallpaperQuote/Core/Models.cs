using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WallpaperQuote.Core
{
    public static class QuoteGroup
    {
        public const string BuiltIn = "builtin";
        public const string User = "user";
        public const string Online = "online";
    }

    /// <summary>Một câu quote. Tác giả và nguồn trích dẫn không bắt buộc (BR W2.1).</summary>
    public sealed class Quote
    {
        public string Id { get; set; } = "";
        public string Text { get; set; } = "";
        public string? Author { get; set; }
        public string? Source { get; set; }
        /// <summary>"vi" hoặc "en".</summary>
        public string Lang { get; set; } = "vi";

        /// <summary>Nhóm quote: mặc định / cá nhân / online. Gán khi nạp, không lưu.</summary>
        [JsonIgnore]
        public string Group { get; set; } = QuoteGroup.BuiltIn;

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

    public static class EffectType
    {
        public const string None = "none";
        public const string Snow = "snow";
        public const string Rain = "rain";
        public const string Petals = "petals";
    }

    public static class EffectIntensity
    {
        public const string Light = "light";
        public const string Medium = "medium";
        public const string Heavy = "heavy";
    }

    public static class LinkStatus
    {
        public const string Pending = "pending";   // Chờ tải (offline / đang tải)
        public const string Ready = "ready";       // Sẵn sàng
        public const string Error = "error";       // Lỗi
    }

    /// <summary>Ảnh qua đường link (W1.2).</summary>
    public sealed class ImageLink
    {
        public string Url { get; set; } = "";
        public string Status { get; set; } = LinkStatus.Pending;
        public string? Error { get; set; }
        public string? File { get; set; }
        public int FailCount { get; set; }
        public DateTime AddedAt { get; set; } = DateTime.Now;
    }

    public static class OnlineSource
    {
        public const string Nasa = "nasa";
        public const string Wikimedia = "wikimedia";
    }

    /// <summary>Ảnh online đã tải về bộ nhớ đệm, kèm thông tin giấy phép (W1.3, W5.2).</summary>
    public sealed class OnlineImage
    {
        public string Id { get; set; } = "";
        public string Source { get; set; } = "";
        public string File { get; set; } = "";
        public string Title { get; set; } = "";
        public string? Author { get; set; }
        public string License { get; set; } = "";
        public string? PageUrl { get; set; }
        public string? Date { get; set; }
        public DateTime DownloadedAt { get; set; } = DateTime.Now;
    }

    /// <summary>Cấu hình lưu tại %AppData%\WallpaperQuote\settings.json.</summary>
    public sealed class AppSettings
    {
        public const int CurrentVersion = 2;
        public int SettingsVersion { get; set; }

        // Ảnh
        public List<string> Folders { get; set; } = new List<string>();
        public bool ScanSubfolders { get; set; } = true;
        public List<ImageLink> Links { get; set; } = new List<ImageLink>();
        public bool NasaEnabled { get; set; } = true;
        public string NasaApiKey { get; set; } = "DEMO_KEY";
        public bool WikimediaEnabled { get; set; } = true;
        public DateTime? LastOnlineSync { get; set; }
        public string? LastOnlineSyncMessage { get; set; }
        public int MinImageWidth { get; set; } = 800;
        public int CacheLimitMb { get; set; } = 500;

        // Quote
        public string QuoteLanguage { get; set; } = Core.QuoteLanguage.All;
        public bool UseBuiltInQuotes { get; set; } = true;
        public bool UseUserQuotes { get; set; } = true;
        public bool UseOnlineQuotes { get; set; } = true;
        public DateTime? LastQuoteSync { get; set; }

        // Lịch
        public int ImageIntervalMinutes { get; set; } = 60;
        public int QuoteIntervalMinutes { get; set; } = 15;
        public bool ChangeOnStartup { get; set; } = true;
        public bool Paused { get; set; }

        // Hiển thị & hệ thống
        public string FitMode { get; set; } = Core.FitMode.Fill;
        /// <summary>Tâm khung quote theo tỉ lệ màn hình (0–1). Null = mặc định góc dưới phải.</summary>
        public double? QuotePosX { get; set; }
        public double? QuotePosY { get; set; }

        // Hiệu ứng động (W7)
        public string Effect { get; set; } = EffectType.None;
        public string EffectIntensity { get; set; } = Core.EffectIntensity.Medium;
        public bool EffectPauseOnBattery { get; set; } = true;
        public string? OriginalWallpaper { get; set; }
        public bool OriginalWallpaperSaved { get; set; }
        public bool WelcomeShown { get; set; }
        public bool DesktopShortcutCreated { get; set; }
        public bool DesktopIconsChecked { get; set; }

        public static readonly int[] AllowedIntervals = { 5, 15, 30, 60, 180, 360, 720, 1440 };

        public void Normalize()
        {
            Folders ??= new List<string>();
            Links ??= new List<ImageLink>();
            if (Array.IndexOf(AllowedIntervals, ImageIntervalMinutes) < 0) ImageIntervalMinutes = 60;
            if (Array.IndexOf(AllowedIntervals, QuoteIntervalMinutes) < 0) QuoteIntervalMinutes = 15;
            if (QuoteLanguage != Core.QuoteLanguage.Vietnamese && QuoteLanguage != Core.QuoteLanguage.English)
                QuoteLanguage = Core.QuoteLanguage.All;
            if (FitMode != Core.FitMode.Fit && FitMode != Core.FitMode.Stretch) FitMode = Core.FitMode.Fill;
            if (string.IsNullOrWhiteSpace(NasaApiKey)) NasaApiKey = "DEMO_KEY";
            if (CacheLimitMb < 50) CacheLimitMb = 500;
            if (Effect != EffectType.Snow && Effect != EffectType.Rain && Effect != EffectType.Petals) Effect = EffectType.None;
            if (EffectIntensity != Core.EffectIntensity.Light && EffectIntensity != Core.EffectIntensity.Heavy) EffectIntensity = Core.EffectIntensity.Medium;
            if (QuotePosX.HasValue && (QuotePosX < 0 || QuotePosX > 1)) QuotePosX = null;
            if (QuotePosY.HasValue && (QuotePosY < 0 || QuotePosY > 1)) QuotePosY = null;
            if (!QuotePosX.HasValue || !QuotePosY.HasValue) { QuotePosX = null; QuotePosY = null; }

            // Nâng cấp từ bản M1: hạ ngưỡng ảnh tối thiểu 1280px → 800px.
            if (SettingsVersion < 2)
            {
                MinImageWidth = 800;
                SettingsVersion = 2;
            }
            if (MinImageWidth < 0) MinImageWidth = 800;
        }
    }
}
