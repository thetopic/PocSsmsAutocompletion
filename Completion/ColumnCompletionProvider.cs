using System;
using System.Collections.Generic;
using System.Linq;

namespace SsmsAutocompletion {

    internal sealed class ColumnCompletionProvider : ICompletionProvider {
        private readonly IDatabaseMetadata _databaseMetadata;
        private readonly IAliasExtractor   _aliasExtractor;

        public ColumnCompletionProvider(IDatabaseMetadata databaseMetadata, IAliasExtractor aliasExtractor) {
            _databaseMetadata = databaseMetadata;
            _aliasExtractor   = aliasExtractor;
        }

        public IReadOnlyList<CompletionItem> GetCompletions(CompletionRequest request) {
            if (!request.IsDotContext) return Array.Empty<CompletionItem>();
            if (string.IsNullOrEmpty(request.Qualifier)) return Array.Empty<CompletionItem>();
            var tableInfo = ResolveTable(request);
            if (tableInfo == null) return Array.Empty<CompletionItem>();
            var columns = _databaseMetadata.GetColumns(request.ConnectionKey, tableInfo.Schema, tableInfo.TableName);
            var items   = new List<CompletionItem>(columns.Count);
            foreach (var column in columns)
                items.Add(new CompletionItem(column.ColumnName, column.ColumnName, column.DataType, CompletionItemKind.Column));
            return items.AsReadOnly();
        }

        private TableInfo ResolveTable(CompletionRequest request) {
            var aliasMap = request.ParseResult != null
                ? _aliasExtractor.Extract(request.ParseResult)
                : _aliasExtractor.Extract(request.Sql);
            aliasMap.TryGetValue(request.Qualifier.ToLowerInvariant(), out TableInfo tableInfo);
            if (tableInfo != null) return tableInfo;
            return _databaseMetadata.GetTables(request.ConnectionKey)
                .FirstOrDefault(t => string.Equals(t.TableName, request.Qualifier, StringComparison.OrdinalIgnoreCase));
        }
    }
}
