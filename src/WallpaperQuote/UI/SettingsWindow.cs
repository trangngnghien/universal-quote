using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WallpaperQuote.Core;
using WallpaperQuote.Platform;
using Forms = System.Windows.Forms;

namespace WallpaperQuote.UI
{
    /// <summary>Cửa sổ Cài đặt 4 tab: Ảnh / Quote / Lịch xoay / Chung (màn S03–S06).</summary>
    public sealed class SettingsWindow : Window
    {
        private static readonly (int Minutes, string Label)[] Intervals =
        {
            (5, "5 phút"), (15, "15 phút"), (30, "30 phút"), (60, "1 giờ"),
            (180, "3 giờ"), (360, "6 giờ"), (720, "12 giờ"), (1440, "24 giờ")
        };

        private readonly AppHost _host;
        private bool _updating;

        // Tab Ảnh
        private readonly CheckBox _subfolders = new CheckBox { Content = "Quét cả thư mục con", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        private readonly StackPanel _folderList = new StackPanel();
        private readonly TextBlock _skipNote = Ui.Text("", 12, color: Ui.Muted);
        private readonly TextBox _linkInput = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            Height = 64,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(6),
            ToolTip = "Dán 1 hoặc nhiều link ảnh, mỗi dòng một link"
        };
        private readonly TextBlock _linkMessage = Ui.Text("", 12, color: Ui.Muted);
        private readonly StackPanel _linkList = new StackPanel();
        private readonly CheckBox _nasa = new CheckBox { VerticalAlignment = VerticalAlignment.Center };
        private readonly CheckBox _wiki = new CheckBox { VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBox _nasaKey = new TextBox { Width = 280, Padding = new Thickness(4), VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _syncMessage = Ui.Text("", 12, color: Ui.Muted);

        // Tab Quote
        private readonly RadioButton _langVi = new RadioButton { Content = "Tiếng Việt", GroupName = "lang", Margin = new Thickness(10, 0, 0, 0) };
        private readonly RadioButton _langEn = new RadioButton { Content = "Tiếng Anh", GroupName = "lang", Margin = new Thickness(10, 0, 0, 0) };
        private readonly RadioButton _langAll = new RadioButton { Content = "Cả hai", GroupName = "lang", Margin = new Thickness(10, 0, 0, 0) };
        private readonly CheckBox _gBuiltIn = new CheckBox { Margin = new Thickness(0, 0, 24, 0) };
        private readonly CheckBox _gUser = new CheckBox { Margin = new Thickness(0, 0, 24, 0) };
        private readonly CheckBox _gOnline = new CheckBox();
        private readonly TextBox _search = new TextBox { Padding = new Thickness(4), ToolTip = "Tìm theo nội dung, tác giả hoặc nguồn" };
        private readonly ComboBox _groupFilter = new ComboBox { Width = 150, Margin = new Thickness(8, 0, 0, 0) };
        private readonly TextBlock _quoteCount = Ui.Text("", 12, color: Ui.Muted);
        private readonly StackPanel _quoteList = new StackPanel();

        // Tab Lịch
        private readonly ComboBox _imageInterval = new ComboBox { Width = 160, HorizontalAlignment = HorizontalAlignment.Left };
        private readonly ComboBox _quoteInterval = new ComboBox { Width = 160, HorizontalAlignment = HorizontalAlignment.Left };
        private readonly CheckBox _changeOnStartup = new CheckBox { Content = "Đổi ảnh và quote mỗi khi mở app / khởi động máy", Margin = new Thickness(0, 10, 0, 0) };
        private readonly TextBlock _nextInfo = Ui.Text("", 12, color: Ui.Muted);
        private readonly TextBlock _stateText = Ui.Text("", 14, bold: true);
        private readonly Button _pauseButton;

        // Tab Chung
        private readonly CheckBox _startup = new CheckBox { Content = "Khởi động cùng Windows", FontWeight = FontWeights.SemiBold };
        private readonly RadioButton _fitFill = new RadioButton { Content = "Lấp đầy (cắt bớt mép)", GroupName = "fit", Margin = new Thickness(0, 0, 20, 0) };
        private readonly RadioButton _fitFit = new RadioButton { Content = "Vừa khung (có viền)", GroupName = "fit", Margin = new Thickness(0, 0, 20, 0) };
        private readonly RadioButton _fitStretch = new RadioButton { Content = "Kéo giãn", GroupName = "fit" };
        private readonly TextBlock _iconsText = Ui.Text("", 12, color: Ui.Muted);
        private readonly TextBlock _cacheText = Ui.Text("", 12, color: Ui.Muted);

        private readonly TextBlock _footer = Ui.Text("", 13, color: Ui.Muted, wrap: false);

        public SettingsWindow(AppHost host)
        {
            _host = host;
            Title = "Hình nền & Quote — Cài đặt";
            Width = 920;
            Height = 720;
            MinWidth = 720;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;
            Background = Brushes.White;

            _pauseButton = Ui.Btn("Tạm dừng", () => _host.SetPaused(!_host.IsPaused));

            var tabs = new TabControl { Margin = new Thickness(12, 12, 12, 0), Padding = new Thickness(0) };
            tabs.Items.Add(Tab("Ảnh", BuildImagesTab()));
            tabs.Items.Add(Tab("Quote", BuildQuotesTab()));
            tabs.Items.Add(Tab("Lịch xoay", BuildScheduleTab()));
            tabs.Items.Add(Tab("Chung", BuildGeneralTab()));

            var footer = new Border
            {
                Background = Ui.Panel,
                BorderBrush = Ui.LineBrush,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(16, 10, 16, 10),
                Child = Ui.Row(_footer, Ui.Btn("Làm mới ngay", () => _ = _host.ChangeAsync(Change.All, auto: false), primary: true))
            };

            var root = new DockPanel();
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);
            root.Children.Add(tabs);
            Content = root;

            WireEvents();
            RefreshAll();
            _host.StateChanged += OnHostChanged;
            Closed += (_, __) => _host.StateChanged -= OnHostChanged;
        }

        private static TabItem Tab(string header, UIElement content) => new TabItem
        {
            Header = new TextBlock { Text = header, FontSize = 14, Padding = new Thickness(10, 4, 10, 4) },
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(20, 16, 20, 16),
                Content = content
            }
        };

