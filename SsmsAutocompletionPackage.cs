using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;
using Microsoft.SqlServer.Management.SmoMetadataProvider;
using Microsoft.SqlServer.Management.SqlParser.Intellisense;
using Microsoft.SqlServer.Management.SqlParser.MetadataProvider;
using Microsoft.SqlServer.Management.SqlParser.Parser;
using Microsoft.SqlServer.Management.UI.VSIntegration;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel.Composition;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Task = System.Threading.Tasks.Task;

namespace SsmsAutocompletion {

    // ── DTOs ─────────────────────────────────────────────────────────────────

    internal sealed class TableInfo {
        public string Schema    { get; }
        public string TableName { get; }
        public TableInfo(string schema, string tableName) { Schema = schema; TableName = tableName; }
        public override string ToString() => Schema == "dbo" ? TableName : $"{Schema}.{TableName}";
    }

    internal sealed class ColumnInfo {
        public string ColumnName { get; }
        public string DataType   { get; }
        public ColumnInfo(string columnName, string dataType) { ColumnName = columnName; DataType = dataType; }
    }

    internal sealed class ForeignKeyInfo {
        public string FkSchema    { get; }
        public string FkTable     { get; }
        public IReadOnlyList<string> FkColumns         { get; }
        public string ReferencedSchema { get; }
        public string ReferencedTable  { get; }
        public IReadOnlyList<string> ReferencedColumns { get; }

        public ForeignKeyInfo(string fkSchema, string fkTable, IReadOnlyList<string> fkCols,
                              string refSchema, string refTable, IReadOnlyList<string> refCols) {
            FkSchema = fkSchema; FkTable = fkTable; FkColumns = fkCols;
            ReferencedSchema = refSchema; ReferencedTable = refTable; ReferencedColumns = refCols;
        }
    }

    internal sealed class CompletionItem {
        public string DisplayText  { get; }
        public string InsertText   { get; }
        public string Description  { get; }
        public CompletionItem(string display, string insert, string description) {
            DisplayText = display; InsertText = insert; Description = description;
        }
    }

    // ── ConnectionHelper ─────────────────────────────────────────────────────

    internal static class ConnectionHelper {
        // Returns (serverName, database, windowsAuth, userName, password)
        // Must be called on the UI thread (accesses ServiceCache).
        internal static (string server, string db, bool windowsAuth, string user, string pass) GetCurrentConnectionInfo() {
            try {
                var scriptFactory = ServiceCache.ScriptFactory;
                var activeWndInfo = scriptFactory?.CurrentlyActiveWndConnectionInfo;
                if (activeWndInfo == null) return default;

                PropertyInfo uiConnProp = activeWndInfo.GetType().GetProperty("UIConnectionInfo");
                if (uiConnProp == null) return default;

                object uiConnInfo = uiConnProp.GetValue(activeWndInfo);
                if (uiConnInfo == null) return default;

                string server   = GetProp(uiConnInfo, "ServerName") as string ?? "";
                string userName = GetProp(uiConnInfo, "UserName")   as string ?? "";
                string password = GetProp(uiConnInfo, "Password")   as string ?? "";
                int authType    = (int)(GetProp(uiConnInfo, "AuthenticationType") ?? 0);

                string db = "master";
                var adv = GetProp(uiConnInfo, "AdvancedOptions") as NameValueCollection;
                if (adv != null && !string.IsNullOrWhiteSpace(adv["DATABASE"]))
                    db = adv["DATABASE"];

                return (server, db, authType == 0, userName, password);
            }
            catch { return default; }
        }

        internal static string GetConnectionKey() {
            var (server, db, _, _, _) = GetCurrentConnectionInfo();
            if (string.IsNullOrEmpty(server)) return null;
            return $"{server}|{db}".ToUpperInvariant();
        }

        internal static ServerConnection BuildServerConnection() {
            var (server, db, windowsAuth, user, pass) = GetCurrentConnectionInfo();
            if (string.IsNullOrEmpty(server)) return null;
            var conn = new ServerConnection(server) { DatabaseName = db };
            if (windowsAuth) {
                conn.LoginSecure = true;
            } else {
                conn.LoginSecure = false;
                conn.Login    = user;
                conn.Password = pass;
            }
            return conn;
        }

        private static object GetProp(object src, string name)
            => src.GetType().GetProperty(name)?.GetValue(src, null);
    }

    // ── DatabaseMetadataCache ────────────────────────────────────────────────

    internal static class DatabaseMetadataCache {
        private static readonly object _lock = new object();
        private static readonly Dictionary<string, CacheEntry> _entries =
            new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

