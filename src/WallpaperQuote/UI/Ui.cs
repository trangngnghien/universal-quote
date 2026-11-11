using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WallpaperQuote.UI
{
    /// <summary>Bộ dựng giao diện dùng chung, theo wireframe: nền trắng, thẻ viền xám, 1 màu nhấn xanh.</summary>
    internal static class Ui
    {
        public static readonly Brush Ink = Frozen(0x11, 0x18, 0x27);
        public static readonly Brush Muted = Frozen(0x4B, 0x55, 0x63);
        public static readonly Brush LineBrush = Frozen(0xE5, 0xE7, 0xEB);
        public static readonly Brush Panel = Frozen(0xF9, 0xFA, 0xFB);
        public static readonly Brush Accent = Frozen(0x25, 0x63, 0xEB);
        public static readonly Brush AccentSoft = Frozen(0xE8, 0xEE, 0xFD);
        public static readonly Brush Ok = Frozen(0x16, 0x65, 0x34);
        public static readonly Brush OkSoft = Frozen(0xDC, 0xFC, 0xE7);
        public static readonly Brush Bad = Frozen(0x99, 0x1B, 0x1B);
        public static readonly Brush BadSoft = Frozen(0xFE, 0xE2, 0xE2);
        public static readonly Brush Wait = Frozen(0x92, 0x40, 0x0E);
        public static readonly Brush WaitSoft = Frozen(0xFE, 0xF3, 0xC7);
        public static readonly Brush TagSoft = Frozen(0xF3, 0xF4, 0xF6);

        private static Brush Frozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        public static TextBlock Text(string text, double size = 13, bool bold = false, Brush? color = null, bool wrap = true)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = size,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = color ?? Ink,
                TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
                TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis
            };
        }

        public static TextBlock Title(string text) => Text(text, 15, bold: true);

        public static Button Btn(string text, Action onClick, bool primary = false, bool link = false)
        {
            var b = new Button
            {
                Content = text,
                Padding = link ? new Thickness(6, 3, 6, 3) : new Thickness(14, 6, 14, 6),
                Margin = new Thickness(4, 0, 0, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Background = primary ? Accent : (link ? Brushes.Transparent : Brushes.White),
                Foreground = primary ? Brushes.White : (link ? Accent : Ink),
                BorderBrush = primary ? Accent : (link ? Brushes.Transparent : Frozen(0xD1, 0xD5, 0xDB)),
                FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal
            };
            b.Click += (_, __) => onClick();
            return b;
        }

        public static Border Card(params UIElement[] children)
        {
            var stack = new StackPanel();
            foreach (var c in children) stack.Children.Add(c);
            return new Border
            {
                BorderBrush = LineBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 14),
                Background = Brushes.White,
                Child = stack
            };
        }

        /// <summary>Hàng ngang: phần tử đầu giãn hết chỗ trống, các phần tử sau xếp bên phải.</summary>
        public static Grid Row(UIElement main, params UIElement[] right)
        {
            var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(main, 0);
            grid.Children.Add(main);
            for (var i = 0; i < right.Length; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetColumn(right[i], i + 1);
                grid.Children.Add(right[i]);
            }
            return grid;
        }

        public static StackPanel Stack(params UIElement[] children)
        {
            var s = new StackPanel();
            foreach (var c in children) s.Children.Add(c);
            return s;
        }

        public static StackPanel Horizontal(params UIElement[] children)
        {
            var s = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var c in children) s.Children.Add(c);
            return s;
        }

        public static Border Badge(string text, Brush fg, Brush bg) => new Border
        {
            Background = bg,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(8, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = Text(text, 12, bold: true, color: fg, wrap: false)
        };

        public static Border Tag(string text) => new Border
        {
            Background = TagSoft,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 1, 6, 1),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Child = Text(text, 11, bold: true, color: Muted, wrap: false)
        };

        public static Border Divider() => new Border
        {
            Height = 1,
            Background = LineBrush,
            Margin = new Thickness(0, 6, 0, 6)
        };

        public static UIElement Spacer(double h) => new Border { Height = h };
    }
}
