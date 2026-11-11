using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.Win32;
using WallpaperQuote.Core;
using WallpaperQuote.Platform;
using Forms = System.Windows.Forms;

namespace WallpaperQuote
{
    [Flags]
    public enum Change
    {
        None = 0,
        Image = 1,
        Quote = 2,
        All = Image | Quote,
        Rerender = 4
    }

    /// <summary>
    /// Điều phối toàn bộ app: lịch xoay (W3.1), chọn ngẫu nhiên (W3.2), làm mới thủ công (W3.3),
    /// ghép và đặt hình nền (W4). Mọi thao tác chạy trên luồng giao diện; việc nặng đẩy sang luồng phụ.
    /// </summary>
    public sealed class AppHost : IDisposable
    {
        private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
        private readonly QuoteLibrary _quotes = new QuoteLibrary();
        private readonly ShuffleBag<string> _imageBag = new ShuffleBag<string>(StringComparer.OrdinalIgnoreCase);
        private readonly ShuffleBag<Quote> _quoteBag = new ShuffleBag<Quote>();
        private readonly DispatcherTimer _imageTimer = new DispatcherTimer();
        private readonly DispatcherTimer _quoteTimer = new DispatcherTimer();
        private readonly DispatcherTimer _rescanDebounce = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        private readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();

        private AppSettings _settings = new AppSettings();
        private TrayController? _tray;
        private List<string> _images = new List<string>();
        private ScanResult? _lastScan;
        private string? _currentImage;
        private Quote? _currentQuote;
        private DateTime _lastImageChange = DateTime.MinValue;
        private DateTime _lastQuoteChange = DateTime.MinValue;
        private bool _busy;
        private Change _pendingAuto = Change.None;
        private int _slot;
        private bool _noImageNotified;
        private bool _disposed;

        public AppSettings Settings => _settings;
        public bool IsPaused => _settings.Paused;
        public int ImageCount => _images.Count;
        public int QuoteCount => _quoteBag.Count;
        public string? CurrentImage => _currentImage;
        public Quote? CurrentQuote => _currentQuote;
        public ScanResult? LastScan => _lastScan;

        public void Start()
        {
            _settings = SettingsStore.Load();
            Log.Info("Khởi động app");

            // Lưu hình nền gốc ở lần chạy đầu để khôi phục khi gỡ cài (G4).
            if (!_settings.OriginalWallpaperSaved)
            {
                _settings.OriginalWallpaper = WallpaperSetter.GetCurrent();
                _settings.OriginalWallpaperSaved = true;
                SettingsStore.Save(_settings);
            }

            _quoteBag.SetItems(_quotes.Active(_settings.QuoteLanguage));

            _imageTimer.Tick += async (_, __) => await ChangeAsync(Change.Image, auto: true);
            _quoteTimer.Tick += async (_, __) => await ChangeAsync(Change.Quote, auto: true);
            _rescanDebounce.Tick += async (_, __) =>
            {
                _rescanDebounce.Stop();
                await RescanAsync();
            };

            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

            _tray = new TrayController(this);
            _tray.Show();

            _ = InitialAsync();
        }

        private async Task InitialAsync()
        {
            await RescanAsync();

            if (!_settings.WelcomeShown)
            {
                _settings.WelcomeShown = true;
                SettingsStore.Save(_settings);
                _tray?.Notify("Hình nền & Quote đang chạy",
                    "Hình nền tự đổi theo lịch. Nháy đúp icon dưới khay để làm mới ngay. Bấm chuột phải để chọn thư mục ảnh.");
            }

            if (!_settings.Paused && (_settings.ChangeOnStartup || _currentImage == null))
                await ChangeAsync(Change.All, auto: false, resetTimers: true);
            else
                RestartTimers();
        }

        // ------------------------------------------------------------------ Làm mới

        /// <summary>
        /// Đổi ảnh và/hoặc quote. Lượt tự động đang chờ được gộp và chạy ngay sau lượt hiện tại;
        /// lần bấm thủ công trong lúc đang xử lý thì bỏ qua (BR W3.3).
        /// </summary>
        public async Task ChangeAsync(Change change, bool auto, bool resetTimers = false)
        {
            if (_disposed) return;
            if (_busy)
            {
                if (auto) _pendingAuto |= change;
                return;
            }
            if (auto && _settings.Paused) return;

            _busy = true;
            try
            {
                await DoChangeAsync(change, auto);
                if (!auto || resetTimers)
                {
                    // Làm mới xong, chu kỳ tương ứng tính lại từ đầu.
                    if (change.HasFlag(Change.Image)) RestartTimer(_imageTimer, _settings.ImageIntervalMinutes);
                    if (change.HasFlag(Change.Quote)) RestartTimer(_quoteTimer, _settings.QuoteIntervalMinutes);
                }
            }
            finally
            {
                _busy = false;
                _tray?.Refresh();
            }

            if (_pendingAuto != Change.None)
            {
                var next = _pendingAuto;
                _pendingAuto = Change.None;
                await ChangeAsync(next, auto: true);
            }
        }