        private static UIElement Header(string title, string subtitle) => Ui.Stack(
            Ui.Text(title, 20, bold: true),
            Ui.Text(subtitle, 13, color: Ui.Muted),
            Ui.Spacer(14));

        // ================================================================== Tab Ảnh

        private UIElement BuildImagesTab()
        {
            var keyLink = Ui.Btn("Lấy key miễn phí", () => Shell.OpenUrl("https://api.nasa.gov/"), link: true);

            return Ui.Stack(
                Header("Nguồn ảnh", "Ảnh từ mọi nguồn đang bật được xáo trộn chung."),
                Ui.Card(
                    Ui.Row(Ui.Title("Thư mục trên máy"), _subfolders, Ui.Btn("+ Thêm thư mục", PickFolder)),
                    _folderList,
                    _skipNote),
                Ui.Card(
                    Ui.Title("Ảnh qua đường link"),
                    Ui.Text("Dán link trực tiếp tới file ảnh (http/https), mỗi dòng một link. Link thư mục Google Drive không dùng được; hãy cài Google Drive cho máy tính rồi chọn thư mục Drive ở phần Thư mục trên máy.", 12, color: Ui.Muted),
                    Ui.Spacer(8),
                    Ui.Row(_linkInput, Ui.Btn("Thêm", AddLinks, primary: true)),
                    _linkMessage,
                    _linkList),
                Ui.Card(
                    Ui.Title("Ảnh online (nguồn public)"),
                    Ui.Text("Chỉ nhận ảnh Public Domain / CC0. Đồng bộ 1 lần mỗi ngày, mỗi nguồn tối đa 10 ảnh.", 12, color: Ui.Muted),
                    Ui.Spacer(8),
                    _nasa,
                    Ui.Spacer(6),
                    _wiki,
                    Ui.Divider(),
                    Ui.Row(Ui.Text("NASA API key (để trống = DEMO_KEY, giới hạn 50 lần/ngày)", 12, color: Ui.Muted), _nasaKey,
                        Ui.Btn("Lưu key", () => _host.SetNasaApiKey(_nasaKey.Text)), keyLink),
                    Ui.Row(_syncMessage, Ui.Btn("Đồng bộ ngay", () => _ = _host.SyncOnlineAsync(force: true))))
            );
        }

