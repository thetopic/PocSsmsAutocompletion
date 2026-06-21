using System;
using System.Collections.Generic;

namespace SsmsAutocompletion {

    internal sealed class UnqualifiedColumnCompletionProvider : ICompletionProvider {
        private readonly IDatabaseMetadata _databaseMetadata;
        private readonly IAliasExtractor   _aliasExtractor;

        public UnqualifiedColumnCompletionProvider(
            IDatabaseMetadata databaseMetadata, IAliasExtractor aliasExtractor) {
            _databaseMetadata = databaseMetadata;
            _aliasExtractor   = aliasExtractor;
        }

        public IReadOnlyList<CompletionItem> GetCompletions(CompletionRequest request) {
            if (request.IsDotContext)                                              return Array.Empty<CompletionItem>();
            if (!request.IsInSelectList && !request.IsWhereContext)                return Array.Empty<CompletionItem>();
            if (request.ConnectionKey == null || request.ConnectionKey.IsEmpty)    return Array.Empty<CompletionItem>();

            var aliasMap = _aliasExtractor.ExtractInScope(request.ParseResult, request.Line, request.Column);
            if (aliasMap.Count == 0) return Array.Empty<CompletionItem>();

            var seen  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var items = new List<CompletionItem>();
            foreach (var kv in aliasMap) {
                var table   = kv.Value;
                var columns = _databaseMetadata.GetColumns(request.ConnectionKey, table.Schema, table.TableName);
                foreach (var col in columns) {
                    if (!seen.Add(col.ColumnName)) continue;
                    items.Add(new CompletionItem(col.ColumnName, col.ColumnName, col.DataType, CompletionItemKind.Column, rank: 1));
                }
            }
            return items.AsReadOnly();
        }
    }
}
