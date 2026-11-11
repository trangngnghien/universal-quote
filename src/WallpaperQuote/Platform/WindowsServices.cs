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
                    key.SetValue(ValueName, $"\"{exe}\"");
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

    public static class Shell
    {
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
