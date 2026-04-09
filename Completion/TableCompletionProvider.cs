using System;
using System.Collections.Generic;

namespace SsmsAutocompletion {

    internal sealed class TableCompletionProvider : ICompletionProvider {
        private readonly IDatabaseMetadata _databaseMetadata;

        public TableCompletionProvider(IDatabaseMetadata databaseMetadata) {
            _databaseMetadata = databaseMetadata;
        }

        public IReadOnlyList<CompletionItem> GetCompletions(CompletionRequest request) {
            if (request.IsDotContext) return Array.Empty<CompletionItem>();
            if (request.ConnectionKey == null || request.ConnectionKey.IsEmpty)
                return Array.Empty<CompletionItem>();
            var tables = _databaseMetadata.GetTables(request.ConnectionKey);
            var items  = new List<CompletionItem>(tables.Count);
            foreach (var table in tables)
                items.Add(new CompletionItem(table.ToString(), table.ToString() + " ", "Table", CompletionItemKind.Table));
            return items.AsReadOnly();
        }
    }
}
