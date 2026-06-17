namespace SsmsAutocompletion {

    internal enum CompletionItemKind {
        Keyword,
        Table,
        View,
        StoredProcedure,
        UserDefinedFunction,
        Column,
        Join,
        Alias,
        Cte,
        Function,
        Parameter,
        Schema
    }

    internal sealed class CompletionItem {
        public string             DisplayText { get; }
        public string             InsertText  { get; }
        public string             Description { get; }
        public CompletionItemKind Kind        { get; }
        public int                Rank        { get; }

        public CompletionItem(string displayText, string insertText, string description,
            CompletionItemKind kind = CompletionItemKind.Keyword, int rank = 0) {
            DisplayText = displayText;
            InsertText  = insertText;
            Description = description;
            Kind        = kind;
            Rank        = rank;
        }
    }
}
