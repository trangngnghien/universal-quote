using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.Win32;
using WallpaperQuote.Core;
using WallpaperQuote.Platform;
using WallpaperQuote.UI;
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
    /// Điều phối toàn bộ app: nguồn ảnh (W1), quote (W2), lịch xoay & làm mới (W3),
    /// ghép và đặt hình nền (W4), đồng bộ online (G3). Mọi thao tác chạy trên luồng giao diện.
    /// </summary>
    public sealed class AppHost : IDisposable
    {
        public const string Version = "0.4.1";

        private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
        private readonly ShuffleBag<string> _imageBag = new ShuffleBag<string>(StringComparer.OrdinalIgnoreCase);
        private readonly ShuffleBag<Quote> _quoteBag = new ShuffleBag<Quote>();
        private readonly DispatcherTimer _imageTimer = new DispatcherTimer();
        private readonly DispatcherTimer _quoteTimer = new DispatcherTimer();
        private readonly DispatcherTimer _hourly = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        private readonly DispatcherTimer _rescanDebounce = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        private readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();

        private AppSettings _settings = new AppSettings();
        private QuoteLibrary _quotes = null!;
        private OnlineImageStore _online = null!;
        private TrayController? _tray;
        private SettingsWindow? _window;
        private List<string> _folderImages = new List<string>();
        private ScanResult? _lastScan;
        private string? _currentImage;
        private Quote? _currentQuote;
        private DateTime _lastImageChange = DateTime.MinValue;
        private DateTime _lastQuoteChange = DateTime.MinValue;
        private DateTime _lastSyncAttempt = DateTime.MinValue;
        private int _syncAttemptsToday;
        private DateTime _syncAttemptDay = DateTime.Today;
        private bool _busy;
        private bool _syncing;
        private Change _pendingAuto = Change.None;
        private int _slot;
        private bool _noImageNotified;
        private bool _disposed;
        private bool _editingPosition;
        private EffectsWindow? _effects;
        private bool _effectsFailedNotified;
        private string? _currentWallpaperFile;

        /// <summary>Báo cho cửa sổ Cài đặt và tray cập nhật lại hiển thị.</summary>
        public event Action? StateChanged;

        public AppSettings Settings => _settings;
        public QuoteLibrary Quotes => _quotes;
        public OnlineImageStore OnlineImages => _online;
        public bool IsPaused => _settings.Paused;
        public bool IsSyncing => _syncing;
        public int ImageCount => _imageBag.Count;
        public int QuoteCount => _quoteBag.Count;
        public string? CurrentImage => _currentImage;
        public Quote? CurrentQuote => _currentQuote;
        public ScanResult? LastScan => _lastScan;

        // ================================================================== Khởi động

        public void Start(bool autostart)
        {
            _settings = SettingsStore.Load();
            SettingsStore.Save(_settings);   // lưu lại phần nâng cấp cấu hình (nếu có)
            _quotes = new QuoteLibrary();
            _online = new OnlineImageStore();
            Log.Info($"Khởi động app {Version}{(autostart ? " (cùng Windows)" : "")}");

            if (!_settings.OriginalWallpaperSaved)
            {
                _settings.OriginalWallpaper = WallpaperSetter.GetCurrent();
                _settings.OriginalWallpaperSaved = true;
                SettingsStore.Save(_settings);
            }

            RebuildQuoteBag();

            _imageTimer.Tick += async (_, __) => await ChangeAsync(Change.Image, auto: true);
            _quoteTimer.Tick += async (_, __) => await ChangeAsync(Change.Quote, auto: true);
            _rescanDebounce.Tick += async (_, __) =>
            {
                _rescanDebounce.Stop();
                await RescanAsync();
            };
            _hourly.Tick += async (_, __) => await BackgroundWorkAsync();
            _hourly.Start();

            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

            _tray = new TrayController(this);
            _tray.Show();

            EnsureDesktop();
            _ = InitialAsync(openSettings: !autostart);
        }

        /// <summary>
        /// Không tự động can thiệp desktop (tạo lối tắt, bật biểu tượng) để tránh bị Windows nghi ngờ.
        /// Người dùng tự bấm trong Cài đặt → tab Chung. Chỉ ghi nhật ký nếu biểu tượng desktop đang bị ẩn.
        /// </summary>
        private void EnsureDesktop()
        {
            if (!_settings.DesktopIconsChecked)
            {
                _settings.DesktopIconsChecked = true;
                if (DesktopIntegration.IconsHidden()) Log.Info("Biểu tượng desktop đang bị Windows ẩn (người dùng có thể bật trong tab Chung)");
                SettingsStore.Save(_settings);
            }
        }

        private async Task InitialAsync(bool openSettings)
        {
            await RescanAsync();

            if (!_settings.WelcomeShown)
            {
                _settings.WelcomeShown = true;
                SettingsStore.Save(_settings);
                _tray?.Notify("Hình nền & Quote đang chạy",
                    "Hình nền tự đổi theo lịch. Nháy đúp icon dưới khay để làm mới ngay. Chuột phải icon → Cài đặt để mở lại cửa sổ này.");
            }

            if (openSettings) OpenSettings();

            if (!_settings.Paused && (_settings.ChangeOnStartup || _currentImage == null))
                await ChangeAsync(Change.All, auto: false, resetTimers: true);
            else
                RestartTimers();

            ApplyEffects();
            await BackgroundWorkAsync();
        }

        public void OpenSettings()
        {
            if (_disposed) return;
            if (_window == null)
            {
                _window = new SettingsWindow(this);
                _window.Closed += (_, __) => _window = null;
                _window.Show();
            }
            if (_window.WindowState == System.Windows.WindowState.Minimized)
                _window.WindowState = System.Windows.WindowState.Normal;
            _window.Activate();
            _window.Topmost = true;
            _window.Topmost = false;
            _window.Focus();
        }

        private void NotifyChanged()
        {
            _tray?.Refresh();
            StateChanged?.Invoke();
        }

        // ================================================================== Làm mới

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
                NotifyChanged();
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
            var changeImage = change.HasFlag(Change.Image) || image == null || !File.Exists(image);
            var changeQuote = change.HasFlag(Change.Quote) || quote == null;

            if (changeImage) image = PickImage();
            if (changeQuote) quote = _quoteBag.Next();

            var screen = (Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens.First()).Bounds;
            var width = Math.Max(640, screen.Width);
            var height = Math.Max(480, screen.Height);
            var output = Paths.WallpaperFile(_slot ^ 1);

            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    var request = new RenderRequest(image, quote, width, height, _settings.FitMode, _settings.QuotePosX, _settings.QuotePosY);
                    await Sta.Run(() => WallpaperRenderer.RenderToFile(request, output));
                    break;
                }
                catch (ImageDecodeException ex) when (attempt < 5)
                {
                    // Ảnh hỏng hoặc đã bị xóa → loại khỏi kho, chọn ảnh khác.
                    Log.Warn(ex.Message);
                    DropImage(ex.ImagePath);
                    image = PickImage();
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
            _currentWallpaperFile = output;
            if (_effects != null) _effects.SetBackground(output);
            _currentImage = image;
            _currentQuote = quote;
            if (changeImage) _lastImageChange = DateTime.Now;
            if (changeQuote) _lastQuoteChange = DateTime.Now;
        }

        private string? PickImage()
        {
            for (var i = 0; i < 50; i++)
            {
                var next = _imageBag.Next();
                if (next == null) break;
                if (File.Exists(next)) return next;
                DropImage(next);
            }

            if (!_noImageNotified)
            {
                _noImageNotified = true;
                _tray?.Notify("Chưa có ảnh để hiển thị",
                    "Mở Cài đặt → tab Ảnh để thêm thư mục, link ảnh, hoặc bật ảnh online (cần mạng). Tạm thời quote hiện trên nền tối.",
                    warning: true);
            }
            return null;
        }

        private void DropImage(string path)
        {
            _folderImages.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            if (_online.FindByFile(path) != null && !File.Exists(path)) _online.Remove(path);
            RebuildImageBag();
        }

        // ================================================================== Kho ảnh

        /// <summary>Gộp ảnh từ thư mục + link + online (nguồn đang bật) thành kho random.</summary>
        private void RebuildImageBag()
        {
            var pool = new List<string>(_folderImages);
            pool.AddRange(_settings.Links
                .Where(l => l.Status == LinkStatus.Ready && l.File != null && File.Exists(l.File))
                .Select(l => l.File!));
            pool.AddRange(_online.Items
                .Where(i => (i.Source == OnlineSource.Nasa && _settings.NasaEnabled)
                         || (i.Source == OnlineSource.Wikimedia && _settings.WikimediaEnabled))
                .Where(i => File.Exists(i.File))
                .Select(i => i.File));
            var distinct = pool.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            _imageBag.SetItems(distinct);
            if (distinct.Count > 0) _noImageNotified = false;
        }

        public async Task RescanAsync()
        {
            var folders = _settings.Folders.ToList();
            var recursive = _settings.ScanSubfolders;
            var minWidth = _settings.MinImageWidth;
            try
            {
                var result = await Sta.Run(() => ImageScanner.Scan(folders, recursive, minWidth, ImageProbe.Width));
                _lastScan = result;
                _folderImages = result.Ready;
                Log.Info($"Quét ảnh: {result.Ready.Count} sẵn sàng, {result.TooSmall} nhỏ hơn {minWidth}px, {result.Heic} HEIC chưa đọc được, {result.Unreadable} lỗi");
            }
            catch (Exception ex)
            {
                Log.Error("Lỗi quét thư mục ảnh", ex);
            }
            RebuildImageBag();
            RebuildWatchers();
            NotifyChanged();
        }

        // ------------------------------------------------------------------ Thư mục

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

            var count = _lastScan != null && _lastScan.PerFolder.TryGetValue(full, out var n) ? n : 0;
            if (count == 0)
            {
                var heic = _lastScan?.Heic ?? 0;
                var hint = heic > 0
                    ? "Ảnh .HEIC (iPhone) cần cài \"HEIF Image Extensions\" từ Microsoft Store."
                    : $"Cần ảnh jpg/png/webp/bmp rộng từ {_settings.MinImageWidth}px.";
                _tray?.Notify("Không có ảnh dùng được trong thư mục này", hint, warning: true);
            }
            else if (_currentImage == null || _imageBag.Count == count)
            {
                await ChangeAsync(Change.Image, auto: false);
            }
        }

        public async Task RemoveFolderAsync(string folder)
        {
            _settings.Folders.RemoveAll(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
            SettingsStore.Save(_settings);
            await RescanAsync();
        }

        /// <summary>Dùng cả ảnh rộng dưới 800px (ảnh sẽ bị phóng to, có thể mờ).</summary>
        public async Task SetAllowSmallImagesAsync(bool allow)
        {
            _settings.MinImageWidth = allow ? 0 : 800;
            SettingsStore.Save(_settings);
            await RescanAsync();
            if (allow && _settings.Folders.Count > 0) await ChangeAsync(Change.Image, auto: false);
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
            _rescanDebounce.Stop();
            _rescanDebounce.Start();
        }

        // ------------------------------------------------------------------ Link ảnh (W1.2)

        /// <summary>Thêm 1 hoặc nhiều link (mỗi dòng 1 link). Trả về câu báo kết quả để hiện trên cửa sổ.</summary>
        public async Task<string> AddLinksAsync(string text)
        {
            var lines = (text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            if (lines.Count == 0) return "Hãy dán ít nhất 1 link ảnh.";

            var added = new List<ImageLink>();
            var invalid = 0;
            var duplicate = 0;
            foreach (var line in lines)
            {
                if (!Uri.TryCreate(line, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    invalid++;
                    continue;
                }
                if (_settings.Links.Any(l => string.Equals(l.Url, line, StringComparison.OrdinalIgnoreCase)) || added.Any(l => l.Url == line))
                {
                    duplicate++;
                    continue;
                }
                added.Add(new ImageLink { Url = line, Status = LinkStatus.Pending });
            }
            _settings.Links.AddRange(added);
            SettingsStore.Save(_settings);
            NotifyChanged();

            foreach (var link in added) await DownloadLinkAsync(link);

            var parts = new List<string>();
            if (added.Count > 0) parts.Add($"Đã thêm {added.Count} link");
            if (invalid > 0) parts.Add($"{invalid} dòng không phải link http/https");
            if (duplicate > 0) parts.Add($"{duplicate} link đã tồn tại");
            return string.Join(" · ", parts);
        }

        public async Task RetryLinkAsync(string url)
        {
            var link = _settings.Links.FirstOrDefault(l => l.Url == url);
            if (link == null) return;
            link.FailCount = 0;
            await DownloadLinkAsync(link);
        }

        public void RemoveLink(string url)
        {
            var link = _settings.Links.FirstOrDefault(l => l.Url == url);
            if (link == null) return;
            _settings.Links.Remove(link);
            if (link.File != null && !string.Equals(link.File, _currentImage, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(link.File); } catch { }
            }
            SettingsStore.Save(_settings);
            RebuildImageBag();
            NotifyChanged();
        }

        private async Task DownloadLinkAsync(ImageLink link)
        {
            link.Status = LinkStatus.Pending;
            link.Error = null;
            NotifyChanged();
            try
            {
                var dest = Path.Combine(Paths.LinkCacheDir, Net.HashName(link.Url));
                var file = await Net.DownloadImageAsync(link.Url, dest, CancellationToken.None);
                var width = ImageProbe.Width(file);
                if (width == null) throw new DownloadException("File tải về không phải ảnh đọc được");
                if (width.Value < _settings.MinImageWidth)
                {
                    try { File.Delete(file); } catch { }
                    throw new DownloadException($"Ảnh rộng {width}px, nhỏ hơn {_settings.MinImageWidth}px");
                }
                link.File = file;
                link.Status = LinkStatus.Ready;
                link.FailCount = 0;
            }
            catch (DownloadException ex) when (ex.Offline)
            {
                link.Status = LinkStatus.Pending;
                link.Error = "Đang offline · sẽ tự tải khi có mạng";
            }
            catch (Exception ex)
            {
                link.FailCount++;
                link.Status = LinkStatus.Error;
                link.Error = ex is DownloadException ? ex.Message : "Không tải được ảnh";
                Log.Warn($"Link ảnh lỗi: {link.Error}");
            }
            SettingsStore.Save(_settings);
            RebuildImageBag();
            NotifyChanged();

            if (link.Status == LinkStatus.Ready && _currentImage == null) await ChangeAsync(Change.Image, auto: false);
        }

        // ------------------------------------------------------------------ Online (W1.3, W2.2)

        public void SetNasaEnabled(bool value)
        {
            _settings.NasaEnabled = value;
            SettingsStore.Save(_settings);
            RebuildImageBag();
            NotifyChanged();
            if (value && _online.Count(OnlineSource.Nasa) == 0) _ = SyncOnlineAsync(force: true);
        }

        public void SetWikimediaEnabled(bool value)
        {
            _settings.WikimediaEnabled = value;
            SettingsStore.Save(_settings);
            RebuildImageBag();
            NotifyChanged();
            if (value && _online.Count(OnlineSource.Wikimedia) == 0) _ = SyncOnlineAsync(force: true);
        }

        public void SetNasaApiKey(string key)
        {
            _settings.NasaApiKey = string.IsNullOrWhiteSpace(key) ? "DEMO_KEY" : key.Trim();
            SettingsStore.Save(_settings);
            NotifyChanged();
            if (_settings.NasaEnabled) _ = SyncOnlineAsync(force: true);
        }

        /// <summary>Việc nền định kỳ: đồng bộ online 1 lần/ngày, tải lại link đang chờ.</summary>
        private async Task BackgroundWorkAsync()
        {
            if (_disposed) return;
            ApplyEffects();
            if (_settings.Links.Any(l => l.Status == LinkStatus.Pending) && Net.IsOnline())
            {
                foreach (var link in _settings.Links.Where(l => l.Status == LinkStatus.Pending).ToList())
                    await DownloadLinkAsync(link);
            }
            await SyncOnlineAsync(force: false);
        }

        /// <summary>
        /// Đồng bộ ảnh NASA, Wikimedia và quote online theo lô (tối đa 1 lần/ngày, BR W1.3).
        /// Lỗi mạng → thử lại sau 30 phút, tối đa 3 lần/ngày; không hiện popup lỗi.
        /// </summary>
        public async Task SyncOnlineAsync(bool force)
        {
            if (_syncing || _disposed) return;
            if (!_settings.NasaEnabled && !_settings.WikimediaEnabled && !_settings.UseOnlineQuotes) return;

            if (_syncAttemptDay != DateTime.Today) { _syncAttemptDay = DateTime.Today; _syncAttemptsToday = 0; }
            if (!force)
            {
                if (_settings.LastOnlineSync.HasValue && DateTime.Now - _settings.LastOnlineSync.Value < TimeSpan.FromHours(24)) return;
                if (_syncAttemptsToday >= 3) return;
                if (DateTime.Now - _lastSyncAttempt < TimeSpan.FromMinutes(30)) return;
            }
            if (!Net.IsOnline())
            {
                _settings.LastOnlineSyncMessage = "Đang offline · sẽ đồng bộ khi có mạng";
                NotifyChanged();
                return;
            }

            _syncing = true;
            _lastSyncAttempt = DateTime.Now;
            _syncAttemptsToday++;
            NotifyChanged();

            var parts = new List<string>();
            var anyOk = false;
            var allOk = true;
            const int perSource = 10;
            try
            {
                if (_settings.NasaEnabled)
                {
                    try
                    {
                        var skip = new HashSet<string>(_online.Items.Select(i => i.Id));
                        var fetched = await Net.FetchNasaLibraryAsync(perSource, skip, CancellationToken.None);
                        var added = await DownloadCandidatesAsync(fetched.Accepted, perSource);
                        var notes = new List<string>();
                        if (fetched.RejectedLicense > 0) notes.Add($"loại {fetched.RejectedLicense} ảnh có bản quyền");
                        
                        parts.Add($"NASA +{added} ảnh" + (notes.Count > 0 ? $" ({string.Join(", ", notes)})" : ""));
                        Log.Info($"NASA Image Library: {fetched.Accepted.Count} ảnh mới, tải được {added}, {fetched.RejectedLicense} có ghi chú bản quyền");
                        anyOk = true;
                    }
                    catch (RateLimitException)
                    {
                        parts.Add("NASA: vượt hạn mức, thử lại sau");
                        anyOk = true;
                    }
                    catch (DownloadException ex)
                    {
                        parts.Add("NASA: " + ex.Message);
                        allOk = false;
                        Log.Warn($"Đồng bộ NASA lỗi: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        parts.Add("NASA: máy chủ phản hồi chậm hoặc lỗi kết nối, sẽ thử lại sau 30 phút");
                        allOk = false;
                        Log.Warn($"Đồng bộ NASA lỗi: {ex.Message}");
                    }
                }

                if (_settings.WikimediaEnabled)
                {
                    try
                    {
                        var skip = new HashSet<string>(_online.Items.Select(i => i.Id));
                        var fetched = await Net.FetchWikimediaAsync(30, skip, CancellationToken.None);
                        var added = await DownloadCandidatesAsync(fetched.Accepted, perSource);
                        parts.Add($"Wikimedia +{added} ảnh" + (fetched.RejectedLicense > 0 ? $" (loại {fetched.RejectedLicense} ảnh không phải PD/CC0)" : ""));
                        anyOk = true;
                    }
                    catch (RateLimitException)
                    {
                        parts.Add("Wikimedia: vượt hạn mức, thử lại sau");
                        anyOk = true;
                    }
                    catch (Exception ex)
                    {
                        parts.Add("Wikimedia: lỗi kết nối, sẽ thử lại sau 30 phút");
                        allOk = false;
                        Log.Warn($"Đồng bộ Wikimedia lỗi: {ex.Message}");
                    }
                }

                if (_settings.UseOnlineQuotes)
                {
                    try
                    {
                        var catalog = await Net.FetchQuoteCatalogAsync(CancellationToken.None);
                        if (catalog != null)
                        {
                            if (_quotes.ApplyOnlineCatalog(catalog)) RebuildQuoteBag();
                            _settings.LastQuoteSync = DateTime.Now;
                            parts.Add($"{_quotes.Online.Count} quote online");
                            anyOk = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"Đồng bộ quote online lỗi: {ex.Message}");
                    }
                }

                if (anyOk && allOk) _settings.LastOnlineSync = DateTime.Now;
                _settings.LastOnlineSyncMessage = $"Đồng bộ lúc {DateTime.Now:HH:mm dd/MM}: " + string.Join(" · ", parts);
                _online.Evict((long)_settings.CacheLimitMb * 1024 * 1024, _currentImage,
                    _settings.Links.Where(l => l.File != null).Select(l => l.File!));
                SettingsStore.Save(_settings);
                RebuildImageBag();
            }
            finally
            {
                _syncing = false;
                NotifyChanged();
            }

            if (_currentImage == null && _imageBag.Count > 0) await ChangeAsync(Change.Image, auto: false);
        }

        private async Task<int> DownloadCandidatesAsync(List<OnlineCandidate> candidates, int max)
        {
            var added = 0;
            foreach (var c in candidates)
            {
                if (added >= max) break;
                if (_online.Contains(c.Id)) continue;
                try
                {
                    var dest = Path.Combine(Paths.OnlineCacheDir, Net.HashName(c.Id));
                    var file = await TryDownloadAsync(c.ImageUrl, dest) ?? (c.FallbackUrl != null ? await TryDownloadAsync(c.FallbackUrl, dest) : null);
                    if (file == null) continue;
                    _online.Add(new OnlineImage
                    {
                        Id = c.Id,
                        Source = c.Source,
                        File = file,
                        Title = c.Title,
                        Author = c.Author,
                        License = c.License,
                        PageUrl = c.PageUrl,
                        Date = c.Date
                    });
                    added++;
                }
                catch (Exception ex)
                {
                    Log.Warn($"Không tải được ảnh online {c.Id}: {ex.Message}");
                }
            }
            return added;
        }

        /// <summary>Tải 1 ảnh online và kiểm tra độ rộng. Trả null nếu lỗi hoặc ảnh quá nhỏ.</summary>
        private async Task<string?> TryDownloadAsync(string url, string dest)
        {
            try
            {
                var file = await Net.DownloadImageAsync(url, dest, CancellationToken.None);
                var width = ImageProbe.Width(file);
                if (width != null && width.Value >= _settings.MinImageWidth) return file;
                Log.Warn($"Ảnh online quá nhỏ hoặc không đọc được ({width?.ToString() ?? "?"}px): {url}");
                try { File.Delete(file); } catch { }
            }
            catch (Exception ex)
            {
                Log.Warn($"Không tải được ảnh online {url}: {ex.Message}");
            }
            return null;
        }

        public void ClearCache()
        {
            _online.Clear(_currentImage);
            RebuildImageBag();
            NotifyChanged();
        }

        // ================================================================== Quote

        private void RebuildQuoteBag() => _quoteBag.SetItems(_quotes.Active(_settings));

        public void SetQuoteLanguage(string language)
        {
            _settings.QuoteLanguage = language;
            SettingsStore.Save(_settings);
            RebuildQuoteBag();
            NotifyChanged();
        }

        public void SetQuoteGroup(string group, bool enabled)
        {
            if (group == QuoteGroup.BuiltIn) _settings.UseBuiltInQuotes = enabled;
            else if (group == QuoteGroup.User) _settings.UseUserQuotes = enabled;
            else if (group == QuoteGroup.Online) _settings.UseOnlineQuotes = enabled;
            SettingsStore.Save(_settings);
            RebuildQuoteBag();
            NotifyChanged();
            if (!_settings.UseBuiltInQuotes && !_settings.UseUserQuotes && !_settings.UseOnlineQuotes)
                _tray?.Notify("Hình nền sẽ không có quote", "Bạn đã tắt mọi nhóm quote.", warning: true);
            if (group == QuoteGroup.Online && enabled && _quotes.Online.Count == 0) _ = SyncOnlineAsync(force: true);
        }

        public void AddUserQuote(string text, string? author, string? source, string lang)
        {
            _quotes.AddUser(text, author, source, lang);
            RebuildQuoteBag();
            NotifyChanged();
        }

        public BulkImportResult ImportUserQuotes(string content, string defaultLang, bool csv)
        {
            var result = _quotes.AddUserBulk(content, defaultLang, csv);
            if (result.Added > 0 && !_settings.UseUserQuotes)
            {
                _settings.UseUserQuotes = true;   // vừa nhập quote cá nhân thì bật nhóm này
                SettingsStore.Save(_settings);
            }
            RebuildQuoteBag();
            NotifyChanged();
            return result;
        }

        public void UpdateUserQuote(string id, string text, string? author, string? source, string lang)
        {
            _quotes.UpdateUser(id, text, author, source, lang);
            RebuildQuoteBag();
            NotifyChanged();
        }

        public void DeleteUserQuote(string id)
        {
            _quotes.DeleteUser(id);
            RebuildQuoteBag();
            NotifyChanged();
        }

        // ================================================================== Lịch & cài đặt

        public void SetPaused(bool paused)
        {
            _settings.Paused = paused;
            SettingsStore.Save(_settings);
            RestartTimers();
            NotifyChanged();
        }

        public void SetImageInterval(int minutes)
        {
            _settings.ImageIntervalMinutes = minutes;
            SettingsStore.Save(_settings);
            RestartTimer(_imageTimer, minutes);
            NotifyChanged();
        }

        public void SetQuoteInterval(int minutes)
        {
            _settings.QuoteIntervalMinutes = minutes;
            SettingsStore.Save(_settings);
            RestartTimer(_quoteTimer, minutes);
            NotifyChanged();
        }

        public void SetChangeOnStartup(bool value)
        {
            _settings.ChangeOnStartup = value;
            SettingsStore.Save(_settings);
            NotifyChanged();
        }

        public async Task SetFitModeAsync(string fitMode)
        {
            _settings.FitMode = fitMode;
            SettingsStore.Save(_settings);
            await ChangeAsync(Change.Rerender, auto: false);
        }

        /// <summary>
        /// Mở chế độ chỉnh vị trí quote toàn màn hình: hiện hình nền hiện tại (chưa có quote),
        /// người dùng kéo khung quote bằng chuột rồi Lưu → vẽ lại hình nền.
        /// </summary>
        public async Task EditQuotePositionAsync()
        {
            if (_editingPosition || _disposed) return;
            _editingPosition = true;
            try
            {
                var screen = (Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens.First()).Bounds;
                var width = Math.Max(640, screen.Width);
                var height = Math.Max(480, screen.Height);
                var preview = Path.Combine(Paths.LocalDir, "preview.jpg");
                var image = _currentImage != null && File.Exists(_currentImage) ? _currentImage : null;
                try
                {
                    var request = new RenderRequest(image, null, width, height, _settings.FitMode);
                    await Sta.Run(() => WallpaperRenderer.RenderToFile(request, preview));
                }
                catch (Exception ex)
                {
                    Log.Warn($"Không vẽ được ảnh xem trước, dùng nền tối: {ex.Message}");
                    var request = new RenderRequest(null, null, width, height, _settings.FitMode);
                    await Sta.Run(() => WallpaperRenderer.RenderToFile(request, preview));
                }

                var quote = _currentQuote ?? new Quote { Text = "Có công mài sắt, có ngày nên kim.", Author = "Tục ngữ" };
                var editor = new QuotePositionWindow(preview, quote, _settings.QuotePosX, _settings.QuotePosY);
                if (editor.ShowDialog() == true)
                {
                    _settings.QuotePosX = editor.ResultX;
                    _settings.QuotePosY = editor.ResultY;
                    SettingsStore.Save(_settings);
                    await ChangeAsync(Change.Rerender, auto: false);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Lỗi chỉnh vị trí quote", ex);
            }
            finally
            {
                _editingPosition = false;
                NotifyChanged();
            }
        }

        public async Task ResetQuotePositionAsync()
        {
            _settings.QuotePosX = null;
            _settings.QuotePosY = null;
            SettingsStore.Save(_settings);
            await ChangeAsync(Change.Rerender, auto: false);
        }

        // ================================================================== Hiệu ứng động (W7)

        public bool EffectsPausedForBattery =>
            _settings.EffectPauseOnBattery && Forms.SystemInformation.PowerStatus.PowerLineStatus == Forms.PowerLineStatus.Offline;

        public void SetEffect(string effect)
        {
            _settings.Effect = effect;
            SettingsStore.Save(_settings);
            ApplyEffects();
            NotifyChanged();
        }

        public void SetEffectIntensity(string intensity)
        {
            _settings.EffectIntensity = intensity;
            SettingsStore.Save(_settings);
            ApplyEffects();
            NotifyChanged();
        }

        public void SetEffectPauseOnBattery(bool value)
        {
            _settings.EffectPauseOnBattery = value;
            SettingsStore.Save(_settings);
            ApplyEffects();
            NotifyChanged();
        }

        /// <summary>Thả một đợt hạt rơi dày trong 10 giây (cách B), kể cả khi hiệu ứng liên tục đang tắt.</summary>
        public void BurstEffect(string? kind = null)
        {
            var type = kind ?? (_settings.Effect == EffectType.None ? EffectType.Snow : _settings.Effect);
            if (!EnsureEffectsWindow()) return;
            _effects!.Particles.Burst(type, TimeSpan.FromSeconds(10));
        }

        /// <summary>Bật/tắt lớp hiệu ứng liên tục theo cài đặt (cách A).</summary>
        private void ApplyEffects()
        {
            if (_disposed) return;
            var wantContinuous = _settings.Effect != EffectType.None && !EffectsPausedForBattery;
            if (wantContinuous)
            {
                if (!EnsureEffectsWindow()) return;
                _effects!.Particles.Configure(_settings.Effect, _settings.EffectIntensity);
            }
            else if (_effects != null)
            {
                _effects.Particles.Configure(EffectType.None, _settings.EffectIntensity);   // hạt đang rơi rơi nốt rồi tự ẩn
            }
        }

        private bool EnsureEffectsWindow()
        {
            if (_effects != null && _effects.IsAlive) return true;
            CloseEffectsWindow(restoreWallpaper: false);
            try
            {
                var window = new EffectsWindow();
                window.SetBackground(_currentWallpaperFile ?? WallpaperSetter.GetCurrent());
                if (!window.AttachToDesktop())
                {
                    window.Close();
                    Log.Warn("Không tìm thấy lớp desktop để hiện hiệu ứng");
                    if (!_effectsFailedNotified)
                    {
                        _effectsFailedNotified = true;
                        _tray?.Notify("Không bật được hiệu ứng", "Windows chưa cho gắn lớp hiệu ứng vào desktop. Thử khởi động lại máy rồi bật lại.", warning: true);
                    }
                    return false;
                }
                window.Particles.Idle += () => _dispatcher.InvokeAsync(() =>
                {
                    if (_settings.Effect == EffectType.None || EffectsPausedForBattery) CloseEffectsWindow(restoreWallpaper: true);
                });
                _effects = window;
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Lỗi tạo lớp hiệu ứng", ex);
                return false;
            }
        }

        private void CloseEffectsWindow(bool restoreWallpaper)
        {
            if (_effects == null) return;
            try
            {
                _effects.Particles.Stop();
                _effects.Close();
            }
            catch (Exception ex)
            {
                Log.Warn($"Đóng lớp hiệu ứng lỗi: {ex.Message}");
            }
            _effects = null;
            // Đặt lại hình nền để Windows vẽ lại vùng desktop vừa được giải phóng.
            if (restoreWallpaper && _currentWallpaperFile != null && File.Exists(_currentWallpaperFile))
            {
                try { WallpaperSetter.Set(_currentWallpaperFile); } catch { }
            }
        }

        public bool SetStartup(bool enabled)
        {
            var ok = StartupRegistration.Set(enabled);
            NotifyChanged();
            return ok;
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

        public DateTime? NextQuoteChange =>
            _settings.Paused || _lastQuoteChange == DateTime.MinValue ? (DateTime?)null : _lastQuoteChange.AddMinutes(_settings.QuoteIntervalMinutes);

        /// <summary>Mô tả nguồn của ảnh đang hiển thị (thư mục / link / online + giấy phép).</summary>
        public string DescribeCurrentImage()
        {
            var image = _currentImage;
            if (image == null) return "Chưa có ảnh (đang dùng nền tối)";
            var online = _online.FindByFile(image);
            if (online != null)
            {
                var src = online.Source == OnlineSource.Nasa ? "NASA – Ảnh thiên văn trong ngày" : "Wikimedia Commons – Ảnh trong ngày";
                return $"{online.Title}\nNguồn: {src}\nTác giả: {online.Author ?? "Không rõ"}\nGiấy phép: {online.License}" +
                       (online.PageUrl != null ? $"\nTrang gốc: {online.PageUrl}" : "");
            }
            var link = _settings.Links.FirstOrDefault(l => string.Equals(l.File, image, StringComparison.OrdinalIgnoreCase));
            if (link != null) return $"Ảnh qua link\n{link.Url}";
            return $"{Path.GetFileName(image)}\nThư mục: {Path.GetDirectoryName(image)}";
        }

        // ================================================================== Sự kiện hệ thống

        private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.StatusChange)
            {
                _dispatcher.InvokeAsync(ApplyEffects);
                return;
            }
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
                await BackgroundWorkAsync();
            });
        }

        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            _dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(2000);
                await ChangeAsync(Change.Rerender, auto: true);
                _effects?.Reposition();
            });
        }

        public void Exit()
        {
            // Chạy sau khi menu đóng hẳn. Thoát app giữ nguyên hình nền đang hiển thị.
            _dispatcher.BeginInvoke(new Action(() =>
            {
                _window?.Close();
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
            _hourly.Stop();
            _rescanDebounce.Stop();
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            foreach (var w in _watchers) w.Dispose();
            _watchers.Clear();
            CloseEffectsWindow(restoreWallpaper: true);
            _tray?.Dispose();
            _tray = null;
            Log.Info("Thoát app");
        }
    }
}
