using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace SsmsAutocompletion {

    internal sealed class AutoCompletePopup {
        private readonly Popup        _popup;
        private readonly ListBox      _listBox;
        private readonly IWpfTextView _textView;
        private ITrackingSpan         _wordSpan;
        private List<CompletionItem>  _allItems = new List<CompletionItem>();

        public bool IsVisible => _popup?.IsOpen == true;

        public AutoCompletePopup(IWpfTextView textView) {
            _textView = textView;
            _listBox  = BuildListBox();
            _popup    = BuildPopup(textView);
            _popup.Closed += (sender, args) => _allItems.Clear();
            _textView.VisualElement.PreviewMouseDown += (sender, args) => Dismiss();
        }

        public void Show(IList<CompletionItem> items, ITrackingSpan wordSpan) {
            if (items == null || items.Count == 0) return;
            _wordSpan              = wordSpan;
            _allItems              = items.ToList();
            _listBox.ItemsSource   = _allItems;
            _listBox.SelectedIndex = 0;
            _popup.HorizontalOffset = _textView.Caret.Left;
            _popup.VerticalOffset   = _textView.Caret.Bottom;
            _popup.IsOpen           = true;
            _listBox.Focusable      = false;
        }

        public void Filter(string prefix) {
            if (!IsVisible) return;
            var filtered = string.IsNullOrEmpty(prefix)
                ? _allItems
                : _allItems.Where(item => item.DisplayText.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
            if (filtered.Count == 0) { Dismiss(); return; }
            _listBox.ItemsSource = filtered;
            if (_listBox.SelectedIndex < 0) _listBox.SelectedIndex = 0;
        }

        public void MoveDown() {
            if (_listBox.SelectedIndex < _listBox.Items.Count - 1)
                _listBox.SelectedIndex++;
        }

        public void MoveUp() {
            if (_listBox.SelectedIndex > 0)
                _listBox.SelectedIndex--;
        }

        public CompletionItem Commit() {
            var selectedItem = _listBox.SelectedItem as CompletionItem;
            if (selectedItem == null) { Dismiss(); return null; }
            Dismiss();
            try {
                var snapshot = _textView.TextBuffer.CurrentSnapshot;
                var span     = _wordSpan.GetSpan(snapshot);
                _textView.TextBuffer.Replace(span, selectedItem.InsertText);
            }
            catch { }
            return selectedItem;
        }

        public void Dismiss() {
            if (_popup != null) _popup.IsOpen = false;
        }

        private static ListBox BuildListBox() =>
            new ListBox {
                MaxHeight       = 220,
                MinWidth        = 280,
                FontFamily      = new FontFamily("Consolas, Courier New"),
                FontSize        = 12,
                Background      = new SolidColorBrush(Color.FromRgb(37, 37, 38)),
                Foreground      = new SolidColorBrush(Colors.White),
                BorderBrush     = new SolidColorBrush(Color.FromRgb(0, 122, 204)),
                BorderThickness = new Thickness(1),
                SelectionMode   = SelectionMode.Single,
                ItemTemplate    = BuildItemTemplate(),
            };

        private static DataTemplate BuildItemTemplate() {
            var template = new DataTemplate(typeof(CompletionItem));

            // Root: horizontal StackPanel
            var stack = new FrameworkElementFactory(typeof(StackPanel));
            stack.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            stack.SetValue(StackPanel.MarginProperty, new Thickness(2, 1, 2, 1));

            // VS CrispImage : icône officielle tirée du catalogue Visual Studio
            var icon = new FrameworkElementFactory(typeof(CrispImage));
            icon.SetValue(CrispImage.WidthProperty,  16.0);
            icon.SetValue(CrispImage.HeightProperty, 16.0);
            icon.SetValue(CrispImage.MarginProperty, new Thickness(0, 0, 6, 0));
            icon.SetValue(CrispImage.VerticalAlignmentProperty, VerticalAlignment.Center);
            icon.SetBinding(CrispImage.MonikerProperty,
                new Binding("Kind") { Converter = KindToMonikerConverter.Instance });

            // Texte de complétion
            var label = new FrameworkElementFactory(typeof(TextBlock));
            label.SetBinding(TextBlock.TextProperty, new Binding("DisplayText"));
            label.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Colors.White));
            label.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);

            stack.AppendChild(icon);
            stack.AppendChild(label);

            template.VisualTree = stack;
            return template;
        }

        private Popup BuildPopup(IWpfTextView textView) =>
            new Popup {
                Child              = _listBox,
                StaysOpen          = true,
                AllowsTransparency = true,
                Placement          = PlacementMode.RelativePoint,
                PlacementTarget    = (UIElement)textView.VisualElement,
            };

        // ── Converter CompletionItemKind → ImageMoniker ───────────────────────
        //
        // KnownMonikers expose les ~3 500 icônes du catalogue VS (les mêmes que
        // celles utilisées dans l'IntelliSense natif, l'explorateur de serveurs…).
        // CrispImage les rend correctement en thème clair et sombre.
        //
        private sealed class KindToMonikerConverter : IValueConverter {
            public static readonly KindToMonikerConverter Instance = new KindToMonikerConverter();

            public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
                if (value is CompletionItemKind kind) {
                    switch (kind) {
                        case CompletionItemKind.Table:   return KnownMonikers.Table;
                        case CompletionItemKind.Column:  return KnownMonikers.Column;
                        case CompletionItemKind.Keyword: return KnownMonikers.IntellisenseKeyword;
                        case CompletionItemKind.Join:    return KnownMonikers.Join;
                        case CompletionItemKind.Alias:   return KnownMonikers.KeywordSnippet;
                    }
                }
                return new ImageMoniker();
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
                throw new NotImplementedException();
        }
    }
}