        private async Task DoChangeAsync(Change change, bool auto)
        {
            var image = _currentImage;
            var quote = _currentQuote;
            var changeImage = change.HasFlag(Change.Image) || image == null;
            var changeQuote = change.HasFlag(Change.Quote) || quote == null;

            if (changeImage) image = await PickImageAsync();
            if (changeQuote) quote = _quoteBag.Next();

            var screen = (Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens.First()).Bounds;
            var width = Math.Max(640, screen.Width);
            var height = Math.Max(480, screen.Height);
            var output = Paths.WallpaperFile(_slot ^ 1);

            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    var request = new RenderRequest(image, quote, width, height, _settings.FitMode);
                    await Sta.Run(() => WallpaperRenderer.RenderToFile(request, output));
                    break;
                }
                catch (ImageDecodeException ex) when (attempt < 5)
                {
                    // Ảnh hỏng hoặc đã bị xóa → loại khỏi kho, chọn ảnh khác.
                    Log.Warn(ex.Message);
                    RemoveImage(ex.ImagePath);
                    image = await PickImageAsync();
                    changeImage = true;
                }
                catch (Exception ex)
                {
                    Log.Error("Không ghép được hình nền", ex);
                    if (!auto) _tray?.Notify("Không đổi được hình nền", "Đã giữ hình nền hiện tại. App sẽ thử lại ở lượt sau.", warning: true);
                    return;
                }
            }

            try
            {
                WallpaperSetter.Set(output);
            }
            catch (Exception ex)
            {
                Log.Error("Windows không đặt được hình nền", ex);
                if (!auto) _tray?.Notify("Không đổi được hình nền", "Đã giữ hình nền hiện tại. App sẽ thử lại ở lượt sau.", warning: true);
                return;
            }

