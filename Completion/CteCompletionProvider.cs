using System;
using System.Collections.Generic;

namespace SsmsAutocompletion {

    /// <summary>
    /// Suggère les noms de CTEs (WITH … AS) définis dans la requête courante.
    /// </summary>
    internal sealed class CteCompletionProvider : ICompletionProvider {
        private readonly ICteExtractor _cteExtractor;

        public CteCompletionProvider(ICteExtractor cteExtractor) {
            _cteExtractor = cteExtractor;
        }

        public IReadOnlyList<CompletionItem> GetCompletions(CompletionRequest request) {
            if (request.IsDotContext) return Array.Empty<CompletionItem>();
            var names = _cteExtractor.Extract(request.ParseResult);
            if (names.Count == 0) return Array.Empty<CompletionItem>();
            var items = new List<CompletionItem>(names.Count);
            foreach (string name in names)
                items.Add(new CompletionItem(name, name + " ", "CTE", CompletionItemKind.Cte));
            return items.AsReadOnly();
        }
    }
}
