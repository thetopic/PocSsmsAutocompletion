using Microsoft.SqlServer.Management.SqlParser.Parser;
using Microsoft.SqlServer.Management.SqlParser.SqlCodeDom;
using System;
using System.Collections.Generic;

namespace SsmsAutocompletion {

    internal sealed class SelectListAliasExtractor : ISelectListAliasExtractor {

        public IReadOnlyList<string> Extract(ParseResult parseResult, int line, int column) {
            var script = parseResult?.Script;
            if (script == null) return Array.Empty<string>();
            try {
                // The statement may not lexically span trailing keywords the user is still typing
                // (e.g. "ORDER BY " past the parsed end location), so pick the last statement that
                // starts at or before the cursor rather than requiring strict end-containment.
                SqlSelectStatement matchedStmt = null;
                foreach (SqlBatch batch in script.Batches)
                    foreach (SqlStatement stmt in batch.Statements) {
                        if (!(stmt is SqlSelectStatement select)) continue;
                        var start = select.StartLocation;
                        if (start == null) continue;
                        if (ComparePosition(line, column, start.LineNumber, start.ColumnNumber) < 0) continue;
                        matchedStmt = select;
                    }
                if (matchedStmt == null) return Array.Empty<string>();

                var mainSpec = AnchorSpec(matchedStmt.SelectSpecification?.QueryExpression);
                if (mainSpec == null) return Array.Empty<string>();

                var nested = FindNestedSpec(mainSpec, line, column);
                return ExtractAliases(nested ?? mainSpec);
            }
            catch { }
            return Array.Empty<string>();
        }

        private static SqlQuerySpecification AnchorSpec(SqlQueryExpression queryExpr) {
            while (queryExpr is SqlBinaryQueryExpression binary) queryExpr = binary.Left;
            return queryExpr as SqlQuerySpecification;
        }

        private static SqlQuerySpecification FindNestedSpec(SqlQuerySpecification spec, int line, int column) {
            var tableExpressions = spec.FromClause?.TableExpressions;
            if (tableExpressions == null) return null;
            foreach (SqlTableExpression te in tableExpressions) {
                var match = FindInTableExpression(te, line, column);
                if (match != null) return match;
            }
            return null;
        }

        private static SqlQuerySpecification FindInTableExpression(SqlTableExpression tableExpr, int line, int column) {
            switch (tableExpr) {
                case SqlQualifiedJoinTableExpression join:
                    return FindInTableExpression(join.Left, line, column)
                        ?? FindInTableExpression(join.Right, line, column);
                case SqlDerivedTableExpression derived:
                    if (!Contains(derived.StartLocation, derived.EndLocation, line, column)) return null;
                    var innerSpec = AnchorSpec(derived.QueryExpression);
                    if (innerSpec == null) return null;
                    return FindNestedSpec(innerSpec, line, column) ?? innerSpec;
                default:
                    return null;
            }
        }

        private static bool Contains(Location start, Location end, int line, int column) {
            if (start == null || end == null) return false;
            return ComparePosition(line, column, start.LineNumber, start.ColumnNumber) >= 0
                && ComparePosition(line, column, end.LineNumber, end.ColumnNumber) <= 0;
        }

        private static int ComparePosition(int line, int column, int refLine, int refColumn) {
            if (line != refLine) return line < refLine ? -1 : 1;
            if (column != refColumn) return column < refColumn ? -1 : 1;
            return 0;
        }

        private static IReadOnlyList<string> ExtractAliases(SqlQuerySpecification spec) {
            var aliases = new List<string>();
            var selectExpressions = spec.SelectClause?.SelectExpressions;
            if (selectExpressions == null) return aliases.AsReadOnly();
            foreach (SqlSelectExpression selectExpr in selectExpressions) {
                if (!(selectExpr is SqlSelectScalarExpression scalar)) continue;
                string alias = scalar.Alias?.Value;
                if (!string.IsNullOrEmpty(alias)) aliases.Add(alias);
            }
            return aliases.AsReadOnly();
        }
    }
}
