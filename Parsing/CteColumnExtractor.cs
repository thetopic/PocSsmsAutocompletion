using Microsoft.SqlServer.Management.SqlParser.Parser;
using Microsoft.SqlServer.Management.SqlParser.SqlCodeDom;
using System;
using System.Collections.Generic;

namespace SsmsAutocompletion {

    internal sealed class CteColumnExtractor : ICteColumnExtractor {

        public IReadOnlyList<string> ExtractColumns(ParseResult parseResult, string cteName) {
            var script = parseResult?.Script;
            if (script == null) return Array.Empty<string>();
            try {
                foreach (SqlBatch batch in script.Batches)
                    foreach (SqlStatement stmt in batch.Statements) {
                        var withClause = (stmt as SqlSelectStatement)?.QueryWithClause;
                        if (withClause == null) continue;
                        foreach (SqlCommonTableExpression cte in withClause.CommonTableExpressions) {
                            if (!string.Equals(cte.Name?.Value, cteName, StringComparison.OrdinalIgnoreCase))
                                continue;
                            return ExtractFromCte(cte);
                        }
                    }
            }
            catch { }
            return Array.Empty<string>();
        }

        private static IReadOnlyList<string> ExtractFromCte(SqlCommonTableExpression cte) {
            // Explicit column list: WITH cte (col1, col2) AS (...)
            if (cte.ColumnList?.Count > 0) {
                var explicit_ = new List<string>();
                foreach (SqlIdentifier id in cte.ColumnList)
                    if (!string.IsNullOrEmpty(id.Value)) explicit_.Add(id.Value);
                if (explicit_.Count > 0) return explicit_.AsReadOnly();
            }

            // Derive from SELECT clause; for UNION/INTERSECT/EXCEPT use the first branch
            SqlQueryExpression queryExpr = cte.QueryExpression;
            while (queryExpr is SqlBinaryQueryExpression binary)
                queryExpr = binary.Left;

            var spec = queryExpr as SqlQuerySpecification;
            if (spec?.SelectClause?.SelectExpressions == null) return Array.Empty<string>();

            var columns = new List<string>();
            foreach (SqlSelectExpression selectExpr in spec.SelectClause.SelectExpressions) {
                if (!(selectExpr is SqlSelectScalarExpression scalar)) continue; // skip *

                // Explicit alias wins
                string alias = scalar.Alias?.Value;
                if (!string.IsNullOrEmpty(alias)) { columns.Add(alias); continue; }

                // Plain column reference: [schema.]table.column or column
                if (scalar.Expression is SqlColumnRefExpression colRef) {
                    string col = colRef.ColumnName?.Value;
                    if (!string.IsNullOrEmpty(col)) columns.Add(col);
                }
                // Expression without alias → no deterministic name, skip
            }
            return columns.AsReadOnly();
        }
    }
}
