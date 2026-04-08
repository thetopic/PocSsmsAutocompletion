using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace SsmsAutocompletion {

    internal sealed class SqlCommandFilter : IOleCommandTarget {
        private readonly IWpfTextView           _textView;
        private readonly AutoCompletePopup       _popup;
        private readonly CompletionEngine        _completionEngine;
        private readonly IContextDetector        _contextDetector;
        private readonly IConnectionInfoProvider _connectionInfoProvider;
        private readonly IDatabaseMetadata       _databaseMetadata;
        private          ConnectionKey           _connectionKey;

        public IOleCommandTarget Next { get; set; }

        public SqlCommandFilter(
            IWpfTextView textView,
            ConnectionKey connectionKey,
            CompletionEngine completionEngine,
            IContextDetector contextDetector,
            IConnectionInfoProvider connectionInfoProvider,
            IDatabaseMetadata databaseMetadata) {
            _textView               = textView;
            _connectionKey          = connectionKey;
            _completionEngine       = completionEngine;
            _contextDetector        = contextDetector;
            _connectionInfoProvider = connectionInfoProvider;
            _databaseMetadata       = databaseMetadata;
            _popup                  = new AutoCompletePopup(textView);
        }

        public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut) {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (pguidCmdGroup == VSConstants.VSStd2K) {
                int handled = HandleVsStd2KCommand((VSConstants.VSStd2KCmdID)nCmdID, pvaIn);
                if (handled == VSConstants.S_OK) return handled;
            }
            int result = Next.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
            if (pguidCmdGroup == VSConstants.VSStd2K && nCmdID == (uint)VSConstants.VSStd2KCmdID.TYPECHAR)
                HandleTypeChar(pvaIn);
            return result;
        }

        public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pcmdText) {
            ThreadHelper.ThrowIfNotOnUIThread();
            return Next.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pcmdText);
        }

        private int HandleVsStd2KCommand(VSConstants.VSStd2KCmdID commandId, IntPtr pvaIn) {
            switch (commandId) {
                case VSConstants.VSStd2KCmdID.RETURN:
                case VSConstants.VSStd2KCmdID.TAB:
                    if (!_popup.IsVisible) return VSConstants.E_FAIL;
                    var committed = _popup.Commit();
                    if (committed?.Description == "Table")
                        TriggerCompletionFromExplicitCommand();
                    return VSConstants.S_OK;
                case VSConstants.VSStd2KCmdID.CANCEL:
                    if (!_popup.IsVisible) return VSConstants.E_FAIL;
                    _popup.Dismiss();
                    return VSConstants.S_OK;
                case VSConstants.VSStd2KCmdID.UP:
                    if (!_popup.IsVisible) return VSConstants.E_FAIL;
                    _popup.MoveUp();
                    return VSConstants.S_OK;
                case VSConstants.VSStd2KCmdID.DOWN:
                    if (!_popup.IsVisible) return VSConstants.E_FAIL;
                    _popup.MoveDown();
                    return VSConstants.S_OK;
                case VSConstants.VSStd2KCmdID.SHOWMEMBERLIST:
                case VSConstants.VSStd2KCmdID.COMPLETEWORD:
                    TriggerCompletionFromExplicitCommand();
                    return VSConstants.S_OK;
                default:
                    return VSConstants.E_FAIL;
            }
        }

        private void TriggerCompletionFromExplicitCommand() {
            RefreshConnectionKey();
            int caretPosition    = _textView.Caret.Position.BufferPosition.Position;
            string sql           = _textView.TextBuffer.CurrentSnapshot.GetText();
            var snapshot         = _textView.TextBuffer.CurrentSnapshot;
            var capturedKey      = _connectionKey;
            _popup.Dismiss();
            Task.Run(() => TriggerAsync(snapshot, caretPosition, sql, capturedKey));
        }

        private void HandleTypeChar(IntPtr pvaIn) {
            char character = (char)(ushort)Marshal.GetObjectForNativeVariant(pvaIn);
            if (char.IsLetterOrDigit(character) || character == '_') {
                HandleWordCharacter();
                return;
            }
            if (character == '.') { HandleDotCharacter(); return; }
            if (character == ' ') { HandleSpaceCharacter(); return; }
            _popup.Dismiss();
        }

        private void HandleWordCharacter() {
            RefreshConnectionKey();
            int caretPosition = _textView.Caret.Position.BufferPosition.Position;
            var snapshot      = _textView.TextBuffer.CurrentSnapshot;
            if (_popup.IsVisible) {
                string prefix = _contextDetector.GetCurrentWord(snapshot, caretPosition);
                _popup.Filter(prefix);
                return;
            }
            string sql      = snapshot.GetText();
            var capturedKey = _connectionKey;
            Task.Run(() => TriggerAsync(snapshot, caretPosition, sql, capturedKey));
        }

        private void HandleDotCharacter() {
            RefreshConnectionKey();
            int caretPosition = _textView.Caret.Position.BufferPosition.Position;
            string sql        = _textView.TextBuffer.CurrentSnapshot.GetText();
            var snapshot      = _textView.TextBuffer.CurrentSnapshot;
            var capturedKey   = _connectionKey;
            _popup.Dismiss();
            Task.Run(() => TriggerAsync(snapshot, caretPosition, sql, capturedKey));
        }

        private void HandleSpaceCharacter() {
            int caretPosition = _textView.Caret.Position.BufferPosition.Position;
            var snapshot      = _textView.TextBuffer.CurrentSnapshot;
            string wordBefore = _contextDetector.GetWordBefore(snapshot, caretPosition);
            if (!IsContextTriggerWord(wordBefore)) { _popup.Dismiss(); return; }
            RefreshConnectionKey();
            string sql      = snapshot.GetText();
            var capturedKey = _connectionKey;
            _popup.Dismiss();
            Task.Run(() => TriggerAsync(snapshot, caretPosition, sql, capturedKey));
        }

        private void TriggerAsync(ITextSnapshot snapshot, int caretPosition, string sql, ConnectionKey connectionKey) {
            try {
                var items    = _completionEngine.GetCompletions(snapshot, sql, caretPosition, connectionKey);
                if (items.Count == 0) return;
                var wordSpan = _contextDetector.GetWordSpan(snapshot, caretPosition);
                _textView.VisualElement.Dispatcher.InvokeAsync(() => {
                    if (!_popup.IsVisible) _popup.Show(new System.Collections.Generic.List<CompletionItem>(items), wordSpan);
                });
            }
            catch { }
        }

        private void RefreshConnectionKey() {
            var freshKey = _connectionInfoProvider.GetConnectionKey();
            if (freshKey.IsEmpty) return;
            if (freshKey.Equals(_connectionKey)) return;
            _databaseMetadata.Invalidate(_connectionKey);
            _connectionKey = freshKey;
            var serverConnection = _connectionInfoProvider.BuildServerConnection();
            _databaseMetadata.WarmAsync(_connectionKey, serverConnection);
        }

        private static bool IsContextTriggerWord(string word) {
            switch (word.ToUpperInvariant()) {
                case "ON":
                case "WHERE":
                case "AND":
                case "OR":
                case "JOIN":
                    return true;
                default:
                    return false;
            }
        }
    }
}