        public static void WarmAsync(string connectionKey, ServerConnection serverConn) {
            if (connectionKey == null || serverConn == null) return;
            Task.Run(() => EnsureLoaded(connectionKey, serverConn));
        }

        public static IMetadataProvider GetMetadataProvider(string connectionKey) {
            if (connectionKey == null) return null;
            lock (_lock) {
                if (_entries.TryGetValue(connectionKey, out var e) && !e.IsExpired) return e.MetadataProvider;
            }
            return null;
        }

        public static IReadOnlyList<TableInfo> GetTables(string connectionKey) {
            if (connectionKey == null) return Array.Empty<TableInfo>();
            lock (_lock) {
                if (_entries.TryGetValue(connectionKey, out var e) && !e.IsExpired) return e.Tables;
            }
            return Array.Empty<TableInfo>();
        }

        public static IReadOnlyList<ColumnInfo> GetColumns(string connectionKey, string schema, string tableName) {
            if (connectionKey == null) return Array.Empty<ColumnInfo>();
            lock (_lock) {
                if (_entries.TryGetValue(connectionKey, out var e) && !e.IsExpired) {
                    if (e.Columns.TryGetValue(MakeKey(schema, tableName), out var cols)) return cols;
                }
            }
            return Array.Empty<ColumnInfo>();
        }

        public static IReadOnlyList<ForeignKeyInfo> GetForeignKeys(string connectionKey, string schema, string tableName) {
            if (connectionKey == null) return Array.Empty<ForeignKeyInfo>();
            lock (_lock) {
                if (_entries.TryGetValue(connectionKey, out var e) && !e.IsExpired) {
                    if (e.ForeignKeys.TryGetValue(MakeKey(schema, tableName), out var fks)) return fks;
                }
            }
            return Array.Empty<ForeignKeyInfo>();
        }

        public static void Invalidate(string connectionKey) {
            if (connectionKey == null) return;
            lock (_lock) { _entries.Remove(connectionKey); }
        }

        private static void EnsureLoaded(string connectionKey, ServerConnection serverConn) {
            lock (_lock) {
                if (_entries.TryGetValue(connectionKey, out var existing) && !existing.IsExpired) return;
            }
            var entry = LoadFromSmo(connectionKey, serverConn);
            lock (_lock) {
                if (!_entries.TryGetValue(connectionKey, out var existing2) || existing2.IsExpired)
                    _entries[connectionKey] = entry;
            }
        }

        private static CacheEntry LoadFromSmo(string connectionKey, ServerConnection serverConn) {
            var tables    = new List<TableInfo>();
            var columns   = new Dictionary<string, List<ColumnInfo>>(StringComparer.OrdinalIgnoreCase);
            var fksByTable = new Dictionary<string, List<ForeignKeyInfo>>(StringComparer.OrdinalIgnoreCase);
            IMetadataProvider metadataProvider = null;

            try {
                metadataProvider = SmoMetadataProvider.CreateConnectedProvider(serverConn);

                var server   = new Server(serverConn);
                var database = server.Databases[serverConn.DatabaseName];
                if (database == null) return CacheEntry.Empty();

                foreach (Table tbl in database.Tables) {
                    if (tbl.IsSystemObject) continue;

                    var ti = new TableInfo(tbl.Schema, tbl.Name);
                    tables.Add(ti);
                    string tblKey = MakeKey(tbl.Schema, tbl.Name);

                    var colList = new List<ColumnInfo>();
                    foreach (Column col in tbl.Columns)
                        colList.Add(new ColumnInfo(col.Name, col.DataType.Name));
                    columns[tblKey] = colList;

                    foreach (ForeignKey fk in tbl.ForeignKeys) {
                        var fkCols  = new List<string>();
                        var refCols = new List<string>();
                        foreach (ForeignKeyColumn fkc in fk.Columns) {
                            fkCols.Add(fkc.Name);
                            refCols.Add(fkc.ReferencedColumn);
                        }
                        var info = new ForeignKeyInfo(
                            tbl.Schema, tbl.Name, fkCols.AsReadOnly(),
                            fk.ReferencedTableSchema, fk.ReferencedTable, refCols.AsReadOnly());

                        // Index by FK owner table
                        if (!fksByTable.TryGetValue(tblKey, out var ownerList))
                            fksByTable[tblKey] = ownerList = new List<ForeignKeyInfo>();
                        ownerList.Add(info);

                        // Index by referenced (PK) table for reverse lookup
                        string refKey = MakeKey(fk.ReferencedTableSchema, fk.ReferencedTable);
                        if (!fksByTable.TryGetValue(refKey, out var refList))
                            fksByTable[refKey] = refList = new List<ForeignKeyInfo>();
                        refList.Add(info);
                    }
                }
            }
            catch { /* SMO errors must never crash the extension */ }

            var frozenCols = new Dictionary<string, IReadOnlyList<ColumnInfo>>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in columns) frozenCols[kv.Key] = kv.Value.AsReadOnly();

