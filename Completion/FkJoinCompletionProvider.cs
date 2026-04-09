using System;
using System.Collections.Generic;

namespace SsmsAutocompletion {

    internal sealed class FkJoinCompletionProvider : ICompletionProvider {
        private readonly IDatabaseMetadata _databaseMetadata;
        private readonly IAliasExtractor   _aliasExtractor;
        private readonly IContextDetector  _contextDetector;

        public FkJoinCompletionProvider(
            IDatabaseMetadata databaseMetadata,
            IAliasExtractor aliasExtractor,
            IContextDetector contextDetector) {
            _databaseMetadata = databaseMetadata;
            _aliasExtractor   = aliasExtractor;
            _contextDetector  = contextDetector;
        }

        public IReadOnlyList<CompletionItem> GetCompletions(CompletionRequest request) {
            if (request.IsDotContext) return Array.Empty<CompletionItem>();
            if (!request.IsJoinOnContext && !request.IsWhereContext) return Array.Empty<CompletionItem>();
            if (request.ConnectionKey == null || request.ConnectionKey.IsEmpty)
                return Array.Empty<CompletionItem>();
            var aliasMap = request.ParseResult != null
                ? _aliasExtractor.Extract(request.ParseResult)
                : _aliasExtractor.Extract(request.Sql);
            if (aliasMap.Count < 2) return Array.Empty<CompletionItem>();
            return BuildFkConditions(request.ConnectionKey, aliasMap);
        }

        private IReadOnlyList<CompletionItem> BuildFkConditions(
            ConnectionKey connectionKey,
            IReadOnlyDictionary<string, TableInfo> aliasMap) {
            var items     = new List<CompletionItem>();
            var aliasList = new List<KeyValuePair<string, TableInfo>>(aliasMap);
            for (int i = 0; i < aliasList.Count; i++) {
                for (int j = i + 1; j < aliasList.Count; j++) {
                    string aliasA = aliasList[i].Key; var tableA = aliasList[i].Value;
                    string aliasB = aliasList[j].Key; var tableB = aliasList[j].Value;
                    AddFkConditionsForPair(connectionKey, aliasA, tableA, aliasB, tableB, items);
                }
            }
            return items.AsReadOnly();
        }

        private void AddFkConditionsForPair(
            ConnectionKey connectionKey,
            string aliasA, TableInfo tableA, string aliasB, TableInfo tableB,
            List<CompletionItem> items) {
            var foreignKeys = _databaseMetadata.GetForeignKeys(connectionKey, tableA.Schema, tableA.TableName);
            foreach (var foreignKey in foreignKeys) {
                bool aReferencesB = string.Equals(foreignKey.ReferencedTable, tableB.TableName, StringComparison.OrdinalIgnoreCase)
                                 && string.Equals(foreignKey.ReferencedSchema, tableB.Schema,   StringComparison.OrdinalIgnoreCase);
                if (aReferencesB) {
                    string condition = BuildCondition(aliasA, foreignKey.FkColumns, aliasB, foreignKey.ReferencedColumns);
                    items.Add(new CompletionItem(condition, condition, "Relation FK/PK", CompletionItemKind.Join));
                    continue;
                }
                bool bReferencesA = string.Equals(foreignKey.FkTable, tableB.TableName, StringComparison.OrdinalIgnoreCase)
                                 && string.Equals(foreignKey.FkSchema, tableB.Schema,   StringComparison.OrdinalIgnoreCase);
                if (bReferencesA) {
                    string condition = BuildCondition(aliasB, foreignKey.FkColumns, aliasA, foreignKey.ReferencedColumns);
                    items.Add(new CompletionItem(condition, condition, "Relation FK/PK", CompletionItemKind.Join));
                }
            }
        }

        private static string BuildCondition(
            string fkAlias, IReadOnlyList<string> fkColumns,
            string pkAlias, IReadOnlyList<string> pkColumns) {
            var parts = new List<string>();
            for (int k = 0; k < Math.Min(fkColumns.Count, pkColumns.Count); k++)
                parts.Add($"{fkAlias}.{fkColumns[k]} = {pkAlias}.{pkColumns[k]}");
            return string.Join(" AND ", parts);
        }
    }
}
