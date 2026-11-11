using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WallpaperQuote.Core;

namespace WallpaperQuote.UI
{
    /// <summary>Hộp thoại Thêm / Sửa quote cá nhân (màn S04b): nội dung, tác giả, nguồn trích dẫn, ngôn ngữ.</summary>
    public sealed class QuoteDialog : Window
    {
        private readonly AppHost _host;
        private readonly Quote? _editing;

        private readonly TextBox _text = new TextBox
        {
            AcceptsReturn = false,
            TextWrapping = TextWrapping.Wrap,
            Height = 90,
            MaxLength = QuoteLibrary.MaxTextLength,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(6)
        };
        private readonly TextBlock _counter = Ui.Text("0 / 300", 12, color: Ui.Muted, wrap: false);
        private readonly TextBox _author = new TextBox { MaxLength = QuoteLibrary.MaxAuthorLength, Padding = new Thickness(6) };
        private readonly TextBox _source = new TextBox { MaxLength = QuoteLibrary.MaxSourceLength, Padding = new Thickness(6) };
        private readonly RadioButton _vi = new RadioButton { Content = "Tiếng Việt", GroupName = "qlang", Margin = new Thickness(0, 0, 20, 0), IsChecked = true };
        private readonly RadioButton _en = new RadioButton { Content = "Tiếng Anh", GroupName = "qlang" };
        private readonly TextBlock _error = Ui.Text("", 12, color: Ui.Bad);

        public QuoteDialog(AppHost host, Quote? editing)
        {
            _host = host;
            _editing = editing;
            Title = editing == null ? "Thêm quote" : "Sửa quote";
            Width = 540;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;
            Background = Brushes.White;
            ShowInTaskbar = false;

            if (editing != null)
            {
                _text.Text = editing.Text;
                _author.Text = editing.Author ?? "";
                _source.Text = editing.Source ?? "";
                _vi.IsChecked = editing.Lang != QuoteLanguage.English;
                _en.IsChecked = editing.Lang == QuoteLanguage.English;
            }
            UpdateCounter();
            _text.TextChanged += (_, __) => { UpdateCounter(); _error.Text = ""; };

            var buttons = Ui.Horizontal(
                Ui.Btn("Hủy", () => { DialogResult = false; }),
                Ui.Btn("Lưu", Save, primary: true));
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            buttons.Margin = new Thickness(0, 16, 0, 0);

            Content = new Border
            {
                Padding = new Thickness(20),
                Child = Ui.Stack(
                    FieldLabel("Nội dung *"),
                    _text,
                    Ui.Row(_error, _counter),
                    Ui.Spacer(10),
                    FieldLabel("Tác giả (không bắt buộc)"),
                    _author,
                    Ui.Spacer(10),
                    FieldLabel("Nguồn trích dẫn (không bắt buộc)"),
                    _source,
                    Ui.Text("Sách, tác phẩm, bài phát biểu… tối đa 100 ký tự, không nhập link. Hiển thị dưới quote: — Tác giả, Nguồn", 12, color: Ui.Muted),
                    Ui.Spacer(10),
                    FieldLabel("Ngôn ngữ"),
                    Ui.Horizontal(_vi, _en),
                    buttons)
            };

            Loaded += (_, __) => _text.Focus();
        }

        private static TextBlock FieldLabel(string text)
        {
            var t = Ui.Text(text, 13, bold: true);
            t.Margin = new Thickness(0, 0, 0, 4);
            return t;
        }

        private void UpdateCounter() => _counter.Text = $"{_text.Text.Length} / {QuoteLibrary.MaxTextLength}";

        private void Save()
        {
            var lang = _en.IsChecked == true ? QuoteLanguage.English : QuoteLanguage.Vietnamese;
            var error = _host.Quotes.Validate(_text.Text, _author.Text, _source.Text, _editing?.Id);
            if (error != null)
            {
                _error.Text = error;
                return;
            }
            if (_editing == null) _host.AddUserQuote(_text.Text, _author.Text, _source.Text, lang);
            else _host.UpdateUserQuote(_editing.Id, _text.Text, _author.Text, _source.Text, lang);
            DialogResult = true;
        }
    }
}
