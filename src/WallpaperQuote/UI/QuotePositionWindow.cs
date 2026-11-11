using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WallpaperQuote.Core;

namespace WallpaperQuote.UI
{
    /// <summary>
    /// Chỉnh vị trí quote bằng chuột: phủ toàn màn hình chính bằng hình nền hiện tại (chưa có quote),
    /// khung quote kéo thả được. Lưu = tâm khung theo tỉ lệ màn hình (0–1).
    /// </summary>
    public sealed class QuotePositionWindow : Window
    {
        private readonly Canvas _canvas = new Canvas();
        private readonly Border _box;
        private readonly double _k;
        private readonly double? _initialX;
        private readonly double? _initialY;
        private bool _dragging;
        private Point _dragOrigin;
        private double _startLeft;
        private double _startTop;

        public double? ResultX { get; private set; }
        public double? ResultY { get; private set; }

        public QuotePositionWindow(string previewFile, Quote quote, double? posX, double? posY)
        {
            _initialX = posX;
            _initialY = posY;

            Title = "Chỉnh vị trí quote";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = 0;
            Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;
            Background = Brushes.Black;
            FontFamily = new FontFamily("Segoe UI");
            _k = Height / 1080.0;

            var root = new Grid();
            root.Children.Add(new Image { Source = Load(previewFile), Stretch = Stretch.Fill });
            root.Children.Add(_canvas);

            _box = BuildQuoteBox(quote);
            _canvas.Children.Add(_box);
            root.Children.Add(BuildToolbar());
            Content = root;

            _box.MouseLeftButtonDown += OnBoxDown;
            _box.MouseMove += OnBoxMove;
            _box.MouseLeftButtonUp += OnBoxUp;
            _box.LostMouseCapture += (_, __) => _dragging = false;

            Loaded += (_, __) => PlaceInitial();
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) Cancel();
                else if (e.Key == Key.Enter) Save();
            };
        }

        private static BitmapImage Load(string file)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.UriSource = new Uri(file, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        /// <summary>Khung quote giống cách vẽ trên hình nền: chữ trắng, nền tối mờ, góc bo, dòng "— Tác giả, Nguồn".</summary>
        private Border BuildQuoteBox(Quote quote)
        {
            var maxBox = Math.Min(Width * 0.42, 900 * _k);
            var padX = 26 * _k;
            var padY = 20 * _k;
            var size = 30 * _k;

            var text = new TextBlock
            {
                Text = "“" + quote.Text + "”",
                FontFamily = new FontFamily("Cambria, Georgia, Segoe UI"),
                FontSize = size,
                LineHeight = size * 1.4,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = maxBox - 2 * padX
            };
            var stack = new StackPanel();
            stack.Children.Add(text);

            var line = quote.AttributionLine(out var sourceStart, out var sourceLength);
            if (line != null)
            {
                var attribution = new TextBlock
                {
                    FontSize = Math.Max(13, 16 * _k),
                    Foreground = new SolidColorBrush(Color.FromArgb(225, 255, 255, 255)),
                    Margin = new Thickness(0, 10 * _k, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = maxBox - 2 * padX
                };
                if (sourceStart >= 0)
                {
                    attribution.Inlines.Add(new Run(line.Substring(0, sourceStart)));
                    attribution.Inlines.Add(new Run(line.Substring(sourceStart, sourceLength)) { FontStyle = FontStyles.Italic });
                }
                else
                {
                    attribution.Inlines.Add(new Run(line));
                }
                stack.Children.Add(attribution);
            }

            var hint = new TextBlock
            {
                Text = "Kéo để di chuyển",
                FontSize = 12,
                Foreground = Brushes.White,
                Background = Ui.Accent,
                Padding = new Thickness(8, 2, 8, 2),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 6)
            };

            var outer = new StackPanel();
            outer.Children.Add(hint);
            outer.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(148, 15, 23, 42)),
                CornerRadius = new CornerRadius(12 * _k),
                Padding = new Thickness(padX, padY, padX, padY),
                BorderBrush = Ui.Accent,
                BorderThickness = new Thickness(2),
                Child = stack
            });

            return new Border { Child = outer, Cursor = Cursors.SizeAll, Background = Brushes.Transparent };
        }

        private UIElement BuildToolbar()
        {
            var bar = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 10, 10, 10),
                Margin = new Thickness(0, 24, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Child = Ui.Horizontal(
                    new TextBlock
                    {
                        Text = "Kéo khung quote tới vị trí bạn muốn, rồi bấm Lưu.",
                        FontSize = 14,
                        Foreground = Ui.Ink,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 12, 0)
                    },
                    Ui.Btn("Về góc dưới phải", ResetToDefault),
                    Ui.Btn("Hủy", Cancel),
                    Ui.Btn("Lưu vị trí", Save, primary: true))
            };
            return bar;
        }

        private void PlaceInitial()
        {
            _box.UpdateLayout();
            var w = _box.ActualWidth;
            var h = _box.ActualHeight;
            double left, top;
            if (_initialX.HasValue && _initialY.HasValue)
            {
                left = _initialX.Value * Width - w / 2;
                top = _initialY.Value * Height - h / 2;
            }
            else
            {
                left = Width - 56 * _k - w;
                top = Height - 110 * _k - h;
            }
            Move(left, top);
            Activate();
            Focus();
        }

        private void Move(double left, double top)
        {
            var edge = 16 * _k;
            left = Math.Min(Math.Max(left, edge), Math.Max(edge, Width - edge - _box.ActualWidth));
            top = Math.Min(Math.Max(top, edge), Math.Max(edge, Height - edge - _box.ActualHeight));
            Canvas.SetLeft(_box, left);
            Canvas.SetTop(_box, top);
        }

        private void OnBoxDown(object sender, MouseButtonEventArgs e)
        {
            _dragging = true;
            _dragOrigin = e.GetPosition(_canvas);
            _startLeft = Canvas.GetLeft(_box);
            _startTop = Canvas.GetTop(_box);
            _box.CaptureMouse();
            e.Handled = true;
        }

        private void OnBoxMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            var p = e.GetPosition(_canvas);
            Move(_startLeft + (p.X - _dragOrigin.X), _startTop + (p.Y - _dragOrigin.Y));
        }

        private void OnBoxUp(object sender, MouseButtonEventArgs e)
        {
            _dragging = false;
            _box.ReleaseMouseCapture();
        }

        private void Save()
        {
            // Lưu tâm của khung quote thật (bỏ phần nhãn "Kéo để di chuyển" phía trên).
            var left = Canvas.GetLeft(_box);
            var top = Canvas.GetTop(_box);
            var hintHeight = 0.0;
            if (_box.Child is StackPanel outer && outer.Children.Count > 1 && outer.Children[0] is FrameworkElement hint)
                hintHeight = hint.ActualHeight + hint.Margin.Bottom;
            var cx = left + _box.ActualWidth / 2;
            var cy = top + hintHeight + (_box.ActualHeight - hintHeight) / 2;
            ResultX = Math.Round(Math.Min(1, Math.Max(0, cx / Width)), 4);
            ResultY = Math.Round(Math.Min(1, Math.Max(0, cy / Height)), 4);
            DialogResult = true;
        }

        private void ResetToDefault()
        {
            ResultX = null;
            ResultY = null;
            DialogResult = true;
        }

        private void Cancel() => DialogResult = false;
    }
}