        private void PickFolder()
        {
            using var dialog = new Forms.FolderBrowserDialog
            {
                Description = "Chọn thư mục chứa ảnh làm hình nền",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false
            };
            if (dialog.ShowDialog() == Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
                _ = _host.AddFolderAsync(dialog.SelectedPath);
        }

        private async void AddLinks()
        {
            var text = _linkInput.Text;
            _linkMessage.Text = "Đang tải…";
            var message = await _host.AddLinksAsync(text);
            _linkMessage.Text = message;
            if (message.StartsWith("Đã thêm")) _linkInput.Text = "";
        }

        private void RebuildFolders()
        {
            var s = _host.Settings;
            var scan = _host.LastScan;
            _folderList.Children.Clear();
            if (s.Folders.Count == 0)
                _folderList.Children.Add(Ui.Text("Chưa có thư mục nào.", 13, color: Ui.Muted));

            foreach (var folder in s.Folders)
            {
                var path = folder;
                var missing = scan != null && scan.MissingFolders.Any(m => string.Equals(m, path, StringComparison.OrdinalIgnoreCase));
                var count = scan != null && scan.PerFolder.TryGetValue(path, out var n) ? n : 0;
                var info = Ui.Stack(Ui.Text(path, 13, wrap: false),
                    Ui.Text(missing ? "Không tìm thấy thư mục (ổ ngoài đã rút?)" : $"{count} ảnh dùng được", 12, color: missing ? Ui.Bad : Ui.Muted));
                var badge = missing ? Ui.Badge("Không khả dụng", Ui.Bad, Ui.BadSoft) : Ui.Badge("Sẵn sàng", Ui.Ok, Ui.OkSoft);
                _folderList.Children.Add(Ui.Divider());
                _folderList.Children.Add(Ui.Row(info, badge, Ui.Btn("Gỡ", () => ConfirmRemoveFolder(path), link: true)));
            }

            var notes = new List<string>();
            if (scan != null && scan.TooSmall > 0) notes.Add($"Bỏ qua {scan.TooSmall} ảnh rộng dưới {s.MinImageWidth}px");
            if (scan != null && scan.Heic > 0) notes.Add($"{scan.Heic} ảnh HEIC (iPhone) chưa đọc được: cài \"HEIF Image Extensions\" từ Microsoft Store rồi bấm Quét lại");
            if (scan != null && scan.Unreadable > 0) notes.Add($"{scan.Unreadable} ảnh lỗi");
            _skipNote.Text = notes.Count == 0 ? "" : string.Join(" · ", notes);
            _skipNote.Margin = new Thickness(0, notes.Count == 0 ? 0 : 8, 0, 0);
        }

        private void ConfirmRemoveFolder(string folder)
        {
            var answer = MessageBox.Show(this, $"Gỡ thư mục này khỏi app?\n\n{folder}\n\nẢnh trong thư mục vẫn giữ nguyên trên máy.",
                "Gỡ thư mục", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Yes) _ = _host.RemoveFolderAsync(folder);
        }

        private void RebuildLinks()
        {
            _linkList.Children.Clear();
            foreach (var link in _host.Settings.Links.OrderByDescending(l => l.AddedAt))
            {
                var url = link.Url;
                Border badge;
                if (link.Status == LinkStatus.Ready) badge = Ui.Badge("Sẵn sàng", Ui.Ok, Ui.OkSoft);
                else if (link.Status == LinkStatus.Error) badge = Ui.Badge("Lỗi", Ui.Bad, Ui.BadSoft);
                else badge = Ui.Badge("Chờ tải", Ui.Wait, Ui.WaitSoft);

                var info = Ui.Stack(Ui.Text(url, 13, wrap: false));
                if (!string.IsNullOrEmpty(link.Error))
                    info.Children.Add(Ui.Text(link.Error!, 12, color: link.Status == LinkStatus.Error ? Ui.Bad : Ui.Muted));

                var buttons = new List<UIElement> { badge };
                if (link.Status != LinkStatus.Ready) buttons.Add(Ui.Btn("Thử lại", () => _ = _host.RetryLinkAsync(url), link: true));
                buttons.Add(Ui.Btn("Xóa", () => _host.RemoveLink(url), link: true));

                _linkList.Children.Add(Ui.Divider());
                _linkList.Children.Add(Ui.Row(info, buttons.ToArray()));
            }
        }

        // ================================================================== Tab Quote

        private UIElement BuildQuotesTab()
        {
            _groupFilter.Items.Add("Tất cả nhóm");
            _groupFilter.Items.Add("Mặc định");
            _groupFilter.Items.Add("Cá nhân");
            _groupFilter.Items.Add("Online");
            _groupFilter.SelectedIndex = 0;

            var languages = Ui.Horizontal(Ui.Text("Ngôn ngữ:", 13, wrap: false), _langVi, _langEn, _langAll);
            languages.VerticalAlignment = VerticalAlignment.Center;

            return Ui.Stack(
                Ui.Row(Header("Quote", "Quote từ mọi nhóm đang bật được xáo trộn chung."), languages),
                Ui.Card(
                    Ui.Title("Nhóm quote"),
                    Ui.Spacer(8),
                    Ui.Horizontal(_gBuiltIn, _gUser, _gOnline)),
                Ui.Card(
                    Ui.Row(_search, _groupFilter, Ui.Btn("Nhập nhiều quote", OpenBulkDialog), Ui.Btn("+ Thêm quote", () => OpenQuoteDialog(null), primary: true)),
                    _quoteCount,
                    _quoteList,
                    Ui.Spacer(6),
                    Ui.Text("Quote mặc định và online chỉ bật/tắt được theo nhóm. Chỉ quote cá nhân sửa, xóa được.", 12, color: Ui.Muted))
            );
        }

        private void RebuildQuotes()
        {
            var lib = _host.Quotes;
            IEnumerable<Quote> list = lib.All;
            switch (_groupFilter.SelectedIndex)
            {
                case 1: list = lib.BuiltIn; break;
                case 2: list = lib.User; break;
                case 3: list = lib.Online; break;
            }
            var term = (_search.Text ?? "").Trim();
            if (term.Length > 0)
            {
                list = list.Where(q =>
                    q.Text.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0
                    || (q.Author ?? "").IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0
                    || (q.Source ?? "").IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            // Quote cá nhân lên đầu.
            var items = list.OrderBy(q => q.Group == QuoteGroup.User ? 0 : 1).ToList();
            const int limit = 150;
            _quoteCount.Text = items.Count > limit ? $"Hiện {limit}/{items.Count} quote, gõ để tìm thêm" : $"{items.Count} quote";
            _quoteCount.Margin = new Thickness(0, 6, 0, 0);

            _quoteList.Children.Clear();
            foreach (var q in items.Take(limit))
            {
                var quote = q;
                var tag = q.Group == QuoteGroup.User ? "CÁ NHÂN" : q.Group == QuoteGroup.Online ? "ONLINE" : "MẶC ĐỊNH";
                var sub = new List<string>();
                if (!string.IsNullOrWhiteSpace(q.Author)) sub.Add(q.Author!);
                if (!string.IsNullOrWhiteSpace(q.Source)) sub.Add(q.Source!);
                var subText = (sub.Count == 0 ? "Không có tác giả" : string.Join(", ", sub)) + " · " + (q.Lang == QuoteLanguage.English ? "Tiếng Anh" : "Tiếng Việt");

                var body = Ui.Stack(Ui.Text($"“{q.Text}”", 13), Ui.Text(subText, 12, color: Ui.Muted));
                var line = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
                var tagEl = Ui.Tag(tag);
                DockPanel.SetDock(tagEl, Dock.Left);
                line.Children.Add(tagEl);
                if (q.Group == QuoteGroup.User)
                {
                    var actions = Ui.Horizontal(
                        Ui.Btn("Sửa", () => OpenQuoteDialog(quote), link: true),
                        Ui.Btn("Xóa", () => ConfirmDeleteQuote(quote), link: true));
                    actions.VerticalAlignment = VerticalAlignment.Top;
                    DockPanel.SetDock(actions, Dock.Right);
                    line.Children.Add(actions);
                }
                line.Children.Add(body);
                _quoteList.Children.Add(Ui.Divider());
                _quoteList.Children.Add(line);
            }
        }

        private void OpenQuoteDialog(Quote? editing)
        {
            var dialog = new QuoteDialog(_host, editing) { Owner = this };
            dialog.ShowDialog();
        }

        private void OpenBulkDialog()
        {
            var dialog = new BulkQuoteDialog(_host) { Owner = this };
            dialog.ShowDialog();
        }

        private void ConfirmDeleteQuote(Quote quote)
        {
            var answer = MessageBox.Show(this, $"Xóa quote này?\n\n“{quote.Text}”", "Xóa quote",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Yes) _host.DeleteUserQuote(quote.Id);
        }

        // ================================================================== Tab Lịch

        private UIElement BuildScheduleTab()
        {
            foreach (var (_, label) in Intervals)
            {
                _imageInterval.Items.Add(label);
                _quoteInterval.Items.Add(label);
            }

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var left = Ui.Stack(Ui.Text("Đổi ảnh mỗi", 13, bold: true), Ui.Spacer(4), _imageInterval);
            var right = Ui.Stack(Ui.Text("Đổi quote mỗi", 13, bold: true), Ui.Spacer(4), _quoteInterval,
                Ui.Text("Ảnh giữ nguyên, chỉ ghép quote mới", 12, color: Ui.Muted));
            Grid.SetColumn(right, 1);
            grid.Children.Add(left);
            grid.Children.Add(right);

            return Ui.Stack(
                Header("Lịch xoay", "Ảnh và quote đổi theo hai chu kỳ riêng. Thay đổi áp dụng từ lượt kế tiếp."),
                Ui.Card(Ui.Title("Chu kỳ tự động"), Ui.Spacer(10), grid, _changeOnStartup, Ui.Spacer(6), _nextInfo),
                Ui.Card(
                    Ui.Title("Làm mới thủ công"),
                    Ui.Text("Đổi ngay không cần chờ. Làm mới xong, chu kỳ tương ứng tính lại từ đầu.", 12, color: Ui.Muted),
                    Ui.Spacer(10),
                    Ui.Horizontal(
                        Ui.Btn("Làm mới tất cả", () => _ = _host.ChangeAsync(Change.All, auto: false), primary: true),
                        Ui.Btn("Chỉ đổi ảnh", () => _ = _host.ChangeAsync(Change.Image, auto: false)),
                        Ui.Btn("Chỉ đổi quote", () => _ = _host.ChangeAsync(Change.Quote, auto: false))),
                    Ui.Spacer(8),
                    Ui.Text("Mẹo: nháy đúp icon dưới khay hệ thống = Làm mới tất cả.", 12, color: Ui.Muted)),
                Ui.Card(
                    Ui.Row(Ui.Stack(_stateText,
                            Ui.Text("Tạm dừng thì giữ nguyên hình nền hiện tại cho tới khi bấm Tiếp tục. Vẫn làm mới thủ công được.", 12, color: Ui.Muted)),
                        _pauseButton))
            );
        }

        // ================================================================== Tab Chung

        private UIElement BuildGeneralTab()
        {
            return Ui.Stack(
                Header("Cài đặt chung", ""),
                Ui.Card(_startup, Ui.Text("App tự chạy ngầm khi bạn đăng nhập Windows.", 12, color: Ui.Muted)),
                Ui.Card(
                    Ui.Title("Ảnh khác tỉ lệ màn hình thì"),
                    Ui.Spacer(8),
                    Ui.Horizontal(_fitFill, _fitFit, _fitStretch),
                    Ui.Spacer(8),
                    Ui.Text("Quote hiện ở góc dưới bên phải, chữ trắng trên nền tối mờ.", 12, color: Ui.Muted)),
                Ui.Card(
                    Ui.Title("Desktop"),
                    _iconsText,
                    Ui.Spacer(8),
                    Ui.Horizontal(
                        Ui.Btn("Hiện biểu tượng trên desktop", ShowDesktopIcons),
                        Ui.Btn("Tạo lối tắt ngoài desktop", CreateShortcut))),
                Ui.Card(
                    Ui.Row(Ui.Stack(Ui.Title("Bộ nhớ đệm ảnh"), _cacheText),
                        Ui.Btn("Xóa ảnh online đã tải", ConfirmClearCache))),
                Ui.Card(
                    Ui.Row(Ui.Text($"Phiên bản {AppHost.Version}", 13),
                        Ui.Btn("Mở thư mục nhật ký lỗi", () => Shell.OpenFolder(Paths.LocalDir))))
            );
        }

        private void ShowDesktopIcons()
        {
            var ok = DesktopIntegration.ShowIcons();
            if (!ok)
                MessageBox.Show(this, "Không bật được tự động. Hãy chuột phải vào desktop → View (Xem) → tick \"Show desktop icons\".",
                    "Biểu tượng desktop", MessageBoxButton.OK, MessageBoxImage.Information);
            RefreshAll();
        }

        private void CreateShortcut()
        {
            var ok = DesktopIntegration.CreateShortcut();
            MessageBox.Show(this, ok ? "Đã tạo lối tắt \"Hình nền & Quote\" ngoài desktop." : "Không tạo được lối tắt. Xem nhật ký lỗi để biết thêm.",
                "Lối tắt", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
            RefreshAll();
        }

        private void ConfirmClearCache()
        {
            var answer = MessageBox.Show(this, "Xóa các ảnh NASA / Wikimedia đã tải? Lần đồng bộ sau app sẽ tải lại.",
                "Bộ nhớ đệm", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Yes) _host.ClearCache();
        }

        // ================================================================== Đồng bộ trạng thái ↔ giao diện

        private void WireEvents()
        {
            _subfolders.Click += (_, __) => { if (!_updating) _ = _host.SetScanSubfoldersAsync(_subfolders.IsChecked == true); };
            _nasa.Click += (_, __) => { if (!_updating) _host.SetNasaEnabled(_nasa.IsChecked == true); };
            _wiki.Click += (_, __) => { if (!_updating) _host.SetWikimediaEnabled(_wiki.IsChecked == true); };

            _langVi.Checked += (_, __) => { if (!_updating) _host.SetQuoteLanguage(QuoteLanguage.Vietnamese); };
            _langEn.Checked += (_, __) => { if (!_updating) _host.SetQuoteLanguage(QuoteLanguage.English); };
            _langAll.Checked += (_, __) => { if (!_updating) _host.SetQuoteLanguage(QuoteLanguage.All); };
            _gBuiltIn.Click += (_, __) => { if (!_updating) _host.SetQuoteGroup(QuoteGroup.BuiltIn, _gBuiltIn.IsChecked == true); };
            _gUser.Click += (_, __) => { if (!_updating) _host.SetQuoteGroup(QuoteGroup.User, _gUser.IsChecked == true); };
            _gOnline.Click += (_, __) => { if (!_updating) _host.SetQuoteGroup(QuoteGroup.Online, _gOnline.IsChecked == true); };
            _search.TextChanged += (_, __) => { if (!_updating) RebuildQuotes(); };
            _groupFilter.SelectionChanged += (_, __) => { if (!_updating) RebuildQuotes(); };

            _imageInterval.SelectionChanged += (_, __) =>
            {
                if (!_updating && _imageInterval.SelectedIndex >= 0) _host.SetImageInterval(Intervals[_imageInterval.SelectedIndex].Minutes);
            };
            _quoteInterval.SelectionChanged += (_, __) =>
            {
                if (!_updating && _quoteInterval.SelectedIndex >= 0) _host.SetQuoteInterval(Intervals[_quoteInterval.SelectedIndex].Minutes);
            };
            _changeOnStartup.Click += (_, __) => { if (!_updating) _host.SetChangeOnStartup(_changeOnStartup.IsChecked == true); };

            _startup.Click += (_, __) =>
            {
                if (_updating) return;
                if (!_host.SetStartup(_startup.IsChecked == true))
                    MessageBox.Show(this, "Windows không cho ghi cài đặt khởi động. Trạng thái giữ như cũ.", "Khởi động cùng Windows",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                RefreshAll();
            };
            _fitFill.Checked += (_, __) => { if (!_updating) _ = _host.SetFitModeAsync(FitMode.Fill); };
            _fitFit.Checked += (_, __) => { if (!_updating) _ = _host.SetFitModeAsync(FitMode.Fit); };
            _fitStretch.Checked += (_, __) => { if (!_updating) _ = _host.SetFitModeAsync(FitMode.Stretch); };
        }

        private void OnHostChanged() => Dispatcher.InvokeAsync(RefreshAll);

        private void RefreshAll()
        {
            _updating = true;
            try
            {
                var s = _host.Settings;
                var lib = _host.Quotes;

                // Ảnh
                _subfolders.IsChecked = s.ScanSubfolders;
                RebuildFolders();
                RebuildLinks();
                _nasa.Content = $"NASA – Ảnh thiên văn trong ngày  ·  {_host.OnlineImages.Count(OnlineSource.Nasa)} ảnh đã tải";
                _wiki.Content = $"Wikimedia Commons – Ảnh trong ngày  ·  {_host.OnlineImages.Count(OnlineSource.Wikimedia)} ảnh đã tải";
                _nasa.IsChecked = s.NasaEnabled;
                _wiki.IsChecked = s.WikimediaEnabled;
                if (!_nasaKey.IsKeyboardFocusWithin) _nasaKey.Text = s.NasaApiKey == "DEMO_KEY" ? "" : s.NasaApiKey;
                _syncMessage.Text = _host.IsSyncing ? "Đang đồng bộ…" : (s.LastOnlineSyncMessage ?? "Chưa đồng bộ lần nào.");

                // Quote
                _langVi.IsChecked = s.QuoteLanguage == QuoteLanguage.Vietnamese;
                _langEn.IsChecked = s.QuoteLanguage == QuoteLanguage.English;
                _langAll.IsChecked = s.QuoteLanguage == QuoteLanguage.All;
                _gBuiltIn.Content = $"Mặc định ({lib.BuiltIn.Count})";
                _gUser.Content = $"Cá nhân ({lib.User.Count})";
                _gOnline.Content = $"Online ({lib.Online.Count})" + (s.LastQuoteSync.HasValue ? $" · đồng bộ {s.LastQuoteSync.Value:HH:mm dd/MM}" : "");
                _gBuiltIn.IsChecked = s.UseBuiltInQuotes;
                _gUser.IsChecked = s.UseUserQuotes;
                _gOnline.IsChecked = s.UseOnlineQuotes;
                RebuildQuotes();

                // Lịch
                _imageInterval.SelectedIndex = Array.FindIndex(Intervals, i => i.Minutes == s.ImageIntervalMinutes);
                _quoteInterval.SelectedIndex = Array.FindIndex(Intervals, i => i.Minutes == s.QuoteIntervalMinutes);
                _changeOnStartup.IsChecked = s.ChangeOnStartup;
                var ni = _host.NextImageChange;
                var nq = _host.NextQuoteChange;
                _nextInfo.Text = _host.IsPaused ? "Đang tạm dừng."
                    : $"Đổi ảnh tiếp theo: {(ni.HasValue ? ni.Value.ToString("HH:mm") : "—")} · Đổi quote tiếp theo: {(nq.HasValue ? nq.Value.ToString("HH:mm") : "—")}";
                _stateText.Text = _host.IsPaused ? "Trạng thái: Tạm dừng" : "Trạng thái: Đang chạy";
                _stateText.Foreground = _host.IsPaused ? Ui.Wait : Ui.Ok;
                _pauseButton.Content = _host.IsPaused ? "Tiếp tục" : "Tạm dừng";

                // Chung
                _startup.IsChecked = StartupRegistration.IsEnabled();
                _fitFill.IsChecked = s.FitMode == FitMode.Fill;
                _fitFit.IsChecked = s.FitMode == FitMode.Fit;
                _fitStretch.IsChecked = s.FitMode == FitMode.Stretch;
                _iconsText.Text = (DesktopIntegration.IconsHidden()
                        ? "Biểu tượng desktop đang bị Windows ẩn. Bấm nút bên dưới để hiện lại."
                        : "Biểu tượng desktop đang hiện bình thường. App không ẩn, xóa hay di chuyển file nào của bạn.")
                    + (DesktopIntegration.ShortcutExists() ? "" : " Chưa có lối tắt mở app ngoài desktop.");
                var mb = OnlineImageStore.CacheSizeBytes() / 1024.0 / 1024.0;
                _cacheText.Text = $"{mb:0} MB / {s.CacheLimitMb} MB · ảnh cũ nhất tự xóa khi đầy";

                // Chân cửa sổ
                _footer.Text = (_host.IsPaused ? "Tạm dừng" : "Đang chạy") + $" · Kho: {_host.ImageCount} ảnh, {_host.QuoteCount} quote"
                    + (ni.HasValue && !_host.IsPaused ? $" · Đổi ảnh tiếp theo lúc {ni.Value:HH:mm}" : "");
            }
            finally
            {
                _updating = false;
            }
        }
    }
}
