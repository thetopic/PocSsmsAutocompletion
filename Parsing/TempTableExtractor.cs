using Microsoft.SqlServer.Management.SqlParser.Parser;
using Microsoft.SqlServer.Management.SqlParser.SqlCodeDom;
using System;
using System.Collections.Generic;

namespace SsmsAutocompletion {

    internal sealed class TempTableExtractor : ITempTableExtractor {

        public IReadOnlyDictionary<string, IReadOnlyList<string>> Extract(ParseResult parseResult) {
            var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            var script = parseResult?.Script;
            if (script == null) return map;
            try {
                foreach (SqlBatch batch in script.Batches)
                    foreach (SqlStatement stmt in batch.Statements)
                        WalkStatement(stmt, map);
            }
            catch { }
            return map;
        }

        private static void WalkStatement(SqlStatement stmt, Dictionary<string, IReadOnlyList<string>> map) {
            if (stmt is SqlCreateTableStatement create) {
                string name = create.Name?.ToString();
                if (!IsLocalTableName(name)) return;
                map[name] = ExtractColumnDefinitionNames(create.Definition);
                return;
            }

            if (stmt is SqlInlineTableVariableDeclareStatement declare) {
                string name = declare.Declaration?.Name;
                if (!IsLocalTableName(name)) return;
                map[name] = ExtractColumnDefinitionNames(declare.Declaration?.Definition);
                return;
            }

            if (stmt is SqlSelectStatement select) {
                var queryExpr = select.SelectSpecification?.QueryExpression;
                while (queryExpr is SqlBinaryQueryExpression binary) queryExpr = binary.Left;
                if (!(queryExpr is SqlQuerySpecification spec)) return;
                string targetName = spec.IntoClause?.IntoTarget?.ToString();
                if (!IsLocalTableName(targetName)) return;
                map[targetName] = SelectListColumnNaming.ExtractColumnNames(select.SelectSpecification.QueryExpression);
            }
        }

        private static IReadOnlyList<string> ExtractColumnDefinitionNames(SqlTableDefinition definition) {
            var columns = new List<string>();
            var columnDefinitions = definition?.ColumnDefinitions;
            if (columnDefinitions != null)
                foreach (SqlColumnDefinition col in columnDefinitions) {
                    string colName = col.Name?.Value;
                    if (!string.IsNullOrEmpty(colName)) columns.Add(colName);
                }
            return columns.AsReadOnly();
        }

        private static bool IsLocalTableName(string name) =>
            !string.IsNullOrEmpty(name) && (name.StartsWith("#", StringComparison.Ordinal) || name.StartsWith("@", StringComparison.Ordinal));
    }
}
