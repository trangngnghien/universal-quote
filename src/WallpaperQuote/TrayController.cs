using System;
using System.Runtime.InteropServices;
using WallpaperQuote.Core;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace WallpaperQuote
{
    /// <summary>
    /// Icon dưới khay hệ thống (W6.1): Làm mới ▸ / Tạm dừng / Thông tin ảnh / Cài đặt / Thoát.
    /// Nháy đúp icon = Làm mới tất cả.
    /// </summary>
    public sealed class TrayController : IDisposable
    {
        private const string AppTitle = "Hình nền & Quote";

        private readonly AppHost _host;
        private readonly Forms.NotifyIcon _icon;
        private readonly Forms.ContextMenuStrip _menu;
        private readonly Forms.ToolStripMenuItem _status;
        private readonly Forms.ToolStripMenuItem _pause;
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
            _menu.Items.Add(Item("Cài đặt…", () => _host.OpenSettings()));
            _menu.Items.Add(new Forms.ToolStripSeparator());
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
            _icon.BalloonTipClicked += (_, __) => _host.OpenSettings();
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

        public void Refresh()
        {
            var paused = _host.IsPaused;
            _status.Text = paused
                ? $"Tạm dừng · {_host.ImageCount} ảnh · {_host.QuoteCount} quote"
                : $"Đang chạy · {_host.ImageCount} ảnh · {_host.QuoteCount} quote";
            _pause.Text = paused ? "Tiếp tục" : "Tạm dừng";
            var tooltip = paused ? $"{AppTitle} · Tạm dừng" : $"{AppTitle} · Nháy đúp để làm mới";
            _icon.Text = tooltip.Length > 120 ? tooltip.Substring(0, 120) : tooltip;
            _icon.Icon = paused ? _pausedIcon : _runningIcon;
        }

        private void ShowCurrentInfo()
        {
            var quote = _host.CurrentQuote;
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
                $"Ảnh:\n{_host.DescribeCurrentImage()}\n\nQuote:\n{quoteText}\n\nĐổi ảnh tiếp theo lúc: {nextText}",
                "Thông tin ảnh hiện tại", Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Information);
        }

        private static Forms.ToolStripMenuItem Item(string text, Action onClick)
        {
            var item = new Forms.ToolStripMenuItem(text);
            item.Click += (_, __) =>
            {
                try { onClick(); }
                catch (Exception ex) { Log.Error($"Lỗi khi chọn menu \"{text}\"", ex); }
            };
            return item;
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
                g.FillEllipse(white, 20, 8, 6, 6);
                g.FillRectangle(white, 7, 19, 14, 3);
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
