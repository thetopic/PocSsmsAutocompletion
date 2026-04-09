using System;
using System.Collections.Generic;

namespace SsmsAutocompletion {

    /// <summary>
    /// Suggests JOIN/WHERE conditions based on columns with identical or similar names
    /// across the tables referenced in the query (complement to FK-based suggestions).
    /// </summary>
    internal sealed class SimilarColumnJoinCompletionProvider : ICompletionProvider {
        private readonly IDatabaseMetadata _databaseMetadata;
        private readonly IAliasExtractor   _aliasExtractor;

        public SimilarColumnJoinCompletionProvider(
            IDatabaseMetadata databaseMetadata,
            IAliasExtractor aliasExtractor) {
            _databaseMetadata = databaseMetadata;
            _aliasExtractor   = aliasExtractor;
        }

        public IReadOnlyList<CompletionItem> GetCompletions(CompletionRequest request) {
            if (request.IsDotContext)                                  return Array.Empty<CompletionItem>();
            if (!request.IsJoinOnContext && !request.IsWhereContext)   return Array.Empty<CompletionItem>();
            if (request.ConnectionKey == null || request.ConnectionKey.IsEmpty) return Array.Empty<CompletionItem>();

            var aliasMap = request.ParseResult != null
                ? _aliasExtractor.Extract(request.ParseResult)
                : _aliasExtractor.Extract(request.Sql);

            if (aliasMap.Count < 2) return Array.Empty<CompletionItem>();

            return BuildConditions(request.ConnectionKey, aliasMap);
        }

        private IReadOnlyList<CompletionItem> BuildConditions(
            ConnectionKey connectionKey,
            IReadOnlyDictionary<string, TableInfo> aliasMap) {
            var items     = new List<CompletionItem>();
            var aliasList = new List<KeyValuePair<string, TableInfo>>(aliasMap);
            for (int i = 0; i < aliasList.Count; i++) {
                for (int j = i + 1; j < aliasList.Count; j++) {
                    AddConditionsForPair(
                        connectionKey,
                        aliasList[i].Key, aliasList[i].Value,
                        aliasList[j].Key, aliasList[j].Value,
                        items);
                }
            }
            return items.AsReadOnly();
        }

        private void AddConditionsForPair(
            ConnectionKey connectionKey,
            string aliasA, TableInfo tableA,
            string aliasB, TableInfo tableB,
            List<CompletionItem> items) {
            var columnsA = _databaseMetadata.GetColumns(connectionKey, tableA.Schema, tableA.TableName);
            var columnsB = _databaseMetadata.GetColumns(connectionKey, tableB.Schema, tableB.TableName);

            foreach (var columnA in columnsA) {
                foreach (var columnB in columnsB) {
                    if (!AreSimilar(columnA.ColumnName, columnB.ColumnName)) continue;
                    string condition = $"{aliasA}.{columnA.ColumnName} = {aliasB}.{columnB.ColumnName}";
                    items.Add(new CompletionItem(condition, condition, "Colonnes similaires", CompletionItemKind.Join));
                }
            }
        }

        /// <summary>
        /// Two column names are considered similar when:
        ///   1. They are identical (case-insensitive)
        ///   2. One is a meaningful suffix of the other (e.g. CustomerID / ID)
        /// </summary>
        private static bool AreSimilar(string columnA, string columnB) {
            if (string.Equals(columnA, columnB, StringComparison.OrdinalIgnoreCase))
                return true;

            // Suffix match: avoid single-char suffixes (too generic)
            const int minimumSuffixLength = 2;
            if (columnA.Length >= minimumSuffixLength
                && columnB.EndsWith(columnA, StringComparison.OrdinalIgnoreCase))
                return true;

            if (columnB.Length >= minimumSuffixLength
                && columnA.EndsWith(columnB, StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }
    }
}
