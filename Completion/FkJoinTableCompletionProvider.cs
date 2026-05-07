using System;
using System.Collections.Generic;

namespace SsmsAutocompletion {

    /// <summary>
    /// After a JOIN keyword, suggests tables that have a foreign key relationship
    /// with any table already present in the query. Each suggestion includes the
    /// table name, a generated alias, and the full ON condition derived from the FK.
    /// These suggestions appear before the generic table list.
    /// Example: "Orders o JOIN " → suggests "Customers c ON c.CustomerID = o.CustomerID"
    /// </summary>
    internal sealed class FkJoinTableCompletionProvider : ICompletionProvider {
        private readonly IDatabaseMetadata _databaseMetadata;
        private readonly IAliasExtractor   _aliasExtractor;

        public FkJoinTableCompletionProvider(
            IDatabaseMetadata databaseMetadata,
            IAliasExtractor aliasExtractor) {
            _databaseMetadata = databaseMetadata;
            _aliasExtractor   = aliasExtractor;
        }

        public IReadOnlyList<CompletionItem> GetCompletions(CompletionRequest request) {
            if (request.IsDotContext)                                     return Array.Empty<CompletionItem>();
            if (!request.IsAfterJoinKeyword)                              return Array.Empty<CompletionItem>();
            if (request.ConnectionKey == null || request.ConnectionKey.IsEmpty) return Array.Empty<CompletionItem>();

            var aliasMap = _aliasExtractor.Extract(request.ParseResult);

            if (aliasMap.Count == 0) return Array.Empty<CompletionItem>();

            return BuildSuggestions(request.ConnectionKey, aliasMap);
        }

        private IReadOnlyList<CompletionItem> BuildSuggestions(
            ConnectionKey connectionKey,
            IReadOnlyDictionary<string, TableInfo> aliasMap) {
            var items          = new List<CompletionItem>();
            var existingAliases = new HashSet<string>(aliasMap.Keys, StringComparer.OrdinalIgnoreCase);
            var seen           = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in aliasMap) {
                string    existingAlias = kvp.Key;
                TableInfo existingTable = kvp.Value;

                var foreignKeys = _databaseMetadata.GetForeignKeys(
                    connectionKey, existingTable.Schema, existingTable.TableName);

                foreach (var fk in foreignKeys) {
                    bool existingIsOwner = Eq(fk.FkSchema, existingTable.Schema)
                                       && Eq(fk.FkTable,  existingTable.TableName);

                    bool existingIsReferenced = Eq(fk.ReferencedSchema, existingTable.Schema)
                                            && Eq(fk.ReferencedTable,  existingTable.TableName);

                    if (existingIsOwner) {
                        // Suggest the referenced (PK) table.
                        // ON: existingAlias.FkColumns = newAlias.ReferencedColumns
                        var newTable = new TableInfo(fk.ReferencedSchema, fk.ReferencedTable);
                        if (AlreadyInQuery(newTable, aliasMap)) continue;
                        string newAlias = AliasGenerator.Generate(fk.ReferencedTable, existingAliases);
                        string onClause = BuildOnClause(existingAlias, fk.FkColumns, newAlias, fk.ReferencedColumns);
                        AddItem(items, seen, newTable, newAlias, onClause, $"FK → {fk.ReferencedTable}");
                    }

                    if (existingIsReferenced) {
                        // Suggest the FK owner table.
                        // ON: newAlias.FkColumns = existingAlias.ReferencedColumns
                        var newTable = new TableInfo(fk.FkSchema, fk.FkTable);
                        if (AlreadyInQuery(newTable, aliasMap)) continue;
                        string newAlias = AliasGenerator.Generate(fk.FkTable, existingAliases);
                        string onClause = BuildOnClause(newAlias, fk.FkColumns, existingAlias, fk.ReferencedColumns);
                        AddItem(items, seen, newTable, newAlias, onClause, $"FK: {fk.FkTable} → {existingTable.TableName}");
                    }
                }
            }

            return items.AsReadOnly();
        }

        private static void AddItem(
            List<CompletionItem> items, HashSet<string> seen,
            TableInfo table, string alias, string onClause, string description) {
            string display = $"{table} {alias} ON {onClause}";
            if (seen.Add(display))
                items.Add(new CompletionItem(display, display, description, CompletionItemKind.Join));
        }

        private static string BuildOnClause(
            string aliasA, IReadOnlyList<string> colsA,
            string aliasB, IReadOnlyList<string> colsB) {
            var parts = new List<string>();
            for (int i = 0; i < Math.Min(colsA.Count, colsB.Count); i++)
                parts.Add($"{aliasA}.{colsA[i]} = {aliasB}.{colsB[i]}");
            return string.Join(" AND ", parts);
        }

        private static bool AlreadyInQuery(TableInfo table, IReadOnlyDictionary<string, TableInfo> aliasMap) {
            foreach (var v in aliasMap.Values) {
                if (Eq(v.Schema, table.Schema) && Eq(v.TableName, table.TableName))
                    return true;
            }
            return false;
        }

        private static bool Eq(string a, string b) =>
            string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
