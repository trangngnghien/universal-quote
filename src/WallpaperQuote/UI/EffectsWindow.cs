using System;
using System.Runtime.InteropServices;
using System.Text;
using WallpaperQuote.Core;
using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;
using Forms = System.Windows.Forms;

namespace WallpaperQuote.UI
{
    /// <summary>
    /// Lớp hiệu ứng nằm SAU các icon desktop (W7). Cửa sổ Win32 thuần (WinForms, vẽ GDI+) gắn vào lớp desktop:
    /// Windows 11 24H2+ → con của Progman, ngay dưới SHELLDLL_DefView; bản cũ → con của WorkerW.
    /// Cửa sổ vẽ lại chính hình nền hiện tại + hạt rơi phía trên, nên nhìn như hình nền đang chuyển động.
    /// Không nhận chuột, không hiện trên taskbar.
    /// </summary>
    public sealed class EffectsWindow : Forms.Form
    {
        private Drawing.Bitmap? _source;
        private Drawing.Bitmap? _scaled;
        private string? _backgroundFile;
        public ParticleEngine Particles { get; } = new ParticleEngine();

        public EffectsWindow()
        {
            Text = "Hình nền & Quote – hiệu ứng";
            FormBorderStyle = Forms.FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = Forms.FormStartPosition.Manual;
            BackColor = Drawing.Color.Black;
            SetStyle(Forms.ControlStyles.AllPaintingInWmPaint | Forms.ControlStyles.UserPaint | Forms.ControlStyles.OptimizedDoubleBuffer | Forms.ControlStyles.Opaque, true);
            Particles.Changed += () => { if (IsHandleCreated && !IsDisposed) Invalidate(); };
        }

        protected override bool ShowWithoutActivation => true;

