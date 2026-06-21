using System;
using System.Collections.Generic;

namespace SsmsAutocompletion {

    internal sealed class DerivedTableColumnCompletionProvider : ICompletionProvider {
        private readonly IDerivedTableExtractor _derivedTableExtractor;

        public DerivedTableColumnCompletionProvider(IDerivedTableExtractor derivedTableExtractor) {
            _derivedTableExtractor = derivedTableExtractor;
        }

        public IReadOnlyList<CompletionItem> GetCompletions(CompletionRequest request) {
            if (!request.IsDotContext) return Array.Empty<CompletionItem>();
            if (string.IsNullOrEmpty(request.Qualifier)) return Array.Empty<CompletionItem>();

            var derivedTables = _derivedTableExtractor.Extract(request.ParseResult);
            if (!derivedTables.TryGetValue(request.Qualifier, out var columns)) return Array.Empty<CompletionItem>();

            var items = new List<CompletionItem>(columns.Count);
            foreach (string col in columns)
                items.Add(new CompletionItem(col, col, "Colonne de table dérivée", CompletionItemKind.Column));
            return items.AsReadOnly();
        }
    }
}
