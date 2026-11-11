using System;
using System.Threading;
using System.Threading.Tasks;
using WallpaperQuote.Core;
using Forms = System.Windows.Forms;

namespace WallpaperQuote
{
    internal static class Program
    {
        private const string MutexName = "Local\\WallpaperQuote.SingleInstance.v1";

        [STAThread]
        private static void Main()
        {
            // Chỉ cho 1 phiên bản chạy (W6.1).
            using var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
            if (!createdNew)
            {
                Forms.MessageBox.Show(
                    "Hình nền & Quote đang chạy.\nBấm chuột phải vào icon dưới khay hệ thống (góc phải thanh taskbar) để mở menu.",
                    "Hình nền & Quote", Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Information);
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
            app.Startup += (_, __) => host.Start();
            app.Exit += (_, __) => host.Dispose();
            app.Run();

            GC.KeepAlive(mutex);
        }
    }
}
