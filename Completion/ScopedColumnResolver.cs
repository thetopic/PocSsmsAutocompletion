using System;
using System.Collections.Generic;
using System.Linq;

namespace SsmsAutocompletion {

    internal sealed class ScopedColumnResolver : IScopedColumnResolver {
        private readonly IDatabaseMetadata      _databaseMetadata;
        private readonly IAliasExtractor        _aliasExtractor;
        private readonly ICteExtractor          _cteExtractor;
        private readonly ICteColumnExtractor    _cteColumnExtractor;
        private readonly IDerivedTableExtractor _derivedTableExtractor;

        public ScopedColumnResolver(
            IDatabaseMetadata databaseMetadata, IAliasExtractor aliasExtractor,
            ICteExtractor cteExtractor, ICteColumnExtractor cteColumnExtractor,
            IDerivedTableExtractor derivedTableExtractor) {
            _databaseMetadata      = databaseMetadata;
            _aliasExtractor        = aliasExtractor;
            _cteExtractor          = cteExtractor;
            _cteColumnExtractor    = cteColumnExtractor;
            _derivedTableExtractor = derivedTableExtractor;
        }

        public IReadOnlyList<CompletionItem> GetVisibleColumns(CompletionRequest request) {
            var aliasMap      = _aliasExtractor.ExtractInScope(request.ParseResult, request.Line, request.Column);
            var derivedTables = _derivedTableExtractor.Extract(request.ParseResult);
            var cteNames      = _cteExtractor.Extract(request.ParseResult);

            var seen  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var items = new List<CompletionItem>();

            foreach (var derivedColumns in derivedTables.Values)
                AddColumns(items, seen, derivedColumns);

            foreach (var kv in aliasMap) {
                string    alias = kv.Key;
                TableInfo table = kv.Value;

                if (derivedTables.ContainsKey(alias)) continue; // already added above

                string cteName = cteNames.FirstOrDefault(
                    n => string.Equals(n, table.TableName, StringComparison.OrdinalIgnoreCase));
                if (cteName != null) {
                    AddColumns(items, seen, _cteColumnExtractor.ExtractColumns(request.ParseResult, cteName));
                    continue;
                }

                var columns = _databaseMetadata.GetColumns(request.ConnectionKey, table.Schema, table.TableName);
                foreach (var col in columns) {
                    if (!seen.Add(col.ColumnName)) continue;
                    items.Add(new CompletionItem(col.ColumnName, col.ColumnName, col.DataType, CompletionItemKind.Column));
                }
            }
            return items.AsReadOnly();
        }

        private static void AddColumns(List<CompletionItem> items, HashSet<string> seen, IReadOnlyList<string> columnNames) {
            foreach (string col in columnNames) {
                if (!seen.Add(col)) continue;
                items.Add(new CompletionItem(col, col, null, CompletionItemKind.Column));
            }
        }
    }
}
