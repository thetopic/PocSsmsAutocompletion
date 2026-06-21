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
            if (names.Count == 0) {
                if (!request.IsAfterWithKeyword) return Array.Empty<CompletionItem>();
                return new[] { BuildSkeletonItem() };
            }
            var items = new List<CompletionItem>(names.Count);
            foreach (string name in names)
                items.Add(new CompletionItem(name, name + " ", "CTE", CompletionItemKind.Cte, rank: 1));
            return items.AsReadOnly();
        }

        private static CompletionItem BuildSkeletonItem() {
            string insertText = "cte AS (\n    SELECT \n)\nSELECT * FROM cte";
            return new CompletionItem("cte AS (...)", insertText, "Squelette de CTE", CompletionItemKind.Cte, rank: 1);
        }
    }
}
