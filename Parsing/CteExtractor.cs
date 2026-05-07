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
    }
}
