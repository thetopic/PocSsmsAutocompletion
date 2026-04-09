using System;
using System.Collections.Generic;

namespace SsmsAutocompletion {

    internal sealed class AliasCompletionProvider : ICompletionProvider {
        private readonly IAliasExtractor  _aliasExtractor;
        private readonly ISqlParser       _sqlParser;

        public AliasCompletionProvider(IAliasExtractor aliasExtractor, ISqlParser sqlParser) {
            _aliasExtractor = aliasExtractor;
            _sqlParser      = sqlParser;
        }

        public IReadOnlyList<CompletionItem> GetCompletions(CompletionRequest request) {
            if (request.IsDotContext) return Array.Empty<CompletionItem>();
            if (!request.IsAfterTableInFromJoin) return Array.Empty<CompletionItem>();
            if (string.IsNullOrEmpty(request.TableNameBeforeCursor)) return Array.Empty<CompletionItem>();
            var parseResult    = request.ParseResult ?? _sqlParser.Parse(request.Sql);
            var existingAliases = new HashSet<string>(
                _aliasExtractor.Extract(parseResult).Keys, StringComparer.OrdinalIgnoreCase);
            string suggestedAlias = AliasGenerator.Generate(request.TableNameBeforeCursor, existingAliases);
            var item = new CompletionItem(
                suggestedAlias,
                suggestedAlias + " ",
                $"Alias suggéré pour {request.TableNameBeforeCursor}",
                CompletionItemKind.Alias);
            return new[] { item };
        }
    }
}
