using System;
using System.IO;
using System.Runtime.InteropServices;
using WallpaperQuote.Core;
using WallpaperQuote.Platform;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace WallpaperQuote
{
    /// <summary>
    /// Icon dưới khay hệ thống và menu (W6.1). Bản M1 dùng menu này thay cho cửa sổ Cài đặt (có ở M2).
    /// Nháy đúp icon = Làm mới tất cả.
    /// </summary>
    public sealed class TrayController : IDisposable
    {
        private const string AppTitle = "Hình nền & Quote";

        private static readonly (int Minutes, string Label)[] Intervals =
        {
            (5, "5 phút"), (15, "15 phút"), (30, "30 phút"), (60, "1 giờ"),
            (180, "3 giờ"), (360, "6 giờ"), (720, "12 giờ"), (1440, "24 giờ")
        };

        private readonly AppHost _host;
        private readonly Forms.NotifyIcon _icon;
        private readonly Forms.ContextMenuStrip _menu;
        private readonly Forms.ToolStripMenuItem _status;
        private readonly Forms.ToolStripMenuItem _pause;
        private readonly Forms.ToolStripMenuItem _folders;
        private readonly Forms.ToolStripMenuItem _imageInterval;
        private readonly Forms.ToolStripMenuItem _quoteInterval;
        private readonly Forms.ToolStripMenuItem _language;
        private readonly Forms.ToolStripMenuItem _fit;
        private readonly Forms.ToolStripMenuItem _startup;
        private readonly Forms.ToolStripMenuItem _changeOnStartup;
        private Drawing.Icon? _runningIcon;
        private Drawing.Icon? _pausedIcon;

        public TrayController(AppHost host)
        {
            _host = host;
            _runningIcon = MakeIcon(paused: false);
            _pausedIcon = MakeIcon(paused: true);

            _menu = new Forms.ContextMenuStrip();

            _status = new Forms.ToolStripMenuItem(AppTitle) { Enabled = false };
            _menu.Items.Add(_status);
            _menu.Items.Add(new Forms.ToolStripSeparator());

            var refresh = new Forms.ToolStripMenuItem("Làm mới");
            refresh.DropDownItems.Add(Item("Làm mới tất cả", () => _ = _host.ChangeAsync(Change.All, auto: false)));
            refresh.DropDownItems.Add(Item("Chỉ đổi ảnh", () => _ = _host.ChangeAsync(Change.Image, auto: false)));
            refresh.DropDownItems.Add(Item("Chỉ đổi quote", () => _ = _host.ChangeAsync(Change.Quote, auto: false)));
            _menu.Items.Add(refresh);

            _pause = Item("Tạm dừng", () => _host.SetPaused(!_host.IsPaused));
            _menu.Items.Add(_pause);
            _menu.Items.Add(Item("Thông tin ảnh hiện tại", ShowCurrentInfo));
            _menu.Items.Add(new Forms.ToolStripSeparator());

            _folders = new Forms.ToolStripMenuItem("Thư mục ảnh");
            _menu.Items.Add(_folders);

            _imageInterval = new Forms.ToolStripMenuItem("Đổi ảnh mỗi");
            _quoteInterval = new Forms.ToolStripMenuItem("Đổi quote mỗi");
            foreach (var (minutes, label) in Intervals)
            {
                var m = minutes;
                _imageInterval.DropDownItems.Add(Item(label, () => _host.SetImageInterval(m), tag: m));
                _quoteInterval.DropDownItems.Add(Item(label, () => _host.SetQuoteInterval(m), tag: m));
            }
            _menu.Items.Add(_imageInterval);
            _menu.Items.Add(_quoteInterval);

            _language = new Forms.ToolStripMenuItem("Ngôn ngữ quote");
            _language.DropDownItems.Add(Item("Tiếng Việt", () => _host.SetQuoteLanguage(QuoteLanguage.Vietnamese), tag: QuoteLanguage.Vietnamese));
            _language.DropDownItems.Add(Item("Tiếng Anh", () => _host.SetQuoteLanguage(QuoteLanguage.English), tag: QuoteLanguage.English));
            _language.DropDownItems.Add(Item("Cả hai", () => _host.SetQuoteLanguage(QuoteLanguage.All), tag: QuoteLanguage.All));
            _menu.Items.Add(_language);

            _fit = new Forms.ToolStripMenuItem("Ảnh khác tỉ lệ màn hình");
            _fit.DropDownItems.Add(Item("Lấp đầy (cắt bớt mép)", () => _ = _host.SetFitModeAsync(FitMode.Fill), tag: FitMode.Fill));
            _fit.DropDownItems.Add(Item("Vừa khung (có viền)", () => _ = _host.SetFitModeAsync(FitMode.Fit), tag: FitMode.Fit));
            _fit.DropDownItems.Add(Item("Kéo giãn", () => _ = _host.SetFitModeAsync(FitMode.Stretch), tag: FitMode.Stretch));
            _menu.Items.Add(_fit);

            _changeOnStartup = Item("Đổi ảnh và quote khi mở app", () => _host.SetChangeOnStartup(!_host.Settings.ChangeOnStartup));
            _menu.Items.Add(_changeOnStartup);
            _startup = Item("Khởi động cùng Windows", ToggleStartup);
            _menu.Items.Add(_startup);
            _menu.Items.Add(new Forms.ToolStripSeparator());

            _menu.Items.Add(Item("Mở thư mục nhật ký và hình nền", () => Shell.OpenFolder(Paths.LocalDir)));
            _menu.Items.Add(Item("Thoát", () => _host.Exit()));

            _menu.Opening += (_, __) => Refresh();

            _icon = new Forms.NotifyIcon
            {
                Icon = _runningIcon,
                Text = AppTitle,
                ContextMenuStrip = _menu,
                Visible = false
            };
            _icon.DoubleClick += (_, __) => _ = _host.ChangeAsync(Change.All, auto: false);
        }

        public void Show()
        {
            Refresh();
            _icon.Visible = true;
        }

        public void Notify(string title, string text, bool warning = false)
        {
            try
            {
                _icon.ShowBalloonTip(5000, title, text, warning ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                Log.Warn($"Không hiện được thông báo: {ex.Message}");
            }
        }

        /// <summary>Cập nhật trạng thái, dấu tick và danh sách thư mục.</summary>
        public void Refresh()
        {
            var s = _host.Settings;
            var paused = _host.IsPaused;

            _status.Text = paused
                ? $"Tạm dừng · {_host.ImageCount} ảnh · {_host.QuoteCount} quote"
                : $"Đang chạy · {_host.ImageCount} ảnh · {_host.QuoteCount} quote";
            _pause.Text = paused ? "Tiếp tục" : "Tạm dừng";

            var tooltip = paused ? $"{AppTitle} · Tạm dừng" : $"{AppTitle} · Nháy đúp để làm mới";
            _icon.Text = tooltip.Length > 120 ? tooltip.Substring(0, 120) : tooltip;
            _icon.Icon = paused ? _pausedIcon : _runningIcon;

            Check(_imageInterval, s.ImageIntervalMinutes);
            Check(_quoteInterval, s.QuoteIntervalMinutes);
            Check(_language, s.QuoteLanguage);
            Check(_fit, s.FitMode);
            _changeOnStartup.Checked = s.ChangeOnStartup;
            _startup.Checked = StartupRegistration.IsEnabled();

            RebuildFolderMenu();
        }

        private void RebuildFolderMenu()
        {
            var s = _host.Settings;
            _folders.DropDownItems.Clear();

            var missing = _host.LastScan?.MissingFolders;
            foreach (var folder in s.Folders)
            {
                var path = folder;
                var unavailable = missing != null && missing.Exists(m => string.Equals(m, path, StringComparison.OrdinalIgnoreCase));
                var label = unavailable ? $"{path}  (không khả dụng)" : path;
                var item = new Forms.ToolStripMenuItem(label);
                item.DropDownItems.Add(Item("Gỡ thư mục này", () => ConfirmRemoveFolder(path)));
                _folders.DropDownItems.Add(item);
            }
            if (s.Folders.Count == 0)
                _folders.DropDownItems.Add(new Forms.ToolStripMenuItem("(Chưa có thư mục nào)") { Enabled = false });

            _folders.DropDownItems.Add(new Forms.ToolStripSeparator());
            _folders.DropDownItems.Add(Item("Thêm thư mục…", PickFolder));
            var sub = Item("Quét cả thư mục con", () => _ = _host.SetScanSubfoldersAsync(!s.ScanSubfolders));
            sub.Checked = s.ScanSubfolders;
            _folders.DropDownItems.Add(sub);
            _folders.DropDownItems.Add(Item("Quét lại", () => _ = _host.RescanAsync()));

            var scan = _host.LastScan;
            if (scan != null && (scan.TooSmall > 0 || scan.Unreadable > 0))
            {
                _folders.DropDownItems.Add(new Forms.ToolStripSeparator());
                _folders.DropDownItems.Add(new Forms.ToolStripMenuItem(
                    $"Bỏ qua {scan.TooSmall} ảnh nhỏ hơn {s.MinImageWidth}px, {scan.Unreadable} ảnh lỗi")
                { Enabled = false });
            }
        }

        private void PickFolder()
        {
            using var dialog = new Forms.FolderBrowserDialog
            {
                Description = "Chọn thư mục chứa ảnh làm hình nền",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false
            };
            if (dialog.ShowDialog() == Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
                _ = _host.AddFolderAsync(dialog.SelectedPath);
        }

        private void ConfirmRemoveFolder(string folder)
        {
            var answer = Forms.MessageBox.Show(
                $"Gỡ thư mục này khỏi app?\n\n{folder}\n\nẢnh trong thư mục vẫn giữ nguyên trên máy.",
                AppTitle, Forms.MessageBoxButtons.YesNo, Forms.MessageBoxIcon.Question);
            if (answer == Forms.DialogResult.Yes)
                _ = _host.RemoveFolderAsync(folder);
        }

        private void ToggleStartup()
        {
            var target = !StartupRegistration.IsEnabled();
            if (!StartupRegistration.Set(target))
                Notify("Không đổi được cài đặt khởi động", "Windows không cho ghi cài đặt. Trạng thái giữ như cũ.", warning: true);
            Refresh();
        }

        private void ShowCurrentInfo()
        {
            var image = _host.CurrentImage;
            var quote = _host.CurrentQuote;
            var imageText = image == null ? "Chưa có ảnh (đang dùng nền tối)" : $"{Path.GetFileName(image)}\n{Path.GetDirectoryName(image)}";
            var quoteText = "Không có";
            if (quote != null)
            {
                var author = string.IsNullOrWhiteSpace(quote.Author) ? "Không có tác giả" : quote.Author;
                var source = string.IsNullOrWhiteSpace(quote.Source) ? "Không có" : quote.Source;
                quoteText = $"{quote.Text}\nTác giả: {author}\nNguồn quote: {source}";
            }
            var next = _host.NextImageChange;
            var nextText = next == null ? (_host.IsPaused ? "Đang tạm dừng" : "—") : next.Value.ToString("HH:mm");

            Forms.MessageBox.Show(
                $"Ảnh:\n{imageText}\n\nQuote:\n{quoteText}\n\nĐổi ảnh tiếp theo lúc: {nextText}",
                "Thông tin ảnh hiện tại", Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Information);
        }

        private static Forms.ToolStripMenuItem Item(string text, Action onClick, object? tag = null)
        {
            var item = new Forms.ToolStripMenuItem(text) { Tag = tag };
            item.Click += (_, __) =>
            {
                try { onClick(); }
                catch (Exception ex) { Log.Error($"Lỗi khi chọn menu \"{text}\"", ex); }
            };
            return item;
        }

        private static void Check(Forms.ToolStripMenuItem parent, object value)
        {
            foreach (var child in parent.DropDownItems)
            {
                if (child is Forms.ToolStripMenuItem item)
                    item.Checked = item.Tag != null && item.Tag.Equals(value);
            }
        }

        // Icon vẽ bằng code để không cần file .ico riêng.
        private static Drawing.Icon MakeIcon(bool paused)
        {
            using var bmp = new Drawing.Bitmap(32, 32);
            using (var g = Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Drawing.Color.Transparent);
                var fill = paused ? Drawing.Color.FromArgb(107, 114, 128) : Drawing.Color.FromArgb(37, 99, 235);
                using var brush = new Drawing.SolidBrush(fill);
                using var path = RoundedRect(new Drawing.Rectangle(2, 4, 28, 24), 5);
                g.FillPath(brush, path);
                using var white = new Drawing.SolidBrush(Drawing.Color.White);
                g.FillEllipse(white, 20, 8, 6, 6);                 // mặt trời
                g.FillRectangle(white, 7, 19, 14, 3);             // dòng quote
                g.FillRectangle(white, 7, 23, 9, 2);
            }
            var handle = bmp.GetHicon();
            try
            {
                using var temp = Drawing.Icon.FromHandle(handle);
                return (Drawing.Icon)temp.Clone();
            }
            finally
            {
                DestroyIcon(handle);
            }
        }

        private static Drawing.Drawing2D.GraphicsPath RoundedRect(Drawing.Rectangle r, int radius)
        {
            var d = radius * 2;
            var path = new Drawing.Drawing2D.GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
            _menu.Dispose();
            _runningIcon?.Dispose();
            _pausedIcon?.Dispose();
            _runningIcon = null;
            _pausedIcon = null;
        }
    }
}
