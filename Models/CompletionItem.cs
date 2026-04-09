namespace SsmsAutocompletion {

    internal enum CompletionItemKind {
        Keyword,
        Table,
        Column,
        Join,
        Alias
    }

    internal sealed class CompletionItem {
        public string            DisplayText { get; }
        public string            InsertText  { get; }
        public string            Description { get; }
        public CompletionItemKind Kind       { get; }

        public CompletionItem(string displayText, string insertText, string description, CompletionItemKind kind = CompletionItemKind.Keyword) {
            DisplayText = displayText;
            InsertText  = insertText;
            Description = description;
            Kind        = kind;
        }
    }
}
