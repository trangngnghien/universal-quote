using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WallpaperQuote.Core;
using WallpaperQuote.Platform;
using Forms = System.Windows.Forms;

namespace WallpaperQuote
{
    internal static class Program
    {
        private const string MutexName = "Local\\WallpaperQuote.SingleInstance.v1";
        private const string ShowSettingsEventName = "Local\\WallpaperQuote.ShowSettings.v1";

        [STAThread]
        private static void Main(string[] args)
        {
            var autostart = args.Any(a => string.Equals(a, StartupRegistration.AutostartArg, StringComparison.OrdinalIgnoreCase));

            // Chỉ cho 1 phiên bản chạy (W6.1). Mở lần 2 → báo phiên bản đang chạy bật cửa sổ Cài đặt.
            using var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
            if (!createdNew)
            {
                if (!autostart)
                {
                    try
                    {
                        using var signal = EventWaitHandle.OpenExisting(ShowSettingsEventName);
                        signal.Set();
                    }
                    catch
                    {
                        Forms.MessageBox.Show(
                            "Hình nền & Quote đang chạy.\nBấm chuột phải vào icon dưới khay hệ thống (góc phải thanh taskbar) để mở menu.",
                            "Hình nền & Quote", Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Information);
                    }
                }
                return;
            }

            Forms.Application.EnableVisualStyles();
            Forms.Application.SetCompatibleTextRenderingDefault(false);

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                Log.Error("Lỗi không xử lý được", e.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                Log.Error("Lỗi trong tác vụ nền", e.Exception);
                e.SetObserved();
            };

            var app = new System.Windows.Application
            {
                ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown
            };
            app.DispatcherUnhandledException += (_, e) =>
            {
                Log.Error("Lỗi giao diện", e.Exception);
                e.Handled = true;   // app chạy nền 24/7: ghi log và tiếp tục, không văng app
            };

            var host = new AppHost();
            using var showSettings = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEventName);
            var listener = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        showSettings.WaitOne();
                        app.Dispatcher.BeginInvoke(new Action(host.OpenSettings));
                    }
                    catch
                    {
                        return;
                    }
                }
            })
            { IsBackground = true, Name = "WallpaperQuote.ShowSettings" };

            app.Startup += (_, __) =>
            {
                host.Start(autostart);
                listener.Start();
            };
            app.Exit += (_, __) => host.Dispose();
            app.Run();

            GC.KeepAlive(mutex);
        }
    }
}
