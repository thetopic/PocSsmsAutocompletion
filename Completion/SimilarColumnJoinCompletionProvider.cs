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

            var aliasMap = _aliasExtractor.Extract(request.ParseResult);

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
            var columnsA   = _databaseMetadata.GetColumns(connectionKey, tableA.Schema, tableA.TableName);
            var columnsB   = _databaseMetadata.GetColumns(connectionKey, tableB.Schema, tableB.TableName);
            var fkCovered  = BuildFkCoveredPairs(connectionKey, tableA, tableB);

            foreach (var columnA in columnsA) {
                foreach (var columnB in columnsB) {
                    if (!AreSimilar(columnA.ColumnName, columnB.ColumnName)) continue;
                    if (fkCovered.Contains(MakePairKey(columnA.ColumnName, columnB.ColumnName))) continue;
                    string condition = $"{aliasA}.{columnA.ColumnName} = {aliasB}.{columnB.ColumnName}";
                    items.Add(new CompletionItem(condition, condition, "Colonnes similaires", CompletionItemKind.Join, rank: 1));
                }
            }
        }

        private HashSet<string> BuildFkCoveredPairs(
            ConnectionKey connectionKey, TableInfo tableA, TableInfo tableB) {
            var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // GetForeignKeys returns all FKs involving the table (both directions)
            var fks = _databaseMetadata.GetForeignKeys(connectionKey, tableA.Schema, tableA.TableName);
            foreach (var fk in fks) {
                bool aIsFk = string.Equals(fk.FkTable, tableA.TableName, StringComparison.OrdinalIgnoreCase)
                          && string.Equals(fk.FkSchema, tableA.Schema, StringComparison.OrdinalIgnoreCase);
                bool bIsFk = string.Equals(fk.FkTable, tableB.TableName, StringComparison.OrdinalIgnoreCase)
                          && string.Equals(fk.FkSchema, tableB.Schema, StringComparison.OrdinalIgnoreCase);
                bool aIsRef = string.Equals(fk.ReferencedTable, tableA.TableName, StringComparison.OrdinalIgnoreCase)
                           && string.Equals(fk.ReferencedSchema, tableA.Schema, StringComparison.OrdinalIgnoreCase);
                bool bIsRef = string.Equals(fk.ReferencedTable, tableB.TableName, StringComparison.OrdinalIgnoreCase)
                           && string.Equals(fk.ReferencedSchema, tableB.Schema, StringComparison.OrdinalIgnoreCase);

                // A→B: tableA has FK referencing tableB
                if (aIsFk && bIsRef)
                    for (int i = 0; i < Math.Min(fk.FkColumns.Count, fk.ReferencedColumns.Count); i++)
                        covered.Add(MakePairKey(fk.FkColumns[i], fk.ReferencedColumns[i]));

                // B→A: tableB has FK referencing tableA
                if (bIsFk && aIsRef)
                    for (int i = 0; i < Math.Min(fk.FkColumns.Count, fk.ReferencedColumns.Count); i++)
                        covered.Add(MakePairKey(fk.ReferencedColumns[i], fk.FkColumns[i]));
            }
            return covered;
        }

        private static string MakePairKey(string colA, string colB) =>
            colA.ToLowerInvariant() + "|" + colB.ToLowerInvariant();

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
