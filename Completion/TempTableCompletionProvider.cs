using System;
using System.Collections.Generic;

namespace SsmsAutocompletion {

    internal sealed class TempTableCompletionProvider : ICompletionProvider {
        private readonly ITempTableExtractor _tempTableExtractor;

        public TempTableCompletionProvider(ITempTableExtractor tempTableExtractor) {
            _tempTableExtractor = tempTableExtractor;
        }

        public IReadOnlyList<CompletionItem> GetCompletions(CompletionRequest request) {
            if (request.IsDotContext) return Array.Empty<CompletionItem>();
            if (!request.IsAfterFromKeyword && !request.IsAfterJoinKeyword) return Array.Empty<CompletionItem>();

            var tempTables = _tempTableExtractor.Extract(request.ParseResult);
            var items = new List<CompletionItem>(tempTables.Count);
            foreach (var name in tempTables.Keys)
                items.Add(new CompletionItem(name, name + " ", "Table temporaire", CompletionItemKind.Table, rank: 1));
            return items.AsReadOnly();
        }
    }
}