        protected override Forms.CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= (int)(DesktopLayer.WS_EX_NOACTIVATE | DesktopLayer.WS_EX_TRANSPARENT | DesktopLayer.WS_EX_TOOLWINDOW);
                return cp;
            }
        }

        protected override void WndProc(ref Forms.Message m)
        {
            const int WM_NCHITTEST = 0x0084, HTTRANSPARENT = -1, WM_MOUSEACTIVATE = 0x0021, MA_NOACTIVATE = 3;
            if (m.Msg == WM_NCHITTEST) { m.Result = new IntPtr(HTTRANSPARENT); return; }
            if (m.Msg == WM_MOUSEACTIVATE) { m.Result = new IntPtr(MA_NOACTIVATE); return; }
            base.WndProc(ref m);
        }

        public void SetBackground(string? file)
        {
            if (file == null) return;
            if (_source != null && string.Equals(file, _backgroundFile, StringComparison.OrdinalIgnoreCase) && !FileChangedSinceLoad(file)) return;
            try
            {
                // Đọc vào bộ nhớ rồi đóng file ngay, để app vẫn ghi đè được file hình nền.
                var bytes = System.IO.File.ReadAllBytes(file);
                using var ms = new System.IO.MemoryStream(bytes);
                using var img = Drawing.Image.FromStream(ms);
                _source?.Dispose();
                _source = new Drawing.Bitmap(img);
                _backgroundFile = file;
                _loadedAt = System.IO.File.GetLastWriteTimeUtc(file);
                RebuildScaled();
                if (IsHandleCreated) Invalidate();
            }
            catch (Exception ex)
            {
                Log.Warn($"Lớp hiệu ứng không nạp được hình nền: {ex.Message}");
            }
        }

        private DateTime _loadedAt;
        private bool FileChangedSinceLoad(string file)
        {
            try { return System.IO.File.GetLastWriteTimeUtc(file) != _loadedAt; } catch { return true; }
        }

        private void RebuildScaled()
        {
            var w = ClientSize.Width;
            var h = ClientSize.Height;
            Particles.Width = w;
            Particles.Height = h;
            if (_source == null || w <= 0 || h <= 0) return;
            _scaled?.Dispose();
            _scaled = new Drawing.Bitmap(w, h, Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using var g = Drawing.Graphics.FromImage(_scaled);
            g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(_source, new Drawing.Rectangle(0, 0, w, h));
        }

        protected override void OnClientSizeChanged(EventArgs e)
        {
            base.OnClientSizeChanged(e);
            RebuildScaled();
        }

        protected override void OnPaint(Forms.PaintEventArgs e)
        {
            var g = e.Graphics;
            if (_scaled != null) g.DrawImageUnscaled(_scaled, 0, 0);
            else g.Clear(Drawing.Color.Black);
            Particles.Render(g);
        }

        /// <summary>Gắn cửa sổ vào lớp desktop phía sau icon. Trả false nếu không tìm thấy lớp đó.</summary>
        public bool AttachToDesktop()
        {
            var layer = DesktopLayer.Find();
            if (layer.Parent == IntPtr.Zero) return false;

            var hwnd = Handle;   // tạo cửa sổ
            var style = DesktopLayer.GetWindowLongPtr(hwnd, DesktopLayer.GWL_STYLE).ToInt64();
            style = (style & ~(long)DesktopLayer.WS_POPUP) | DesktopLayer.WS_CHILD;
            DesktopLayer.SetWindowLongPtr(hwnd, DesktopLayer.GWL_STYLE, new IntPtr(style));
            if (layer.Raised)
            {
                // Windows 11 24H2+: Progman là cửa sổ "layered"; cửa sổ con cũng phải layered mới hiện được.
                var exStyle = DesktopLayer.GetWindowLongPtr(hwnd, DesktopLayer.GWL_EXSTYLE).ToInt64() | DesktopLayer.WS_EX_LAYERED;
                DesktopLayer.SetWindowLongPtr(hwnd, DesktopLayer.GWL_EXSTYLE, new IntPtr(exStyle));
                DesktopLayer.SetLayeredWindowAttributes(hwnd, 0, 255, DesktopLayer.LWA_ALPHA);
            }

            if (DesktopLayer.SetParent(hwnd, layer.Parent) == IntPtr.Zero)
            {
                Log.Warn("Không gắn được lớp hiệu ứng vào desktop (SetParent)");
                return false;
            }

            Show();
            Reposition();
            if (layer.Raised)
            {
                // Đặt ngay dưới lớp icon (SHELLDLL_DefView), trên lớp vẽ hình nền (WorkerW).
                DesktopLayer.SetWindowPos(hwnd, layer.Icons, 0, 0, 0, 0,
                    DesktopLayer.SWP_NOMOVE | DesktopLayer.SWP_NOSIZE | DesktopLayer.SWP_NOACTIVATE);
                if (layer.Wallpaper != IntPtr.Zero)
                    DesktopLayer.SetWindowPos(layer.Wallpaper, DesktopLayer.HWND_BOTTOM, 0, 0, 0, 0,
                        DesktopLayer.SWP_NOMOVE | DesktopLayer.SWP_NOSIZE | DesktopLayer.SWP_NOACTIVATE);
            }
            Log.Info((layer.Raised ? "Lớp hiệu ứng: kiểu Windows 11 24H2+ (con của Progman, dưới icon)" : "Lớp hiệu ứng: kiểu cũ (con của WorkerW)")
                     + " · " + DesktopLayer.Describe(hwnd));
            return true;
        }

        /// <summary>Phủ đúng màn hình chính (toạ độ pixel thật, tính từ góc cửa sổ cha).</summary>
        public void Reposition()
        {
            if (!IsHandleCreated) return;
            var hwnd = Handle;
            var primary = (Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0]).Bounds;
            var parent = DesktopLayer.GetParent(hwnd);
            var origin = new DesktopLayer.POINT { X = primary.X, Y = primary.Y };
            if (parent != IntPtr.Zero) DesktopLayer.ScreenToClient(parent, ref origin);
            DesktopLayer.SetWindowPos(hwnd, IntPtr.Zero, origin.X, origin.Y, primary.Width, primary.Height,
                DesktopLayer.SWP_NOZORDER | DesktopLayer.SWP_NOACTIVATE | DesktopLayer.SWP_SHOWWINDOW);
            RebuildScaled();
            Invalidate();
        }

        public bool IsAlive => IsHandleCreated && !IsDisposed && DesktopLayer.IsWindow(Handle) && DesktopLayer.GetParent(Handle) != IntPtr.Zero;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Particles.Dispose();
                _scaled?.Dispose();
                _source?.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal static class DesktopLayer
    {
        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;
        public const long WS_CHILD = 0x40000000L;
        public const long WS_POPUP = 0x80000000L;
        public const long WS_EX_NOACTIVATE = 0x08000000L;
        public const long WS_EX_TRANSPARENT = 0x00000020L;
        public const long WS_EX_TOOLWINDOW = 0x00000080L;
        public const long WS_EX_LAYERED = 0x00080000L;
        public const uint LWA_ALPHA = 0x2;
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);

        [DllImport("user32.dll")]
        public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        public static extern bool ScreenToClient(IntPtr hWnd, ref POINT p);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT r);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);

        private static string ClassOf(IntPtr h)
        {
            if (h == IntPtr.Zero) return "-";
            var sb = new StringBuilder(64);
            GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        /// <summary>Thông tin chẩn đoán: vị trí, hiển thị, cửa sổ cha và cửa sổ liền trên/dưới.</summary>
        public static string Describe(IntPtr hwnd)
        {
            GetWindowRect(hwnd, out var r);
            const uint GW_HWNDNEXT = 2, GW_HWNDPREV = 3;
            return $"rect=({r.Left},{r.Top})-({r.Right},{r.Bottom}) visible={IsWindowVisible(hwnd)} parent={ClassOf(GetParent(hwnd))} above={ClassOf(GetWindow(hwnd, GW_HWNDPREV))} below={ClassOf(GetWindow(hwnd, GW_HWNDNEXT))}";
        }

        public struct Layer
        {
            public IntPtr Parent;     // cửa sổ cha để gắn lớp hiệu ứng
            public IntPtr Icons;      // SHELLDLL_DefView
            public IntPtr Wallpaper;  // WorkerW vẽ hình nền
            public bool Raised;       // Windows 11 24H2+: icon nằm trực tiếp trong Progman
        }

        public static Layer Find()
        {
            var progman = FindWindow("Progman", null);
            if (progman == IntPtr.Zero) return default;
            SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(0x1), 0, 1000, out _);
            SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 1000, out _);

            var icons = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (icons != IntPtr.Zero)
            {
                return new Layer
                {
                    Parent = progman,
                    Icons = icons,
                    Wallpaper = FindWindowEx(progman, IntPtr.Zero, "WorkerW", null),
                    Raised = true
                };
            }
            return new Layer { Parent = FindWorkerW() };
        }
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string? className, string? windowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

        [DllImport("user32.dll")]
        public static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

        /// <summary>
        /// Tìm lớp "WorkerW" nằm giữa hình nền và icon desktop.
        /// Gửi thông điệp 0x052C cho Progman để Windows tạo lớp này (cách Wallpaper Engine / Lively dùng).
        /// Windows 11 24H2 trở đi: WorkerW là con của Progman.
        /// </summary>
        public static IntPtr FindWorkerW()
        {
            var progman = FindWindow("Progman", null);
            if (progman == IntPtr.Zero) return IntPtr.Zero;
            SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(0x1), 0, 1000, out _);
            SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 1000, out _);

            var workerW = IntPtr.Zero;
            EnumWindows((top, _) =>
            {
                var view = FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (view != IntPtr.Zero)
                    workerW = FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
                return true;
            }, IntPtr.Zero);

            if (workerW == IntPtr.Zero)
                workerW = FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);   // Windows 11 24H2+
            return workerW;
        }
    }
}