            _slot ^= 1;
            _currentImage = image;
            _currentQuote = quote;
            if (changeImage) _lastImageChange = DateTime.Now;
            if (changeQuote) _lastQuoteChange = DateTime.Now;
        }

        private async Task<string?> PickImageAsync()
        {
            if (_images.Count == 0 && _settings.Folders.Count > 0) await RescanAsync();

            for (var i = 0; i < 50; i++)
            {
                var next = _imageBag.Next();
                if (next == null) break;
                if (File.Exists(next)) return next;
                RemoveImage(next);
            }

            if (!_noImageNotified)
            {
                _noImageNotified = true;
                _tray?.Notify("Chưa có ảnh để hiển thị",
                    "Bấm chuột phải vào icon dưới khay → Thư mục ảnh → Thêm thư mục. Tạm thời quote hiện trên nền tối.",
                    warning: true);
            }
            return null;
        }

        private void RemoveImage(string path)
        {
            if (_images.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) > 0)
                _imageBag.SetItems(_images);
        }

        // ------------------------------------------------------------------ Thư mục ảnh

        public async Task RescanAsync()
        {
            var folders = _settings.Folders.ToList();
            var recursive = _settings.ScanSubfolders;
            var minWidth = _settings.MinImageWidth;
            try
            {
                var result = await Sta.Run(() => ImageScanner.Scan(folders, recursive, minWidth, ImageProbe.Width));
                _lastScan = result;
                _images = result.Ready;
                _imageBag.SetItems(_images);
                if (_images.Count > 0) _noImageNotified = false;
                Log.Info($"Quét ảnh: {result.Ready.Count} sẵn sàng, {result.TooSmall} nhỏ hơn {minWidth}px, {result.Unreadable} không đọc được");
            }
            catch (Exception ex)
            {
                Log.Error("Lỗi quét thư mục ảnh", ex);
            }
            RebuildWatchers();
            _tray?.Refresh();
        }

        public async Task AddFolderAsync(string folder)
        {
            var full = Path.GetFullPath(folder);
            if (_settings.Folders.Any(f => string.Equals(f, full, StringComparison.OrdinalIgnoreCase)))
            {
                _tray?.Notify("Thư mục đã được thêm", full);
                return;
            }
            _settings.Folders.Add(full);
            SettingsStore.Save(_settings);
            await RescanAsync();

            var count = _lastScan?.Ready.Count ?? 0;
            if (count == 0)
                _tray?.Notify("Không có ảnh hợp lệ", $"Không tìm thấy ảnh jpg/png/bmp/webp rộng từ {_settings.MinImageWidth}px trong thư mục này.", warning: true);
            else if (_currentImage == null)
                await ChangeAsync(Change.Image, auto: false);
        }

        public async Task RemoveFolderAsync(string folder)
        {
            _settings.Folders.RemoveAll(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
            SettingsStore.Save(_settings);
            await RescanAsync();
        }

        public async Task SetScanSubfoldersAsync(bool value)
        {
            _settings.ScanSubfolders = value;
            SettingsStore.Save(_settings);
            await RescanAsync();
        }

        private void RebuildWatchers()
        {
            foreach (var w in _watchers) w.Dispose();
            _watchers.Clear();
            foreach (var folder in _settings.Folders.Where(Directory.Exists))
            {
                try
                {
                    var watcher = new FileSystemWatcher(folder)
                    {
                        IncludeSubdirectories = _settings.ScanSubfolders,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                    };
                    FileSystemEventHandler onChange = (_, __) => _dispatcher.InvokeAsync(RequestRescan);
                    watcher.Created += onChange;
                    watcher.Deleted += onChange;
                    watcher.Renamed += (_, __) => _dispatcher.InvokeAsync(RequestRescan);
                    watcher.EnableRaisingEvents = true;
                    _watchers.Add(watcher);
                }
                catch (Exception ex)
                {
                    Log.Warn($"Không theo dõi được thư mục {folder}: {ex.Message}");
                }
            }
        }

        private void RequestRescan()
        {
            // Gộp nhiều thay đổi liên tiếp thành 1 lần quét.
            _rescanDebounce.Stop();
            _rescanDebounce.Start();
        }

        // ------------------------------------------------------------------ Lịch & cài đặt

        public void SetPaused(bool paused)
        {
            _settings.Paused = paused;
            SettingsStore.Save(_settings);
            RestartTimers();
            _tray?.Refresh();
        }

        public void SetImageInterval(int minutes)
        {
            _settings.ImageIntervalMinutes = minutes;
            SettingsStore.Save(_settings);
            RestartTimer(_imageTimer, minutes);   // áp dụng từ lượt kế tiếp
        }

        public void SetQuoteInterval(int minutes)
        {
            _settings.QuoteIntervalMinutes = minutes;
            SettingsStore.Save(_settings);
            RestartTimer(_quoteTimer, minutes);
        }

        public void SetQuoteLanguage(string language)
        {
            _settings.QuoteLanguage = language;
            SettingsStore.Save(_settings);
            _quoteBag.SetItems(_quotes.Active(language));
            _tray?.Refresh();
        }

        public async Task SetFitModeAsync(string fitMode)
        {
            _settings.FitMode = fitMode;
            SettingsStore.Save(_settings);
            await ChangeAsync(Change.Rerender, auto: false);
        }

        public void SetChangeOnStartup(bool value)
        {
            _settings.ChangeOnStartup = value;
            SettingsStore.Save(_settings);
        }

        private void RestartTimers()
        {
            RestartTimer(_imageTimer, _settings.ImageIntervalMinutes);
            RestartTimer(_quoteTimer, _settings.QuoteIntervalMinutes);
        }

        private void RestartTimer(DispatcherTimer timer, int minutes)
        {
            timer.Stop();
            timer.Interval = TimeSpan.FromMinutes(Math.Max(1, minutes));
            if (!_settings.Paused && !_disposed) timer.Start();
        }

        public DateTime? NextImageChange =>
            _settings.Paused || _lastImageChange == DateTime.MinValue ? (DateTime?)null : _lastImageChange.AddMinutes(_settings.ImageIntervalMinutes);

        // ------------------------------------------------------------------ Sự kiện hệ thống

        private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode != PowerModes.Resume) return;
            _dispatcher.InvokeAsync(async () =>
            {
                // Máy ngủ dậy mà đã lỡ lượt → đổi 1 lần ngay rồi tính chu kỳ mới (BR W3.1).
                await RescanAsync();
                if (_settings.Paused) return;
                var now = DateTime.Now;
                var missed = Change.None;
                if (now - _lastImageChange >= TimeSpan.FromMinutes(_settings.ImageIntervalMinutes)) missed |= Change.Image;
                if (now - _lastQuoteChange >= TimeSpan.FromMinutes(_settings.QuoteIntervalMinutes)) missed |= Change.Quote;
                if (missed != Change.None) await ChangeAsync(missed, auto: false);
                else RestartTimers();
            });
        }

        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            // Đổi độ phân giải / cắm thêm màn hình → ghép lại cho đúng kích thước.
            _dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(2000);
                await ChangeAsync(Change.Rerender, auto: true);
            });
        }

        public void Exit()
        {
            // Chạy sau khi menu đóng hẳn, tránh hủy menu ngay trong lúc nó đang xử lý cú bấm.
            // Thoát app giữ nguyên hình nền đang hiển thị.
            _dispatcher.BeginInvoke(new Action(() =>
            {
                Dispose();
                System.Windows.Application.Current?.Shutdown();
            }));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _imageTimer.Stop();
            _quoteTimer.Stop();
            _rescanDebounce.Stop();
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            foreach (var w in _watchers) w.Dispose();
            _watchers.Clear();
            _tray?.Dispose();
            _tray = null;
            Log.Info("Thoát app");
        }
    }
}
