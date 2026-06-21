using System;
using System.Collections.Generic;

namespace SsmsAutocompletion {

    /// <summary>
    /// Immediately after a table name in a FROM or JOIN clause, suggests complete
    /// JOIN expressions for tables related via foreign keys — without waiting for
    /// the user to type the JOIN keyword.
    ///
    /// Example: "FROM Orders " →  suggests  "JOIN Customers c ON c.CustomerId = orders.CustomerId"
    /// </summary>
    internal sealed class InlineJoinCompletionProvider : ICompletionProvider {
        private readonly IDatabaseMetadata _databaseMetadata;
        private readonly IAliasExtractor   _aliasExtractor;

        public InlineJoinCompletionProvider(
            IDatabaseMetadata databaseMetadata,
            IAliasExtractor   aliasExtractor) {
            _databaseMetadata = databaseMetadata;
            _aliasExtractor   = aliasExtractor;
        }

        public IReadOnlyList<CompletionItem> GetCompletions(CompletionRequest request) {
            if (request.IsDotContext)                                          return Array.Empty<CompletionItem>();
            if (!request.IsAfterTableInFromJoin)                               return Array.Empty<CompletionItem>();
            if (string.IsNullOrEmpty(request.TableNameBeforeCursor))           return Array.Empty<CompletionItem>();
            if (request.ConnectionKey == null || request.ConnectionKey.IsEmpty) return Array.Empty<CompletionItem>();

            var aliasMap     = _aliasExtractor.Extract(request.ParseResult);
            string currentTableName = request.TableNameBeforeCursor;

            // Locate the TableInfo and its alias for the table the cursor is on.
            string    existingAlias  = null;
            TableInfo currentTable   = null;
            foreach (var kvp in aliasMap) {
                if (!string.Equals(kvp.Value.TableName, currentTableName, StringComparison.OrdinalIgnoreCase))
                    continue;
                existingAlias = kvp.Key;
                currentTable  = kvp.Value;
                break;
            }
            if (currentTable == null) return Array.Empty<CompletionItem>();

            var foreignKeys = _databaseMetadata.GetForeignKeys(
                request.ConnectionKey, currentTable.Schema, currentTable.TableName);
            if (foreignKeys == null || foreignKeys.Count == 0) return Array.Empty<CompletionItem>();

            return BuildSuggestions(foreignKeys, aliasMap, existingAlias, currentTable);
        }

        private static IReadOnlyList<CompletionItem> BuildSuggestions(
            IReadOnlyList<ForeignKeyInfo>          foreignKeys,
            IReadOnlyDictionary<string, TableInfo> aliasMap,
            string                                 existingAlias,
            TableInfo                              currentTable) {
            var items           = new List<CompletionItem>();
            var existingAliases = new HashSet<string>(aliasMap.Keys, StringComparer.OrdinalIgnoreCase);
            var seen            = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var fk in foreignKeys) {
                bool isOwner      = Eq(fk.FkSchema, currentTable.Schema)
                                 && Eq(fk.FkTable,  currentTable.TableName);
                bool isReferenced = Eq(fk.ReferencedSchema, currentTable.Schema)
                                 && Eq(fk.ReferencedTable,  currentTable.TableName);

                if (isOwner) {
                    // currentTable owns the FK → related table is the PK (referenced) side
                    // ON: existingAlias.FkColumns = newAlias.ReferencedColumns
                    var relatedTable = new TableInfo(fk.ReferencedSchema, fk.ReferencedTable);
                    if (AlreadyInQuery(relatedTable, aliasMap)) continue;
                    string newAlias  = AliasGenerator.Generate(fk.ReferencedTable, existingAliases);
                    string onClause  = BuildOnClause(existingAlias, fk.FkColumns, newAlias, fk.ReferencedColumns);
                    AddItem(items, seen, relatedTable, newAlias, onClause,
                        $"FK → {fk.ReferencedTable}");
                }

                if (isReferenced) {
                    // currentTable is the PK side → related table is the FK owner
                    // ON: newAlias.FkColumns = existingAlias.ReferencedColumns
                    var relatedTable = new TableInfo(fk.FkSchema, fk.FkTable);
                    if (AlreadyInQuery(relatedTable, aliasMap)) continue;
                    string newAlias  = AliasGenerator.Generate(fk.FkTable, existingAliases);
                    string onClause  = BuildOnClause(newAlias, fk.FkColumns, existingAlias, fk.ReferencedColumns);
                    AddItem(items, seen, relatedTable, newAlias, onClause,
                        $"FK: {fk.FkTable} → {currentTable.TableName}");
                }
            }

            return items.AsReadOnly();
        }

        private static void AddItem(
            List<CompletionItem> items, HashSet<string> seen,
            TableInfo table, string alias, string onClause, string description) {
            string display = $"JOIN {table} {alias} ON {onClause}";
            if (seen.Add(display))
                items.Add(new CompletionItem(display, display + " ", description, CompletionItemKind.Join, rank: 1));
        }

        private static string BuildOnClause(
            string aliasA, IReadOnlyList<string> colsA,
            string aliasB, IReadOnlyList<string> colsB) {
            var parts = new List<string>();
            for (int i = 0; i < Math.Min(colsA.Count, colsB.Count); i++)
                parts.Add($"{aliasA}.{colsA[i]} = {aliasB}.{colsB[i]}");
            return string.Join(" AND ", parts);
        }

        private static bool AlreadyInQuery(
            TableInfo table, IReadOnlyDictionary<string, TableInfo> aliasMap) {
            foreach (var v in aliasMap.Values)
                if (Eq(v.Schema, table.Schema) && Eq(v.TableName, table.TableName))
                    return true;
            return false;
        }

        private static bool Eq(string a, string b) =>
            string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
