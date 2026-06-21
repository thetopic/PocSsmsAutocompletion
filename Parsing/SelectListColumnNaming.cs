using Microsoft.SqlServer.Management.SqlParser.SqlCodeDom;
using System;
using System.Collections.Generic;

namespace SsmsAutocompletion {

    internal static class SelectListColumnNaming {

        public static IReadOnlyList<string> ExtractColumnNames(SqlQueryExpression queryExpression) {
            SqlQueryExpression queryExpr = queryExpression;
            while (queryExpr is SqlBinaryQueryExpression binary)
                queryExpr = binary.Left;

            var spec = queryExpr as SqlQuerySpecification;
            if (spec?.SelectClause?.SelectExpressions == null) return Array.Empty<string>();

            var columns = new List<string>();
            foreach (SqlSelectExpression selectExpr in spec.SelectClause.SelectExpressions) {
                if (!(selectExpr is SqlSelectScalarExpression scalar)) continue; // skip *

                string alias = scalar.Alias?.Value;
                if (!string.IsNullOrEmpty(alias)) { columns.Add(alias); continue; }

                if (scalar.Expression is SqlColumnRefExpression colRef) {
                    string col = colRef.ColumnName?.Value;
                    if (!string.IsNullOrEmpty(col)) columns.Add(col);
                }
            }
            return columns.AsReadOnly();
        }
    }
}
