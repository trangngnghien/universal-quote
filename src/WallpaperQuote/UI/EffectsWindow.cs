using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WallpaperQuote.Core;
using Forms = System.Windows.Forms;

namespace WallpaperQuote.UI
{
    /// <summary>
    /// Lớp hiệu ứng nằm SAU các icon desktop (W7): cửa sổ con của "WorkerW" — cùng cách các app hình nền động dùng.
    /// Cửa sổ vẽ lại chính hình nền hiện tại + hạt rơi phía trên, nên nhìn như hình nền đang chuyển động.
    /// Không nhận chuột, không hiện trên taskbar.
    /// </summary>
    public sealed class EffectsWindow : Window
    {
        private readonly Image _background = new Image { Stretch = Stretch.Fill };
        public ParticleLayer Particles { get; } = new ParticleLayer();

        public EffectsWindow()
        {
            Title = "Hình nền & Quote – hiệu ứng";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            IsHitTestVisible = false;
            Background = Brushes.Black;
            var grid = new Grid();
            grid.Children.Add(_background);
            grid.Children.Add(Particles);
            Content = grid;
        }

        public void SetBackground(string? file)
        {
            if (file == null) return;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bmp.UriSource = new Uri(file, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                _background.Source = bmp;
            }
            catch (Exception ex)
            {
                Log.Warn($"Lớp hiệu ứng không nạp được hình nền: {ex.Message}");
            }
        }

        /// <summary>Gắn cửa sổ vào lớp desktop phía sau icon. Trả false nếu không tìm thấy lớp đó.</summary>
        public bool AttachToDesktop()
        {
            var layer = DesktopLayer.Find();
            if (layer.Parent == IntPtr.Zero) return false;

            var helper = new WindowInteropHelper(this);
            var hwnd = helper.EnsureHandle();
            var style = DesktopLayer.GetWindowLongPtr(hwnd, DesktopLayer.GWL_STYLE).ToInt64();
            style = (style & ~(long)DesktopLayer.WS_POPUP) | DesktopLayer.WS_CHILD;
            DesktopLayer.SetWindowLongPtr(hwnd, DesktopLayer.GWL_STYLE, new IntPtr(style));
            var exStyle = DesktopLayer.GetWindowLongPtr(hwnd, DesktopLayer.GWL_EXSTYLE).ToInt64();
            exStyle |= DesktopLayer.WS_EX_NOACTIVATE | DesktopLayer.WS_EX_TRANSPARENT | DesktopLayer.WS_EX_TOOLWINDOW;
            if (layer.Raised) exStyle |= DesktopLayer.WS_EX_LAYERED;
            DesktopLayer.SetWindowLongPtr(hwnd, DesktopLayer.GWL_EXSTYLE, new IntPtr(exStyle));
            if (layer.Raised)
            {
                // Windows 11 24H2+: Progman là cửa sổ "layered"; cửa sổ con cũng phải layered mới hiện được.
                DesktopLayer.SetLayeredWindowAttributes(hwnd, 0, 255, DesktopLayer.LWA_ALPHA);
                var source = HwndSource.FromHwnd(hwnd);
                if (source?.CompositionTarget != null) source.CompositionTarget.RenderMode = RenderMode.SoftwareOnly;
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
                // Đặt ngay dưới lớp icon (SHELLDLL_DefView) và trên lớp vẽ hình nền (WorkerW).
                DesktopLayer.SetWindowPos(hwnd, layer.Icons, 0, 0, 0, 0,
                    DesktopLayer.SWP_NOMOVE | DesktopLayer.SWP_NOSIZE | DesktopLayer.SWP_NOACTIVATE);
                if (layer.Wallpaper != IntPtr.Zero)
                    DesktopLayer.SetWindowPos(layer.Wallpaper, DesktopLayer.HWND_BOTTOM, 0, 0, 0, 0,
                        DesktopLayer.SWP_NOMOVE | DesktopLayer.SWP_NOSIZE | DesktopLayer.SWP_NOACTIVATE);
            }
            Log.Info(layer.Raised ? "Lớp hiệu ứng: kiểu Windows 11 24H2+ (con của Progman, dưới icon)" : "Lớp hiệu ứng: kiểu cũ (con của WorkerW)");
            return true;
        }

        /// <summary>Phủ đúng màn hình chính (toạ độ pixel thật, tính từ góc màn hình ảo).</summary>
        public void Reposition()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            var primary = (Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0]).Bounds;
            var virtualScreen = Forms.SystemInformation.VirtualScreen;
            DesktopLayer.SetWindowPos(hwnd, IntPtr.Zero,
                primary.X - virtualScreen.X, primary.Y - virtualScreen.Y, primary.Width, primary.Height,
                DesktopLayer.SWP_NOZORDER | DesktopLayer.SWP_NOACTIVATE | DesktopLayer.SWP_SHOWWINDOW);
        }

        public bool IsAlive
        {
            get
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                return hwnd != IntPtr.Zero && DesktopLayer.IsWindow(hwnd) && DesktopLayer.GetParent(hwnd) != IntPtr.Zero;
            }
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