            var frozenFks = new Dictionary<string, IReadOnlyList<ForeignKeyInfo>>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in fksByTable) frozenFks[kv.Key] = kv.Value.AsReadOnly();

            return new CacheEntry(metadataProvider, tables.AsReadOnly(), frozenCols, frozenFks);
        }

        private static string MakeKey(string schema, string table)
            => $"{schema ?? "dbo"}.{table}";

        private sealed class CacheEntry {
            public readonly IMetadataProvider MetadataProvider;
            public readonly IReadOnlyList<TableInfo> Tables;
            public readonly IReadOnlyDictionary<string, IReadOnlyList<ColumnInfo>> Columns;
            public readonly IReadOnlyDictionary<string, IReadOnlyList<ForeignKeyInfo>> ForeignKeys;
            private readonly DateTime _loadedAt;

            public bool IsExpired => DateTime.UtcNow - _loadedAt > Ttl;

            public CacheEntry(
                IMetadataProvider provider,
                IReadOnlyList<TableInfo> tables,
                IReadOnlyDictionary<string, IReadOnlyList<ColumnInfo>> columns,
                IReadOnlyDictionary<string, IReadOnlyList<ForeignKeyInfo>> fks) {
                MetadataProvider = provider;
                Tables     = tables;
                Columns    = columns;
                ForeignKeys = fks;
                _loadedAt  = DateTime.UtcNow;
            }

            public static CacheEntry Empty() => new CacheEntry(
                null,
                Array.Empty<TableInfo>(),
                new Dictionary<string, IReadOnlyList<ColumnInfo>>(),
                new Dictionary<string, IReadOnlyList<ForeignKeyInfo>>());
        }
    }

    // ── AliasGenerator ───────────────────────────────────────────────────────

    internal static class AliasGenerator {
        public static string Generate(string tableName, ISet<string> existingAliases) {
            // Strip schema prefix and brackets
            int dotIdx = tableName.LastIndexOf('.');
            if (dotIdx >= 0) tableName = tableName.Substring(dotIdx + 1);
            tableName = tableName.Trim('[', ']');

            var words = SplitIntoWords(tableName);
            string baseAlias = words.Count > 0
                ? string.Concat(words.Select(w => char.ToLowerInvariant(w[0])))
                : tableName.Substring(0, 1).ToLowerInvariant();

            if (!existingAliases.Contains(baseAlias)) return baseAlias;

            for (int n = 2; n < 100; n++) {
                string candidate = baseAlias + n;
                if (!existingAliases.Contains(candidate)) return candidate;
            }
            return baseAlias;
        }

        private static List<string> SplitIntoWords(string name) {
            var words   = new List<string>();
            var current = new StringBuilder();

            for (int i = 0; i < name.Length; i++) {
                char c = name[i];
                if (c == '_') {
                    if (current.Length > 0) { words.Add(current.ToString()); current.Clear(); }
                    continue;
                }
                bool isBoundary = char.IsUpper(c) && current.Length > 0 &&
                                  (char.IsLower(current[current.Length - 1]) ||
                                   (i + 1 < name.Length && char.IsLower(name[i + 1])));
                if (isBoundary) { words.Add(current.ToString()); current.Clear(); }
                current.Append(c);
            }
            if (current.Length > 0) words.Add(current.ToString());
            return words;
        }
    }

    // ── SqlParserService ─────────────────────────────────────────────────────

    internal static class SqlParserService {
        // Convert linear buffer position → 1-based (line, col) for the SSMS parser
        public static (int line, int col) GetLineCol(ITextSnapshot snapshot, int position) {
            var textLine = snapshot.GetLineFromPosition(Math.Min(position, snapshot.Length));
            return (textLine.LineNumber + 1, position - textLine.Start.Position + 1);
        }

        public static ParseResult Parse(string sql) {
            try { return Parser.Parse(sql ?? ""); }
            catch { return null; }
        }

        // Returns completions from the SSMS Intellisense Resolver.
        // provider may be null — in that case only keyword completions are returned.
        public static List<CompletionItem> GetNativeCompletions(
            ParseResult parseResult, int line, int col, IMetadataProvider provider) {
            var result = new List<CompletionItem>();
            if (parseResult == null) return result;

            try {
                var displayProvider = provider as IMetadataDisplayInfoProvider;
                var declarations    = Resolver.FindCompletions(parseResult, line, col, displayProvider);
                if (declarations == null) return result;

                foreach (var d in declarations) {
                    if (string.IsNullOrEmpty(d.Title)) continue;
                    string insertText = d.Title;
                    if (/*d.Type == DeclarationType.Keyword ||*/
                        d.Type == DeclarationType.Table   ||
                        d.Type == DeclarationType.View)
                        insertText += " ";
                    result.Add(new CompletionItem(d.Title, insertText, d.Type.ToString()));
                }
            }
            catch { /* resolver errors are non-fatal */ }

            return result;
        }

        // Returns the text of the significant token immediately before the cursor.
        // Uses ParseResult.Script.TokenManager (no separate ScanSource needed).
        public static string GetPreviousSignificantText(ParseResult parseResult, int line, int col) {
            try {
                var tm       = parseResult?.Script?.TokenManager;
                if (tm == null) return null;
                int tokenIdx = tm.FindToken(line, col);
                if (tokenIdx < 0) return null;
                int prevIdx  = tm.GetPreviousSignificantTokenIndex(tokenIdx);
                if (prevIdx < 0) return null;
                return tm.GetText(prevIdx)?.Trim();
            }
            catch { return null; }
        }

        // Extracts alias→TableInfo map by scanning TokenManager for FROM/JOIN patterns.
        // TokenManager has no GetNextSignificantTokenIndex, so we use a local helper.
        public static Dictionary<string, TableInfo> ExtractAliasMap(ParseResult parseResult) {
            var map = new Dictionary<string, TableInfo>(StringComparer.OrdinalIgnoreCase);
            try {
                var tm    = parseResult?.Script?.TokenManager;
                if (tm == null) return map;
                int count = tm.Count;

                int i = 0;
                while (i < count) {
                    string text = tm.GetText(i) ?? "";

                    if (!string.Equals(text, "FROM", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(text, "JOIN", StringComparison.OrdinalIgnoreCase)) {
                        i++;
                        continue;
                    }

                    int next = NextSignificant(tm, i);
                    if (next < 0) break;

                    string schema    = "dbo";
                    string tableName;

                    string t1       = tm.GetText(next) ?? "";
                    int    afterT1  = NextSignificant(tm, next);
                    if (afterT1 >= 0 && tm.GetText(afterT1) == ".") {
                        schema = t1.Trim('[', ']');
                        int tblIdx = NextSignificant(tm, afterT1);
                        if (tblIdx < 0) { i = afterT1 + 1; continue; }
                        tableName = (tm.GetText(tblIdx) ?? "").Trim('[', ']');
                        next      = tblIdx;
                    } else {
                        tableName = t1.Trim('[', ']');
                    }

                    if (string.IsNullOrEmpty(tableName)) { i = next + 1; continue; }

                    int afterTable = NextSignificant(tm, next);
                    if (afterTable >= 0 && string.Equals(tm.GetText(afterTable), "AS", StringComparison.OrdinalIgnoreCase))
                        afterTable = NextSignificant(tm, afterTable);

                    string alias;
                    if (afterTable >= 0 && !SqlKeywords.Contains(tm.GetText(afterTable) ?? "")) {
                        alias = (tm.GetText(afterTable) ?? "").Trim('[', ']');
                        i     = afterTable + 1;
                    } else {
                        alias = tableName;
                        i     = next + 1;
                    }

                    if (!string.IsNullOrEmpty(alias))
                        map[alias.ToLowerInvariant()] = new TableInfo(schema, tableName);
                }
            }
            catch { }
            return map;
        }

        // Forward-scan helper: returns index of next significant (non-whitespace/comment) token.
        public static int NextSignificant(TokenManager tm, int startIdx) {
            for (int i = startIdx + 1; i < tm.Count; i++) {
                try { if (tm.GetToken(i)?.IsSignificant == true) return i; }
                catch { }
            }
            return -1;
        }

        private static readonly HashSet<string> SqlKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "SELECT","FROM","WHERE","JOIN","INNER","LEFT","RIGHT","OUTER","CROSS","FULL",
            "ON","AS","AND","OR","NOT","IN","IS","NULL","LIKE","BETWEEN","ORDER","GROUP",
            "BY","HAVING","UNION","ALL","DISTINCT","TOP","INTO","VALUES","INSERT","UPDATE",
            "DELETE","SET","TABLE","WITH","EXISTS","CASE","WHEN","THEN","ELSE","END",
            "ASC","DESC","LIMIT","OFFSET"
        };
    }

    // ── FkJoinProvider ───────────────────────────────────────────────────────

    internal static class FkJoinProvider {
        public static List<CompletionItem> GetFkCompletions(
            string connectionKey, ParseResult parseResult, int line, int col) {
            var result = new List<CompletionItem>();
            if (connectionKey == null || parseResult == null) return result;

            try {
                string prevToken = SqlParserService.GetPreviousSignificantText(parseResult, line, col);
                bool afterOn     = string.Equals(prevToken, "ON", StringComparison.OrdinalIgnoreCase);
                bool afterWhere  = IsInsideWhereClause(parseResult, line, col);

                if (!afterOn && !afterWhere) return result;

                var aliasMap = SqlParserService.ExtractAliasMap(parseResult);
                if (aliasMap.Count < 2) return result;

                var aliasList = aliasMap.ToList();
                for (int i = 0; i < aliasList.Count; i++) {
                    for (int j = i + 1; j < aliasList.Count; j++) {
                        string aliasA = aliasList[i].Key;
                        var    tableA = aliasList[i].Value;
                        string aliasB = aliasList[j].Key;
                        var    tableB = aliasList[j].Value;

                        var fks = DatabaseMetadataCache.GetForeignKeys(
                            connectionKey, tableA.Schema, tableA.TableName);

                        foreach (var fk in fks) {
                            // tableA owns FK → tableB
                            if (string.Equals(fk.ReferencedTable,  tableB.TableName, StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(fk.ReferencedSchema, tableB.Schema,     StringComparison.OrdinalIgnoreCase)) {
                                string cond = BuildCondition(aliasA, fk.FkColumns, aliasB, fk.ReferencedColumns);
                                result.Add(new CompletionItem(cond, cond, "Relation FK/PK"));
                            }
                            // tableA is PK side, tableB owns the FK
                            if (string.Equals(fk.FkTable,  tableB.TableName, StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(fk.FkSchema, tableB.Schema,     StringComparison.OrdinalIgnoreCase)) {
                                string cond = BuildCondition(aliasB, fk.FkColumns, aliasA, fk.ReferencedColumns);
                                result.Add(new CompletionItem(cond, cond, "Relation FK/PK"));
                            }
                        }
                    }
                }
            }
            catch { }
            return result;
        }

        private static bool IsInsideWhereClause(ParseResult parseResult, int line, int col) {
            try {
                var tm       = parseResult?.Script?.TokenManager;
                if (tm == null) return false;
                int cursorTk = tm.FindToken(line, col);
                int lastWhere = -1, lastFromJoin = -1;
                for (int i = 0; i < cursorTk && i < tm.Count; i++) {
                    string t = tm.GetText(i)?.ToUpperInvariant() ?? "";
                    if (t == "WHERE") lastWhere    = i;
                    if (t == "FROM" || t == "JOIN") lastFromJoin = i;
                }
                return lastWhere > lastFromJoin && lastWhere >= 0;
            }
            catch { return false; }
        }

        private static string BuildCondition(
            string fkAlias, IReadOnlyList<string> fkCols,
            string pkAlias, IReadOnlyList<string> pkCols) {
            var parts = new List<string>();
            for (int k = 0; k < Math.Min(fkCols.Count, pkCols.Count); k++)
                parts.Add($"{fkAlias}.{fkCols[k]} = {pkAlias}.{pkCols[k]}");
            return string.Join(" AND ", parts);
        }
    }

    // ── AutoCompletePopup ────────────────────────────────────────────────────

    internal sealed class AutoCompletePopup {
        private Popup         _popup;
        private ListBox       _listBox;
        private IWpfTextView  _textView;
        private ITrackingSpan _wordSpan;
        private List<CompletionItem> _allItems = new List<CompletionItem>();

        public bool IsVisible => _popup?.IsOpen == true;

        public AutoCompletePopup(IWpfTextView textView) {
            _textView = textView;

            _listBox = new ListBox {
                MaxHeight   = 220,
                MinWidth    = 280,
                FontFamily  = new FontFamily("Consolas, Courier New"),
                FontSize    = 12,
                Background  = new SolidColorBrush(Color.FromRgb(37, 37, 38)),
                Foreground  = new SolidColorBrush(Colors.White),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0, 122, 204)),
                BorderThickness = new Thickness(1),
                SelectionMode = SelectionMode.Single,
            };

            _popup = new Popup {
                Child           = _listBox,
                StaysOpen       = false,
                AllowsTransparency = true,
                Placement       = PlacementMode.RelativePoint,
                PlacementTarget = (UIElement)_textView.VisualElement,
            };

            _popup.Closed += (s, e) => _allItems.Clear();
        }

        public void Show(IList<CompletionItem> items, ITrackingSpan wordSpan) {
            if (items == null || items.Count == 0) return;
            _wordSpan  = wordSpan;
            _allItems  = items.ToList();
            _listBox.ItemsSource   = _allItems.Select(i => i.DisplayText).ToList();
            _listBox.SelectedIndex = 0;

            double caretLeft   = _textView.Caret.Left;
            double caretBottom = _textView.Caret.Bottom;
            _popup.HorizontalOffset = caretLeft;
            _popup.VerticalOffset   = caretBottom;
            _popup.IsOpen           = true;

            // Keep keyboard focus in the text editor
            _listBox.Focusable = false;
        }

        public void Filter(string prefix) {
            if (!IsVisible) return;
            var filtered = string.IsNullOrEmpty(prefix)
                ? _allItems
                : _allItems.Where(i => i.DisplayText.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();

            if (filtered.Count == 0) { Dismiss(); return; }
            _listBox.ItemsSource   = filtered.Select(i => i.DisplayText).ToList();
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

        public void Commit() {
            var selected = _listBox.SelectedItem as string;
            if (selected == null) { Dismiss(); return; }

            // Find the full CompletionItem to get InsertText
            var item = _allItems.FirstOrDefault(i => i.DisplayText == selected);
            string insertText = item?.InsertText ?? selected;

            Dismiss();

            try {
                var snapshot = _textView.TextBuffer.CurrentSnapshot;
                var span     = _wordSpan.GetSpan(snapshot);
                _textView.TextBuffer.Replace(span, insertText);
            }
            catch { }
        }

        public void Dismiss() {
            if (_popup != null) _popup.IsOpen = false;
        }
    }

    // ── SqlCommandFilter ─────────────────────────────────────────────────────

    internal sealed class SqlCommandFilter : IOleCommandTarget {
        private readonly IWpfTextView    _textView;
        private readonly AutoCompletePopup _popup;
        private readonly string          _connectionKey;
        public  IOleCommandTarget        Next { get; set; }

        public SqlCommandFilter(IWpfTextView textView, string connectionKey) {
            _textView      = textView;
            _connectionKey = connectionKey;
            _popup         = new AutoCompletePopup(textView);
        }

        public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut) {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (pguidCmdGroup == VSConstants.VSStd2K) {
                switch ((VSConstants.VSStd2KCmdID)nCmdID) {
                    case VSConstants.VSStd2KCmdID.RETURN:
                    case VSConstants.VSStd2KCmdID.TAB:
                        if (_popup.IsVisible) { _popup.Commit(); return VSConstants.S_OK; }
                        break;

                    //case VSConstants.VSStd2KCmdID.ESCAPE:
                    case VSConstants.VSStd2KCmdID.CANCEL:
                        if (_popup.IsVisible) { _popup.Dismiss(); return VSConstants.S_OK; }
                        break;

                    case VSConstants.VSStd2KCmdID.UP:
                        if (_popup.IsVisible) { _popup.MoveUp(); return VSConstants.S_OK; }
                        break;

                    case VSConstants.VSStd2KCmdID.DOWN:
                        if (_popup.IsVisible) { _popup.MoveDown(); return VSConstants.S_OK; }
                        break;
                }
            }

            int hr = Next.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);

            if (pguidCmdGroup == VSConstants.VSStd2K &&
                nCmdID == (uint)VSConstants.VSStd2KCmdID.TYPECHAR) {
                char c = (char)(ushort)Marshal.GetObjectForNativeVariant(pvaIn);

                if (char.IsLetterOrDigit(c) || c == '_') {
                    // Capture state on UI thread before going async
                    int    caretPos = _textView.Caret.Position.BufferPosition.Position;
                    string sql      = _textView.TextBuffer.CurrentSnapshot.GetText();
                    var    snapshot = _textView.TextBuffer.CurrentSnapshot;

                    if (_popup.IsVisible) {
                        // Just re-filter with the new character
                        string prefix = GetCurrentWord(snapshot, caretPos);
                        _popup.Filter(prefix);
                    } else {
                        Task.Run(() => TriggerAsync(snapshot, caretPos, sql));
                    }
                } else if (c == '.') {
                    int    caretPos = _textView.Caret.Position.BufferPosition.Position;
                    string sql      = _textView.TextBuffer.CurrentSnapshot.GetText();
                    var    snapshot = _textView.TextBuffer.CurrentSnapshot;
                    _popup.Dismiss();
                    Task.Run(() => TriggerAsync(snapshot, caretPos, sql));
                } else {
                    _popup.Dismiss();
                }
            }

            return hr;
        }

        private void TriggerAsync(ITextSnapshot snapshot, int caretPos, string sql) {
            try {
                var (line, col) = SqlParserService.GetLineCol(snapshot, caretPos);

                var provider    = DatabaseMetadataCache.GetMetadataProvider(_connectionKey);
                var parseResult = SqlParserService.Parse(sql);

                var items = new List<CompletionItem>();

                // 1. SSMS native completions (keywords, tables, columns from metadata)
                items.AddRange(SqlParserService.GetNativeCompletions(parseResult, line, col, provider));

                // 2. FK/PK join condition suggestions
                items.AddRange(FkJoinProvider.GetFkCompletions(_connectionKey, parseResult, line, col));

                // 3. Alias suggestion: if just after a table name in FROM/JOIN
                var aliasItems = GetAliasSuggestions(sql, line, col);
                items.AddRange(aliasItems);

                // 4. Keyword fallback — ensures popup always shows when native/parser fails
                foreach (var kw in SqlKeywordSet)
                    items.Add(new CompletionItem(kw, kw + " ", "Keyword"));

                // Deduplicate by DisplayText
                items = items
                    .GroupBy(i => i.DisplayText, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();

                // Filter by word prefix at cursor
                string prefix = GetCurrentWord(snapshot, caretPos);
                if (!string.IsNullOrEmpty(prefix))
                    items = items.Where(i => i.DisplayText.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();

                if (items.Count == 0) return;

                ITrackingSpan wordSpan = GetWordSpan(snapshot, caretPos);

                _textView.VisualElement.Dispatcher.InvokeAsync(() => {
                    if (!_popup.IsVisible)
                        _popup.Show(items, wordSpan);
                });
            }
            catch { }
        }

        private List<CompletionItem> GetAliasSuggestions(string sql, int line, int col) {
            var result = new List<CompletionItem>();
            try {
                // Context: is the previous significant token a non-keyword identifier
                // AND the token before that is FROM or JOIN?
                var tm       = Parser.Parse(sql ?? "").Script?.TokenManager;
                if (tm == null) return result;
                int cursorTk = tm.FindToken(line, col);
                if (cursorTk < 0) return result;

                int prev1 = tm.GetPreviousSignificantTokenIndex(cursorTk);
                if (prev1 < 0) return result;
                string prev1Text = tm.GetText(prev1)?.Trim() ?? "";

                int prev2 = tm.GetPreviousSignificantTokenIndex(prev1);
                if (prev2 < 0) return result;
                string prev2Text = tm.GetText(prev2)?.Trim().ToUpperInvariant() ?? "";

                // Pattern: FROM/JOIN <tableName> <cursor>
                bool isAlias = (prev2Text == "FROM" || prev2Text == "JOIN") &&
                               !string.IsNullOrEmpty(prev1Text) &&
                               !SqlKeywordSet.Contains(prev1Text);

                // Also handle: FROM/JOIN <tableName> AS <cursor>
                if (!isAlias && string.Equals(prev1Text, "AS", StringComparison.OrdinalIgnoreCase)) {
                    int prev3 = tm.GetPreviousSignificantTokenIndex(prev2);
                    string prev3Text = prev3 >= 0 ? tm.GetText(prev3)?.Trim().ToUpperInvariant() ?? "" : "";
                    if (prev3Text == "FROM" || prev3Text == "JOIN") {
                        prev1Text = tm.GetText(prev2)?.Trim() ?? "";
                        isAlias   = !string.IsNullOrEmpty(prev1Text);
                    }
                }

                if (!isAlias) return result;

                var existingAliases = new HashSet<string>(
                    SqlParserService.ExtractAliasMap(SqlParserService.Parse(sql)).Keys,
                    StringComparer.OrdinalIgnoreCase);

                string suggested = AliasGenerator.Generate(prev1Text, existingAliases);
                result.Add(new CompletionItem(
                    suggested,
                    suggested + " ",
                    $"Alias suggéré pour {prev1Text}"));
            }
            catch { }
            return result;
        }

        private static string GetCurrentWord(ITextSnapshot snapshot, int caretPos) {
            if (caretPos <= 0 || caretPos > snapshot.Length) return "";
            int start = caretPos;
            while (start > 0 && IsWordChar(snapshot[start - 1])) start--;
            return snapshot.GetText(start, caretPos - start);
        }

        private static ITrackingSpan GetWordSpan(ITextSnapshot snapshot, int caretPos) {
            int start = Math.Min(caretPos, snapshot.Length);
            while (start > 0 && IsWordChar(snapshot[start - 1])) start--;
            int length = Math.Min(caretPos, snapshot.Length) - start;
            return snapshot.CreateTrackingSpan(
                new SnapshotSpan(snapshot, start, length),
                SpanTrackingMode.EdgeInclusive);
        }

        private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

        public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pcmdText) {
            ThreadHelper.ThrowIfNotOnUIThread();
            return Next.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pcmdText);
        }

        private static readonly HashSet<string> SqlKeywordSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "SELECT","FROM","WHERE","JOIN","INNER","LEFT","RIGHT","OUTER","CROSS","FULL",
            "ON","AS","AND","OR","NOT","IN","IS","NULL","LIKE","BETWEEN","ORDER","GROUP",
            "BY","HAVING","UNION","ALL","DISTINCT","TOP","INTO","VALUES","INSERT","UPDATE",
            "DELETE","SET","TABLE","WITH","EXISTS","CASE","WHEN","THEN","ELSE","END"
        };
    }

    // ── VsTextViewCreationSqlListener ────────────────────────────────────────
    // Merged with the old SqlCompletionController: handles both metadata warm-up
    // and SqlCommandFilter injection into the command chain.

    [Export(typeof(IVsTextViewCreationListener))]
    [ContentType("SQL")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    internal class VsTextViewCreationSqlListener : IVsTextViewCreationListener {
        [Import]
        internal IVsEditorAdaptersFactoryService AdaptersFactory = null;

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IVsTextView, ConnectionKeyHolder>
            _viewKeys = new System.Runtime.CompilerServices.ConditionalWeakTable<IVsTextView, ConnectionKeyHolder>();

        public void VsTextViewCreated(IVsTextView textViewAdapter) {
            // Capture connection info on the UI thread
            string connectionKey = ConnectionHelper.GetConnectionKey();
            var    serverConn    = ConnectionHelper.BuildServerConnection();

            // Detect connection change per view and invalidate stale cache
            if (_viewKeys.TryGetValue(textViewAdapter, out var holder)) {
                if (!string.Equals(holder.Key, connectionKey, StringComparison.OrdinalIgnoreCase)) {
                    DatabaseMetadataCache.Invalidate(holder.Key);
                    holder.Key = connectionKey;
                }
            } else if (connectionKey != null) {
                _viewKeys.Add(textViewAdapter, new ConnectionKeyHolder { Key = connectionKey });
            }

            // Warm the metadata cache in background
            DatabaseMetadataCache.WarmAsync(connectionKey, serverConn);

            // Inject our custom command filter
            IWpfTextView textView = AdaptersFactory?.GetWpfTextView(textViewAdapter);
            if (textView == null) return;

            var filter = new SqlCommandFilter(textView, connectionKey);
            textViewAdapter.AddCommandFilter(filter, out IOleCommandTarget next);
            filter.Next = next;
        }

        private sealed class ConnectionKeyHolder { public string Key { get; set; } }
    }

    // ── SsmsAutocompletionPackage ────────────────────────────────────────────

    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(SsmsAutocompletionPackage.PackageGuidString)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.NoSolution_string,              PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string,          PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.SolutionHasMultipleProjects_string, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.SolutionHasSingleProject_string,    PackageAutoLoadFlags.BackgroundLoad)]
    public sealed class SsmsAutocompletionPackage : AsyncPackage {
        public const string PackageGuidString = "4eba5e14-fb9d-4202-8e7c-49eb8f2c5467";

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress) {
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        }
    }

    // ── Test (kept for reference / manual testing) ───────────────────────────

    public class Test {
        public static IMetadataProvider GetProviderSafe() {
            var scriptFactory = ServiceCache.ScriptFactory;
            var activeWndInfo = scriptFactory?.CurrentlyActiveWndConnectionInfo;
            if (activeWndInfo == null) return null;

            PropertyInfo uiConnProp = activeWndInfo.GetType().GetProperty("UIConnectionInfo");
            if (uiConnProp == null) return null;

            object uiConnectionInfo = uiConnProp.GetValue(activeWndInfo);
            if (uiConnectionInfo == null) return null;

            string serverName = GetPropValue(uiConnectionInfo, "ServerName") as string;
            string userName   = GetPropValue(uiConnectionInfo, "UserName")   as string;
            string password   = GetPropValue(uiConnectionInfo, "Password")   as string;
            int authType      = (int)(GetPropValue(uiConnectionInfo, "AuthenticationType") ?? 0);

            string databaseName = "master";
            var adv = GetPropValue(uiConnectionInfo, "AdvancedOptions") as NameValueCollection;
            if (adv != null && !string.IsNullOrWhiteSpace(adv["DATABASE"]))
                databaseName = adv["DATABASE"];

            var serverConnection = new ServerConnection(serverName) { DatabaseName = databaseName };
            if (authType == 0) {
                serverConnection.LoginSecure = true;
            } else {
                serverConnection.LoginSecure = false;
                serverConnection.Login    = userName;
                serverConnection.Password = password;
            }

            return SmoMetadataProvider.CreateConnectedProvider(serverConnection);
        }

        private static object GetPropValue(object src, string propName)
            => src.GetType().GetProperty(propName)?.GetValue(src, null);
    }
}
