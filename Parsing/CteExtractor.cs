using Microsoft.SqlServer.Management.SqlParser.Parser;
using Microsoft.SqlServer.Management.SqlParser.SqlCodeDom;
using System;
using System.Collections.Generic;

namespace SsmsAutocompletion {

    internal sealed class CteExtractor : ICteExtractor {

        public IReadOnlyList<string> Extract(ParseResult parseResult) {
            var script = parseResult?.Script;
            if (script == null) return Array.Empty<string>();

            var names = new List<string>();
            var seen  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try {
                foreach (SqlBatch batch in script.Batches)
                    foreach (SqlStatement stmt in batch.Statements) {
                        var withClause = (stmt as SqlSelectStatement)?.QueryWithClause;
                        if (withClause == null) continue;
                        foreach (SqlCommonTableExpression cte in withClause.CommonTableExpressions) {
                            string name = cte.Name?.Value;
                            if (!string.IsNullOrEmpty(name) && seen.Add(name))
                                names.Add(name);
                        }
                    }
            }
            catch { }

            return names.AsReadOnly();
        }

        public bool IsRecursive(ParseResult parseResult, string cteName) {
            var script = parseResult?.Script;
            if (script == null || string.IsNullOrEmpty(cteName)) return false;
            try {
                foreach (SqlBatch batch in script.Batches)
                    foreach (SqlStatement stmt in batch.Statements) {
                        var withClause = (stmt as SqlSelectStatement)?.QueryWithClause;
                        if (withClause == null) continue;
                        foreach (SqlCommonTableExpression cte in withClause.CommonTableExpressions) {
                            if (!string.Equals(cte.Name?.Value, cteName, StringComparison.OrdinalIgnoreCase))
                                continue;
                            return ReferencesSelf(cte.QueryExpression, cteName);
                        }
                    }
            }
            catch { }
            return false;
        }

        private static bool ReferencesSelf(SqlQueryExpression queryExpr, string cteName) {
            if (queryExpr is SqlBinaryQueryExpression binary)
                return ReferencesSelf(binary.Left, cteName) || ReferencesSelf(binary.Right, cteName);
            if (!(queryExpr is SqlQuerySpecification spec)) return false;
            var tableExpressions = spec.FromClause?.TableExpressions;
            if (tableExpressions == null) return false;
            foreach (SqlTableExpression te in tableExpressions)
                if (TableExpressionReferencesName(te, cteName)) return true;
            return false;
        }

        private static bool TableExpressionReferencesName(SqlTableExpression tableExpr, string name) {
            switch (tableExpr) {
                case SqlQualifiedJoinTableExpression join:
                    return TableExpressionReferencesName(join.Left, name) || TableExpressionReferencesName(join.Right, name);
                case SqlTableRefExpression tableRef:
                    string objectName = tableRef.ObjectIdentifier?.ObjectName?.Value;
                    return string.Equals(objectName, name, StringComparison.OrdinalIgnoreCase);
                case SqlDerivedTableExpression derived:
                    return ReferencesSelf(derived.QueryExpression, name);
                default:
                    return false;
            }
        }
    }
}
