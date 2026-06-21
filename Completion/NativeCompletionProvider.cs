using Microsoft.SqlServer.Management.SqlParser.Intellisense;
using Microsoft.SqlServer.Management.SqlParser.MetadataProvider;
using System;
using System.Collections.Generic;

namespace SsmsAutocompletion {

    internal sealed class NativeCompletionProvider : ICompletionProvider {

        public IReadOnlyList<CompletionItem> GetCompletions(CompletionRequest request) {
            if (request.ParseResult == null) return Array.Empty<CompletionItem>();
            if (request.IsAfterFromKeyword) return Array.Empty<CompletionItem>();
            var items = new List<CompletionItem>();
            try {
                var displayProvider  = request.MetadataProvider as IMetadataDisplayInfoProvider;
                var declarations     = Resolver.FindCompletions(
                    request.ParseResult, request.Line, request.Column, displayProvider);
                if (declarations == null) return Array.Empty<CompletionItem>();
                foreach (var declaration in declarations) {
                    if (string.IsNullOrEmpty(declaration.Title)) continue;
                    string insertText = declaration.Title;
                    if (declaration.Type == DeclarationType.Table || declaration.Type == DeclarationType.View)
                        insertText += " ";
                    items.Add(new CompletionItem(declaration.Title, insertText, declaration.Type.ToString(),
                        CompletionItemKind.Keyword, rank: 3));
                }
            }
            catch { }
            return items.AsReadOnly();
        }
    }
}
