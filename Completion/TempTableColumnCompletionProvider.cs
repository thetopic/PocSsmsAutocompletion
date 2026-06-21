using System;
using System.Collections.Generic;

namespace SsmsAutocompletion {

    internal sealed class TempTableColumnCompletionProvider : ICompletionProvider {
        private readonly ITempTableExtractor _tempTableExtractor;
        private readonly IAliasExtractor     _aliasExtractor;

        public TempTableColumnCompletionProvider(
            ITempTableExtractor tempTableExtractor, IAliasExtractor aliasExtractor) {
            _tempTableExtractor = tempTableExtractor;
            _aliasExtractor     = aliasExtractor;
        }

        public IReadOnlyList<CompletionItem> GetCompletions(CompletionRequest request) {
            if (!request.IsDotContext) return Array.Empty<CompletionItem>();
            if (string.IsNullOrEmpty(request.Qualifier)) return Array.Empty<CompletionItem>();

            var tempTables = _tempTableExtractor.Extract(request.ParseResult);
            if (!tempTables.TryGetValue(request.Qualifier, out var columns)) {
                // Qualifier may be an alias pointing at a temp table (e.g. "FROM #temp t WHERE t.")
                var aliasMap = _aliasExtractor.Extract(request.ParseResult);
                if (!aliasMap.TryGetValue(request.Qualifier.ToLowerInvariant(), out var table)) return Array.Empty<CompletionItem>();
                if (!tempTables.TryGetValue(table.TableName, out columns)) return Array.Empty<CompletionItem>();
            }

            var items = new List<CompletionItem>(columns.Count);
            foreach (string col in columns)
                items.Add(new CompletionItem(col, col, "Colonne de table temporaire", CompletionItemKind.Column));
            return items.AsReadOnly();
        }
    }
}
