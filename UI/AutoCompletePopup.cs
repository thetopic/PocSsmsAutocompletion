using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
            _wordSpan = wordSpan;
            _allItems = items.ToList();
            _listBox.ItemsSource  = _allItems.Select(item => item.DisplayText).ToList();
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
                : _allItems.Where(item => item.DisplayText.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)).ToList();
            if (filtered.Count == 0) { Dismiss(); return; }
            _listBox.ItemsSource = filtered.Select(item => item.DisplayText).ToList();
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
            var selectedText = _listBox.SelectedItem as string;
            if (selectedText == null) { Dismiss(); return null; }
            var selectedItem = _allItems.FirstOrDefault(item => item.DisplayText == selectedText);
            string insertText = selectedItem?.InsertText ?? selectedText;
            Dismiss();
            try {
                var snapshot = _textView.TextBuffer.CurrentSnapshot;
                var span     = _wordSpan.GetSpan(snapshot);
                _textView.TextBuffer.Replace(span, insertText);
            }
            catch { }
            return selectedItem;
        }

        public void Dismiss() {
            if (_popup != null) _popup.IsOpen = false;
        }

        private static ListBox BuildListBox() =>
            new ListBox {
                MaxHeight        = 220,
                MinWidth         = 280,
                FontFamily       = new FontFamily("Consolas, Courier New"),
                FontSize         = 12,
                Background       = new SolidColorBrush(Color.FromRgb(37, 37, 38)),
                Foreground       = new SolidColorBrush(Colors.White),
                BorderBrush      = new SolidColorBrush(Color.FromRgb(0, 122, 204)),
                BorderThickness  = new Thickness(1),
                SelectionMode    = SelectionMode.Single,
            };

        private Popup BuildPopup(IWpfTextView textView) =>
            new Popup {
                Child              = _listBox,
                StaysOpen          = true,
                AllowsTransparency = true,
                Placement          = PlacementMode.RelativePoint,
                PlacementTarget    = (UIElement)textView.VisualElement,
            };
    }
}
