using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using WallpaperQuote.Core;

namespace WallpaperQuote.UI
{
    /// <summary>
    /// Bắt click chuột trái lên desktop (W7.3). Chỉ cài khi người dùng chọn chế độ "Click trên desktop".
    /// Dùng hook chuột cấp thấp: app chỉ xem cửa sổ dưới con trỏ có phải desktop không, không ghi lại gì khác.
    /// </summary>
    public sealed class DesktopClickHook : IDisposable
    {
        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData, flags, time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc proc, IntPtr hMod, uint threadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? name);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT p);

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);

        private readonly LowLevelMouseProc _proc;   // giữ tham chiếu để GC không thu hồi
        private readonly Action<int, int> _onDesktopClick;
        private IntPtr _hook;

        public DesktopClickHook(Action<int, int> onDesktopClick)
        {
            _onDesktopClick = onDesktopClick;
            _proc = Callback;
        }

        public bool IsInstalled => _hook != IntPtr.Zero;

        public bool Install()
        {
            if (_hook != IntPtr.Zero) return true;
            using var module = Process.GetCurrentProcess().MainModule;
            _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(module?.ModuleName), 0);
            if (_hook == IntPtr.Zero)
            {
                Log.Warn($"Không cài được bắt click desktop (lỗi {Marshal.GetLastWin32Error()})");
                return false;
            }
            Log.Info("Đã bật: click chuột trái lên desktop để thả hiệu ứng");
            return true;
        }

        public void Uninstall()
        {
            if (_hook == IntPtr.Zero) return;
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
            Log.Info("Đã tắt bắt click desktop");
        }

        private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam.ToInt32() == WM_LBUTTONDOWN)
            {
                try
                {
                    var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    if (IsDesktop(WindowFromPoint(info.pt))) _onDesktopClick(info.pt.X, info.pt.Y);
                }
                catch
                {
                    // Không để lỗi trong hook làm chậm chuột của người dùng.
                }
            }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        /// <summary>Cửa sổ là vùng desktop: SysListView32 (danh sách icon) trong SHELLDLL_DefView, hoặc chính DefView/WorkerW/Progman.</summary>
        private static bool IsDesktop(IntPtr hwnd)
        {
            for (var i = 0; i < 3 && hwnd != IntPtr.Zero; i++)
            {
                var name = ClassOf(hwnd);
                if (name == "SHELLDLL_DefView" || name == "WorkerW" || name == "Progman") return true;
                if (name != "SysListView32") return false;
                hwnd = GetParent(hwnd);
            }
            return false;
        }

        private static string ClassOf(IntPtr hwnd)
        {
            var sb = new StringBuilder(64);
            GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        public void Dispose() => Uninstall();
    }
}
