using System;
using System.Collections.Generic;

namespace SsmsAutocompletion {

    internal sealed class OrderByColumnCompletionProvider : ICompletionProvider {
        private readonly IScopedColumnResolver    _scopedColumnResolver;
        private readonly ISelectListAliasExtractor _selectListAliasExtractor;

        public OrderByColumnCompletionProvider(
            IScopedColumnResolver scopedColumnResolver, ISelectListAliasExtractor selectListAliasExtractor) {
            _scopedColumnResolver      = scopedColumnResolver;
            _selectListAliasExtractor  = selectListAliasExtractor;
        }

        public IReadOnlyList<CompletionItem> GetCompletions(CompletionRequest request) {
            if (!request.IsOrderByContext) return Array.Empty<CompletionItem>();

            var items = new List<CompletionItem>();
            var seen  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var i in _scopedColumnResolver.GetVisibleColumns(request)) {
                if (!seen.Add(i.DisplayText)) continue;
                items.Add(new CompletionItem(i.DisplayText, i.InsertText, i.Description, i.Kind, rank: 1));
            }

            var aliases = _selectListAliasExtractor.Extract(request.ParseResult, request.Line, request.Column);
            foreach (string alias in aliases) {
                if (!seen.Add(alias)) continue;
                items.Add(new CompletionItem(alias, alias, "Alias de la liste SELECT", CompletionItemKind.Column, rank: 1));
            }
            return items.AsReadOnly();
        }
    }
}
