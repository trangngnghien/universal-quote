using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WallpaperQuote.Core;

namespace WallpaperQuote.Platform
{
    public sealed class ImageDecodeException : Exception
    {
        public ImageDecodeException(string path, Exception? inner = null)
            : base($"Không giải mã được ảnh: {path}", inner)
        {
            ImagePath = path;
        }

        public string ImagePath { get; }
    }

    /// <summary>Đọc kích thước ảnh từ phần đầu file, không giải mã cả ảnh.</summary>
    public static class ImageProbe
    {
        public static int? Width(string path) => Size(path)?.Width;

        public static (int Width, int Height)? Size(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var decoder = BitmapDecoder.Create(fs,
                    BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile,
                    BitmapCacheOption.None);
                var frame = decoder.Frames[0];
                return (frame.PixelWidth, frame.PixelHeight);
            }
            catch
            {
                return null;
            }
        }
    }

    public sealed class RenderRequest
    {
        public RenderRequest(string? imagePath, Quote? quote, int width, int height, string fitMode, double? posX = null, double? posY = null)
        {
            PosX = posX;
            PosY = posY;
            ImagePath = imagePath;
            Quote = quote;
            Width = width;
            Height = height;
            FitMode = fitMode;
        }

        public string? ImagePath { get; }
        public Quote? Quote { get; }
        public int Width { get; }
        public int Height { get; }
        public string FitMode { get; }
        public double? PosX { get; }
        public double? PosY { get; }
    }

    /// <summary>
    /// Ghép quote lên ảnh (W4.1): resize theo độ phân giải màn hình, vẽ quote góc dưới phải,
    /// chữ trắng trên nền tối mờ, rồi lưu thành file JPEG. Không sửa file ảnh gốc.
    /// </summary>
    public static class WallpaperRenderer
    {
        private static readonly Color BoxColor = Color.FromArgb(148, 15, 23, 42);      // ~58% đục
        private static readonly Color AttributionColor = Color.FromArgb(225, 255, 255, 255);

        public static void RenderToFile(RenderRequest request, string outputFile)
        {
            var w = request.Width;
            var h = request.Height;
            var full = new Rect(0, 0, w, h);

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                // Nền dự phòng khi chưa có ảnh, và là màu viền khi kiểu khớp = Vừa khung.
                var background = new LinearGradientBrush(Color.FromRgb(0x0F, 0x17, 0x2A), Color.FromRgb(0x1E, 0x1B, 0x4B), 45);
                background.Freeze();
                dc.DrawRectangle(background, null, full);

                if (request.ImagePath != null)
                {
                    var image = LoadScaled(request.ImagePath, w, h, request.FitMode, out var dest);
                    dc.PushClip(new RectangleGeometry(full));
                    dc.DrawImage(image, dest);
                    dc.Pop();
                }

                if (request.Quote != null)
                    DrawQuote(dc, request.Quote, w, h, request.PosX, request.PosY);
            }

            var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);

            var encoder = new JpegBitmapEncoder { QualityLevel = 92 };
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var temp = outputFile + ".tmp";
            using (var fs = File.Create(temp))
                encoder.Save(fs);
            File.Copy(temp, outputFile, overwrite: true);
            File.Delete(temp);
        }

        private static BitmapSource LoadScaled(string path, int w, int h, string fitMode, out Rect dest)
        {
            var size = ImageProbe.Size(path) ?? throw new ImageDecodeException(path);
            double iw = size.Width, ih = size.Height;
            if (iw <= 0 || ih <= 0) throw new ImageDecodeException(path);

            int decodeWidth;
            if (fitMode == FitMode.Stretch)
            {
                dest = new Rect(0, 0, w, h);
                decodeWidth = w;
            }
            else
            {
                var scale = fitMode == FitMode.Fit
                    ? Math.Min(w / iw, h / ih)
                    : Math.Max(w / iw, h / ih);          // Fill: cắt bớt mép
                var dw = iw * scale;
                var dh = ih * scale;
                dest = new Rect((w - dw) / 2, (h - dh) / 2, dw, dh);
                decodeWidth = (int)Math.Ceiling(dw);
            }

            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bmp.StreamSource = fs;
                if (decodeWidth > 0 && decodeWidth < iw) bmp.DecodePixelWidth = decodeWidth;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch (Exception ex)
            {
                throw new ImageDecodeException(path, ex);
            }
        }

        private static void DrawQuote(DrawingContext dc, Quote quote, int w, int h, double? posX, double? posY)
        {
            // Kích thước tính theo chiều cao màn hình, chuẩn 1080p.
            var k = h / 1080.0;
            var padX = 26 * k;
            var padY = 20 * k;
            var marginRight = 56 * k;
            var marginBottom = 110 * k;                 // nằm trên thanh taskbar
            var maxBoxWidth = Math.Min(w * 0.42, 900 * k);
            var maxTextWidth = maxBoxWidth - 2 * padX;

            var culture = CultureInfo.GetCultureInfo(quote.Lang == QuoteLanguage.English ? "en-US" : "vi-VN");
            var serif = new Typeface(new FontFamily("Cambria, Georgia, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            var sans = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

            // Quote dài: quá 3 dòng thì thu nhỏ chữ tới cỡ tối thiểu; vẫn dài thì cắt và thêm "…" (BR W4.1).
            var text = "“" + quote.Text + "”";
            var size = 30 * k;
            var minSize = 20 * k;
            FormattedText body;
            while (true)
            {
                body = MakeText(text, culture, serif, size, Brushes.White, maxTextWidth);
                if (CountLines(body) <= 3 || size <= minSize) break;
                size = Math.Max(minSize, size - 2 * k);
            }
            if (CountLines(body) > 3)
            {
                body.MaxLineCount = 3;
                body.Trimming = TextTrimming.CharacterEllipsis;
            }

            // Dòng ghi nguồn: "— Tác giả, Nguồn" (nguồn in nghiêng).
            FormattedText? attribution = null;
            var line = quote.AttributionLine(out var sourceStart, out var sourceLength);
            if (line != null)
            {
                var brush = new SolidColorBrush(AttributionColor);
                brush.Freeze();
                attribution = MakeText(line, culture, sans, Math.Max(13, 16 * k), brush, maxTextWidth);
                attribution.MaxLineCount = 1;
                attribution.Trimming = TextTrimming.CharacterEllipsis;
                if (sourceStart >= 0 && sourceLength > 0)
                    attribution.SetFontStyle(FontStyles.Italic, sourceStart, sourceLength);
            }

            var gap = 10 * k;
            var contentWidth = Math.Max(body.Width, attribution?.Width ?? 0);
            var boxWidth = Math.Min(maxBoxWidth, contentWidth + 2 * padX);
            var boxHeight = padY + body.Height + (attribution != null ? gap + attribution.Height : 0) + padY;
            double x, y;
            if (posX.HasValue && posY.HasValue)
            {
                // Vị trí người dùng kéo: tâm khung đặt tại điểm đã lưu, giữ khung nằm trọn trong màn hình.
                var edge = 16 * k;
                x = Math.Min(Math.Max(posX.Value * w - boxWidth / 2, edge), Math.Max(edge, w - edge - boxWidth));
                y = Math.Min(Math.Max(posY.Value * h - boxHeight / 2, edge), Math.Max(edge, h - edge - boxHeight));
            }
            else
            {
                x = w - marginRight - boxWidth;
                y = h - marginBottom - boxHeight;
            }

            var boxBrush = new SolidColorBrush(BoxColor);
            boxBrush.Freeze();
            dc.DrawRoundedRectangle(boxBrush, null, new Rect(x, y, boxWidth, boxHeight), 12 * k, 12 * k);
            dc.DrawText(body, new Point(x + padX, y + padY));
            if (attribution != null)
                dc.DrawText(attribution, new Point(x + padX, y + padY + body.Height + gap));
        }

        private static FormattedText MakeText(string text, CultureInfo culture, Typeface typeface, double size, Brush brush, double maxWidth)
        {
            var ft = new FormattedText(text, culture, FlowDirection.LeftToRight, typeface, size, brush, 1.0)
            {
                MaxTextWidth = Math.Max(1, maxWidth),
                LineHeight = size * 1.4,
                TextAlignment = TextAlignment.Left
            };
            return ft;
        }

        private static int CountLines(FormattedText ft) =>
            Math.Max(1, (int)Math.Round(ft.Height / ft.LineHeight));
    }
}
