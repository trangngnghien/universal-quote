using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using WallpaperQuote.Core;

namespace WallpaperQuote.Platform
{
    /// <summary>Đặt / đọc hình nền qua Windows API (W4.2).</summary>
    public static class WallpaperSetter
    {
        private const uint SPI_SETDESKWALLPAPER = 0x0014;
        private const uint SPI_GETDESKWALLPAPER = 0x0073;
        private const uint SPIF_UPDATEINIFILE = 0x01;
        private const uint SPIF_SENDCHANGE = 0x02;
        private const int MaxPath = 1024;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SystemParametersInfoW")]
        private static extern bool SystemParametersInfoSet(uint action, uint param, string value, uint winIni);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SystemParametersInfoW")]
        private static extern bool SystemParametersInfoGet(uint action, uint param, StringBuilder value, uint winIni);

        public static void Set(string imagePath)
        {
            // Ảnh đã được ghép đúng độ phân giải màn hình, nên dùng kiểu "Fill" để Windows không co giãn thêm.
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true);
                key?.SetValue("WallpaperStyle", "10");
                key?.SetValue("TileWallpaper", "0");
            }
            catch (Exception ex)
            {
                Log.Warn($"Không đặt được kiểu hình nền: {ex.Message}");
            }

            if (!SystemParametersInfoSet(SPI_SETDESKWALLPAPER, 0, imagePath, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows từ chối đặt hình nền");
        }

        public static string? GetCurrent()
        {
            try
            {
                var sb = new StringBuilder(MaxPath);
                if (SystemParametersInfoGet(SPI_GETDESKWALLPAPER, MaxPath, sb, 0))
                {
                    var path = sb.ToString();
                    return string.IsNullOrWhiteSpace(path) ? null : path;
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Không đọc được hình nền hiện tại: {ex.Message}");
            }
            return null;
        }
    }

    /// <summary>Khởi động cùng Windows qua HKCU\...\Run, không cần quyền Admin (W6.2).</summary>
    public static class StartupRegistration
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "WallpaperQuote";

        /// <summary>Tham số khi Windows tự mở app: chạy ngầm, không bật cửa sổ Cài đặt.</summary>
        public const string AutostartArg = "--autostart";

        public static bool IsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
                return key?.GetValue(ValueName) is string;
            }
            catch
            {
                return false;
            }
        }

        /// <returns>true nếu ghi thành công.</returns>
        public static bool Set(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
                if (enabled)
                {
                    var exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
                    if (string.IsNullOrEmpty(exe)) return false;
                    key.SetValue(ValueName, $"\"{exe}\" {AutostartArg}");
                }
                else
                {
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Không ghi được đăng ký khởi động cùng Windows", ex);
                return false;
            }
        }
    }

    /// <summary>Chạy việc nặng (quét ảnh, ghép ảnh) trên một luồng STA riêng để không làm đơ menu.</summary>
    public static class Sta
    {
        public static Task<T> Run<T>(Func<T> work)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try { tcs.SetResult(work()); }
                catch (Exception ex) { tcs.SetException(ex); }
            })
            {
                IsBackground = true,
                Name = "WallpaperQuote.Worker"
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return tcs.Task;
        }

        public static Task Run(Action work) => Run<bool>(() => { work(); return true; });
    }

    /// <summary>Lối tắt ngoài desktop và hiện lại biểu tượng desktop khi đang bị ẩn.</summary>
    public static class DesktopIntegration
    {
        private const string ShortcutName = "Hình nền & Quote.lnk";
        private const uint WM_COMMAND = 0x0111;
        private const int ToggleDesktopIcons = 0x7402;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string? className, string? windowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        public static string ShortcutPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ShortcutName);

        public static bool ShortcutExists() => File.Exists(ShortcutPath);

        /// <summary>
        /// Tạo lối tắt "Hình nền & Quote" ngoài desktop trỏ tới file exe đang chạy.
        /// Dùng trực tiếp giao diện IShellLinkW (Unicode) để tên có dấu và desktop trong OneDrive vẫn lưu được.
        /// </summary>
        public static bool CreateShortcut()
        {
            try
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return false;
                var link = (IShellLinkW)new ShellLinkCom();
                link.SetPath(exe);
                link.SetWorkingDirectory(Path.GetDirectoryName(exe) ?? "");
                link.SetDescription("Mở cài đặt Hình nền & Quote");
                link.SetIconLocation(exe, 0);
                var target = ShortcutPath;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Save(target, true);
                Marshal.FinalReleaseComObject(link);
                return File.Exists(target);
            }
            catch (Exception ex)
            {
                Log.Error("Không tạo được lối tắt ngoài desktop", ex);
                return false;
            }
        }

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLinkCom
        {
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxPath, IntPtr findData, uint flags);
            void GetIDList(out IntPtr idList);
            void SetIDList(IntPtr idList);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int maxPath);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int maxPath);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
            void GetHotkey(out short hotkey);
            void SetHotkey(short hotkey);
            void GetShowCmd(out int showCmd);
            void SetShowCmd(int showCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int maxPath, out int iconIndex);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relPath, uint reserved);
            void Resolve(IntPtr hwnd, uint flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
        }

        /// <summary>Windows đang ẩn biểu tượng desktop (chuột phải desktop → View → bỏ tick "Show desktop icons").</summary>
        public static bool IconsHidden()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
                return key?.GetValue("HideIcons") is int v && v == 1;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Hiện lại biểu tượng desktop nếu đang bị ẩn. Không làm gì nếu đang hiện.</summary>
        public static bool ShowIcons()
        {
            if (!IconsHidden()) return true;
            var view = FindDefView();
            if (view == IntPtr.Zero) return false;
            SendMessage(view, WM_COMMAND, (IntPtr)ToggleDesktopIcons, IntPtr.Zero);
            return true;
        }

        private static IntPtr FindDefView()
        {
            var progman = FindWindow("Progman", null);
            var view = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (view != IntPtr.Zero) return view;
            var worker = IntPtr.Zero;
            while ((worker = FindWindowEx(IntPtr.Zero, worker, "WorkerW", null)) != IntPtr.Zero)
            {
                view = FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (view != IntPtr.Zero) return view;
            }
            return IntPtr.Zero;
        }
    }

    public static class Shell
    {
        public static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Error("Không mở được link", ex);
            }
        }

        public static void OpenFolder(string folder)
        {
            try
            {
                Directory.CreateDirectory(folder);
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Error("Không mở được thư mục", ex);
            }
        }
    }
}
