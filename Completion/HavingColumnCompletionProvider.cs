using System;
using System.Collections.Generic;
using System.Linq;

namespace SsmsAutocompletion {

    internal sealed class HavingColumnCompletionProvider : ICompletionProvider {
        private readonly IScopedColumnResolver _scopedColumnResolver;

        public HavingColumnCompletionProvider(IScopedColumnResolver scopedColumnResolver) {
            _scopedColumnResolver = scopedColumnResolver;
        }

        public IReadOnlyList<CompletionItem> GetCompletions(CompletionRequest request) {
            if (!request.IsHavingContext) return Array.Empty<CompletionItem>();
            return _scopedColumnResolver.GetVisibleColumns(request)
                .Select(i => new CompletionItem(i.DisplayText, i.InsertText, i.Description, i.Kind, rank: 1))
                .ToList()
                .AsReadOnly();
        }
    }
}
