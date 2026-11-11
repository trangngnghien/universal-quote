using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WallpaperQuote.Core;
using Forms = System.Windows.Forms;

namespace WallpaperQuote.UI
{
    /// <summary>Nhập nhiều quote cá nhân một lần: dán nhiều dòng, hoặc chọn file .txt / .csv.</summary>
    public sealed class BulkQuoteDialog : Window
    {
        private const string Sample =
            "Có công mài sắt, có ngày nên kim. | Tục ngữ\n" +
            "Học mà không nghĩ thì uổng công, nghĩ mà không học thì nguy hiểm. | Khổng Tử | Luận ngữ\n" +
            "Done is better than perfect.\n" +
            "Knowledge is power. | Francis Bacon | Meditationes Sacrae | en";

        private readonly AppHost _host;
        private bool _csv;

        private readonly TextBox _input = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            Height = 240,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(6),
            FontFamily = new FontFamily("Segoe UI")
        };
        private readonly RadioButton _auto = new RadioButton { Content = "Tự nhận theo dấu tiếng Việt", GroupName = "blang", IsChecked = true, Margin = new Thickness(0, 0, 20, 0) };
        private readonly RadioButton _vi = new RadioButton { Content = "Tất cả là tiếng Việt", GroupName = "blang", Margin = new Thickness(0, 0, 20, 0) };
        private readonly RadioButton _en = new RadioButton { Content = "Tất cả là tiếng Anh", GroupName = "blang" };
        private readonly TextBlock _lineCount = Ui.Text("0 dòng", 12, color: Ui.Muted, wrap: false);
        private readonly TextBlock _result = Ui.Text("", 13);

        public BulkQuoteDialog(AppHost host)
        {
            _host = host;
            Title = "Nhập nhiều quote";
            Width = 720;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;
            Background = Brushes.White;
            ShowInTaskbar = false;

            _input.TextChanged += (_, __) =>
            {
                var n = 0;
                foreach (var line in _input.Text.Split('\n')) if (line.Trim().Length > 0) n++;
                _lineCount.Text = $"{n} dòng";
            };

            var guide = Ui.Stack(
                Ui.Text("Mỗi dòng 1 quote, các phần cách nhau bởi dấu  |  (tác giả, nguồn, ngôn ngữ không bắt buộc):", 13),
                Ui.Text("Nội dung | Tác giả | Nguồn trích dẫn | vi hoặc en", 13, bold: true, color: Ui.Accent),
                Ui.Text("Có thể dán thẳng từ Excel/Google Sheets (các cột: Nội dung, Tác giả, Nguồn, Ngôn ngữ). Dòng bắt đầu bằng # được bỏ qua. Quote trùng nội dung tự bỏ qua.", 12, color: Ui.Muted));

            var buttons = Ui.Horizontal(
                Ui.Btn("Đóng", Close),
                Ui.Btn("Nhập quote", Import, primary: true));
            buttons.HorizontalAlignment = HorizontalAlignment.Right;

            Content = new Border
            {
                Padding = new Thickness(20),
                Child = Ui.Stack(
                    guide,
                    Ui.Spacer(10),
                    Ui.Row(Ui.Horizontal(
                            Ui.Btn("Chọn file .txt / .csv…", PickFile),
                            Ui.Btn("Điền ví dụ", () => { _input.Text = Sample; _csv = false; }, link: true)),
                        _lineCount),
                    Ui.Spacer(6),
                    _input,
                    Ui.Spacer(10),
                    Ui.Text("Ngôn ngữ khi dòng không ghi vi/en:", 13, bold: true),
                    Ui.Spacer(4),
                    Ui.Horizontal(_auto, _vi, _en),
                    Ui.Spacer(12),
                    Ui.Row(_result, buttons))
            };

            Loaded += (_, __) => _input.Focus();
        }

        private void PickFile()
        {
            using var dialog = new Forms.OpenFileDialog
            {
                Title = "Chọn file quote",
                Filter = "File quote (*.txt;*.csv)|*.txt;*.csv|Tất cả file (*.*)|*.*"
            };
            if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
            try
            {
                var bytes = File.ReadAllBytes(dialog.FileName);
                if (bytes.Length > 2 * 1024 * 1024)
                {
                    _result.Text = "File lớn hơn 2MB, hãy chia nhỏ.";
                    return;
                }
                _input.Text = Decode(bytes);
                _csv = string.Equals(Path.GetExtension(dialog.FileName), ".csv", StringComparison.OrdinalIgnoreCase);
                _result.Text = $"Đã mở {Path.GetFileName(dialog.FileName)}. Kiểm tra rồi bấm Nhập quote.";
                _result.Foreground = Ui.Muted;
            }
            catch (Exception ex)
            {
                _result.Text = "Không đọc được file: " + ex.Message;
                _result.Foreground = Ui.Bad;
            }
        }

        /// <summary>Đọc UTF-8 (có hoặc không BOM); nếu không phải UTF-8 hợp lệ thì thử bảng mã Windows-1258 (tiếng Việt).</summary>
        private static string Decode(byte[] bytes)
        {
            try
            {
                return new UTF8Encoding(false, true).GetString(bytes).TrimStart('﻿');
            }
            catch (DecoderFallbackException)
            {
                try
                {
                    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                    return Encoding.GetEncoding(1258).GetString(bytes);
                }
                catch
                {
                    return Encoding.Default.GetString(bytes);
                }
            }
        }

        private void Import()
        {
            if (_input.Text.Trim().Length == 0)
            {
                _result.Text = "Hãy dán quote hoặc chọn file trước.";
                _result.Foreground = Ui.Bad;
                return;
            }
            var lang = _vi.IsChecked == true ? QuoteLanguage.Vietnamese : _en.IsChecked == true ? QuoteLanguage.English : "auto";
            var r = _host.ImportUserQuotes(_input.Text, lang, _csv);

            var text = $"Đã thêm {r.Added} quote";
            if (r.Duplicates > 0) text += $" · bỏ qua {r.Duplicates} quote trùng";
            if (r.Invalid > 0) text += $" · {r.Invalid} dòng lỗi";
            if (r.InvalidLines.Count > 0) text += "\n" + string.Join("\n", r.InvalidLines);
            _result.Text = text;
            _result.Foreground = r.Added > 0 ? Ui.Ok : Ui.Bad;
            if (r.Added > 0 && r.Invalid == 0) _input.Text = "";
        }
    }
}
