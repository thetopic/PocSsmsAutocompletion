using System;
using System.Collections.Generic;

namespace SsmsAutocompletion {

    internal sealed class SchemaCompletionProvider : ICompletionProvider {
        private readonly IDatabaseMetadata _databaseMetadata;
        private readonly IAliasExtractor   _aliasExtractor;
        private readonly ICteExtractor     _cteExtractor;

        public SchemaCompletionProvider(
            IDatabaseMetadata databaseMetadata,
            IAliasExtractor aliasExtractor,
            ICteExtractor cteExtractor) {
            _databaseMetadata = databaseMetadata;
            _aliasExtractor   = aliasExtractor;
            _cteExtractor     = cteExtractor;
        }

        public IReadOnlyList<CompletionItem> GetCompletions(CompletionRequest request) {
            if (!request.IsDotContext)                                           return Array.Empty<CompletionItem>();
            if (string.IsNullOrEmpty(request.Qualifier))                         return Array.Empty<CompletionItem>();
            if (request.ConnectionKey == null || request.ConnectionKey.IsEmpty)  return Array.Empty<CompletionItem>();

            // Skip if qualifier is a known alias, table, view, or CTE
            if (IsKnownObject(request)) return Array.Empty<CompletionItem>();

            var schemas = _databaseMetadata.GetSchemas(request.ConnectionKey);
            if (schemas.Count == 0) return Array.Empty<CompletionItem>();

            var items = new List<CompletionItem>(schemas.Count);
            foreach (var schema in schemas)
                items.Add(new CompletionItem(schema, schema, "Schema", CompletionItemKind.Schema, rank: 1));
            return items.AsReadOnly();
        }

        private bool IsKnownObject(CompletionRequest request) {
            string qualifier = request.Qualifier;

            // Check aliases from the current query
            var aliasMap = _aliasExtractor.Extract(request.ParseResult);
            if (aliasMap.ContainsKey(qualifier.ToLowerInvariant())) return true;

            // Check CTEs
            var cteNames = _cteExtractor.Extract(request.ParseResult);
            foreach (var cte in cteNames)
                if (string.Equals(cte, qualifier, StringComparison.OrdinalIgnoreCase)) return true;

            // Check tables and views
            var tables = _databaseMetadata.GetTables(request.ConnectionKey);
            foreach (var table in tables)
                if (string.Equals(table.TableName, qualifier, StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }
    }
}
