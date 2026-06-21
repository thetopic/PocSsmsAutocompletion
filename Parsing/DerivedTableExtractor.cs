using Microsoft.SqlServer.Management.SqlParser.Parser;
using Microsoft.SqlServer.Management.SqlParser.SqlCodeDom;
using System;
using System.Collections.Generic;

namespace SsmsAutocompletion {

    internal sealed class DerivedTableExtractor : IDerivedTableExtractor {

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
            var queryExpr = (stmt as SqlSelectStatement)?.SelectSpecification?.QueryExpression;
            WalkQueryExpression(queryExpr, map);

            var withClause = (stmt as SqlSelectStatement)?.QueryWithClause;
            if (withClause != null)
                foreach (SqlCommonTableExpression cte in withClause.CommonTableExpressions)
                    WalkQueryExpression(cte.QueryExpression, map);
        }

        private static void WalkQueryExpression(SqlQueryExpression queryExpr, Dictionary<string, IReadOnlyList<string>> map) {
            if (queryExpr is SqlBinaryQueryExpression binary) {
                WalkQueryExpression(binary.Left, map);
                WalkQueryExpression(binary.Right, map);
                return;
            }
            if (!(queryExpr is SqlQuerySpecification spec)) return;
            var tableExpressions = spec.FromClause?.TableExpressions;
            if (tableExpressions == null) return;
            foreach (SqlTableExpression tableExpr in tableExpressions)
                WalkTableExpression(tableExpr, map);
        }

        private static void WalkTableExpression(SqlTableExpression tableExpr, Dictionary<string, IReadOnlyList<string>> map) {
            switch (tableExpr) {
                case SqlQualifiedJoinTableExpression join:
                    WalkTableExpression(join.Left, map);
                    WalkTableExpression(join.Right, map);
                    break;
                case SqlDerivedTableExpression derived: {
                    string alias = derived.Alias?.Value;
                    if (!string.IsNullOrEmpty(alias))
                        map[alias] = SelectListColumnNaming.ExtractColumnNames(derived.QueryExpression);
                    // A derived table may itself contain nested derived tables.
                    WalkQueryExpression(derived.QueryExpression, map);
                    break;
                }
            }
        }
    }
}
