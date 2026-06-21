using System;
using System.Collections.Generic;

namespace SsmsAutocompletion {

    internal sealed class CompletionEngine {
        private readonly IReadOnlyList<ICompletionProvider> _providers;
        private readonly CompletionRequestBuilder           _requestBuilder;
        private readonly IContextDetector                   _contextDetector;

        public CompletionEngine(
            IReadOnlyList<ICompletionProvider> providers,
            CompletionRequestBuilder requestBuilder,
            IContextDetector contextDetector) {
            _providers       = providers;
            _requestBuilder  = requestBuilder;
            _contextDetector = contextDetector;
        }

        public IReadOnlyList<CompletionItem> GetCompletions(
            Microsoft.VisualStudio.Text.ITextSnapshot snapshot,
            string sql, int caretPosition, ConnectionKey connectionKey) {
            var request      = _requestBuilder.Build(snapshot, sql, caretPosition, connectionKey);
            var allItems     = CollectAllItems(request);
            var deduplicated = Deduplicate(allItems);
            var sorted       = SortByRank(deduplicated);
            return FilterByPrefix(sorted, _contextDetector.GetCurrentWord(snapshot, caretPosition));
        }

        private List<CompletionItem> CollectAllItems(CompletionRequest request) {
            var allItems = new List<CompletionItem>();
            foreach (var provider in _providers)
                allItems.AddRange(provider.GetCompletions(request));
            return allItems;
        }

        private static IReadOnlyList<CompletionItem> Deduplicate(List<CompletionItem> items) {
            var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<CompletionItem>(items.Count);
            foreach (var item in items) {
                if (!seen.Add(GetDeduplicationKey(item))) continue;
                result.Add(item);
            }
            return result.AsReadOnly();
        }

        private static string GetDeduplicationKey(CompletionItem item) {
            if (item.Kind != CompletionItemKind.Join) return item.DisplayText;
            return CanonicalJoinKey(item.DisplayText);
        }

        internal static string CanonicalJoinKey(string displayText) {
            try {
                var clauses = displayText.Split(new[] { " AND " }, StringSplitOptions.None);
                var canonical = new string[clauses.Length];
                for (int c = 0; c < clauses.Length; c++) {
                    int eqIdx = clauses[c].IndexOf(" = ", StringComparison.Ordinal);
                    if (eqIdx < 0) return displayText;
                    string left  = clauses[c].Substring(0, eqIdx).Trim();
                    string right = clauses[c].Substring(eqIdx + 3).Trim();
                    if (string.Compare(left, right, StringComparison.OrdinalIgnoreCase) > 0) {
                        string tmp = left; left = right; right = tmp;
                    }
                    canonical[c] = left + " = " + right;
                }
                Array.Sort(canonical, StringComparer.OrdinalIgnoreCase);
                return string.Join(" AND ", canonical);
            }
            catch { return displayText; }
        }

        private static IReadOnlyList<CompletionItem> SortByRank(IReadOnlyList<CompletionItem> items) {
            bool allSameRank = true;
            int firstRank = items.Count > 0 ? items[0].Rank : 0;
            int minRank = firstRank, maxRank = firstRank;
            foreach (var item in items) {
                if (item.Rank != firstRank) allSameRank = false;
                if (item.Rank < minRank) minRank = item.Rank;
                if (item.Rank > maxRank) maxRank = item.Rank;
            }
            if (allSameRank) return items;

            // Stable sort: preserve relative order within each rank group
            var result = new List<CompletionItem>(items.Count);
            for (int rank = minRank; rank <= maxRank; rank++)
                foreach (var item in items)
                    if (item.Rank == rank) result.Add(item);
            return result.AsReadOnly();
        }

        private static IReadOnlyList<CompletionItem> FilterByPrefix(
            IReadOnlyList<CompletionItem> items, string prefix) {
            if (string.IsNullOrEmpty(prefix)) return items;
            var filtered = new List<CompletionItem>();
            foreach (var item in items) {
                if (item.DisplayText.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    filtered.Add(item);
            }
            return filtered.AsReadOnly();
        }
    }
}
