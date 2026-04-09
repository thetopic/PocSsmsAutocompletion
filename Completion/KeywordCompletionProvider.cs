using System;
using System.Collections.Generic;

namespace SsmsAutocompletion {

    internal sealed class KeywordCompletionProvider : ICompletionProvider {

        private static readonly IReadOnlyList<string> Keywords = new List<string> {
            "SELECT","FROM","WHERE","JOIN","INNER","LEFT","RIGHT","OUTER","CROSS","FULL",
            "ON","AS","AND","OR","NOT","IN","IS","NULL","LIKE","BETWEEN","ORDER","GROUP",
            "BY","HAVING","UNION","ALL","DISTINCT","TOP","INTO","VALUES","INSERT","UPDATE",
            "DELETE","SET","TABLE","WITH","EXISTS","CASE","WHEN","THEN","ELSE","END"
        }.AsReadOnly();

        public IReadOnlyList<CompletionItem> GetCompletions(CompletionRequest request) {
            if (request.IsDotContext) return Array.Empty<CompletionItem>();
            var items = new List<CompletionItem>(Keywords.Count);
            foreach (string keyword in Keywords)
                items.Add(new CompletionItem(keyword, keyword + " ", "Keyword", CompletionItemKind.Keyword));
            return items.AsReadOnly();
        }
    }
}
