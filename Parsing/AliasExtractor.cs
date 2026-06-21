using Microsoft.SqlServer.Management.SqlParser.Parser;
using Microsoft.SqlServer.Management.SqlParser.SqlCodeDom;
using System;
using System.Collections.Generic;

namespace SsmsAutocompletion {

    internal sealed class AliasExtractor : IAliasExtractor {

        private static readonly HashSet<string> SqlKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "SELECT","FROM","WHERE","JOIN","INNER","LEFT","RIGHT","OUTER","CROSS","FULL",
            "ON","AS","AND","OR","NOT","IN","IS","NULL","LIKE","BETWEEN","ORDER","GROUP",
            "BY","HAVING","UNION","ALL","DISTINCT","TOP","INTO","VALUES","INSERT","UPDATE",
            "DELETE","SET","TABLE","WITH","EXISTS","CASE","WHEN","THEN","ELSE","END","ASC","DESC","LIMIT","OFFSET"
        };

        public IReadOnlyDictionary<string, TableInfo> Extract(ParseResult parseResult) {
            if (parseResult == null) return new Dictionary<string, TableInfo>();
            var map = new Dictionary<string, TableInfo>(StringComparer.OrdinalIgnoreCase);
            try {
                var tokenManager = parseResult.Script?.TokenManager;
                if (tokenManager != null) PopulateFromTokenManagerRange(tokenManager, 0, tokenManager.Count - 1, map);
            }
            catch { }
            return map;
        }

        public IReadOnlyDictionary<string, TableInfo> ExtractInScope(ParseResult parseResult, int line, int column) {
            if (parseResult == null) return new Dictionary<string, TableInfo>();
            try {
                var tokenManager = parseResult.Script?.TokenManager;
                if (tokenManager == null) return Extract(parseResult);

                var range = FindScopeRange(parseResult, line, column);
                if (range == null) return Extract(parseResult);

                int startIndex  = Math.Max(0, tokenManager.FindToken(range.Value.start.LineNumber, range.Value.start.ColumnNumber));
                int cursorIndex = tokenManager.FindToken(line, column);
                int endIndex    = Math.Max(tokenManager.FindToken(range.Value.end.LineNumber, range.Value.end.ColumnNumber), cursorIndex);

                var map = new Dictionary<string, TableInfo>(StringComparer.OrdinalIgnoreCase);
                PopulateFromTokenManagerRange(tokenManager, startIndex, endIndex, map);
                return map;
            }
            catch { return Extract(parseResult); }
        }

        private static (Location start, Location end)? FindScopeRange(ParseResult parseResult, int line, int column) {
            var script = parseResult.Script;
            if (script == null) return null;
            SqlSelectStatement matchedStmt = null;
            foreach (SqlBatch batch in script.Batches)
                foreach (SqlStatement stmt in batch.Statements) {
                    if (!(stmt is SqlSelectStatement select)) continue;
                    var start = select.StartLocation;
                    if (start == null) continue;
                    if (ComparePosition(line, column, start.LineNumber, start.ColumnNumber) < 0) continue;
                    matchedStmt = select;
                }
            if (matchedStmt == null) return null;

            var queryExpr = matchedStmt.SelectSpecification?.QueryExpression;
            if (queryExpr == null) return null;
            return FindBranchRange(queryExpr, line, column) ?? (queryExpr.StartLocation, queryExpr.EndLocation);
        }

        private static (Location start, Location end)? FindBranchRange(SqlQueryExpression queryExpr, int line, int column) {
            if (!(queryExpr is SqlBinaryQueryExpression binary)) return (queryExpr.StartLocation, queryExpr.EndLocation);

            if (Contains(binary.Left.StartLocation, binary.Left.EndLocation, line, column))
                return FindBranchRange(binary.Left, line, column) ?? (binary.Left.StartLocation, binary.Left.EndLocation);

            // Right is the most recently typed branch — its EndLocation may not yet cover
            // trailing clauses still being typed (e.g. "... UNION SELECT ... WHERE "),
            // so only require the cursor to be at or after its start; the caller widens the
            // end bound to the cursor position regardless of what we return here.
            if (ComparePosition(line, column, binary.Right.StartLocation.LineNumber, binary.Right.StartLocation.ColumnNumber) >= 0)
                return FindBranchRange(binary.Right, line, column) ?? (binary.Right.StartLocation, binary.Right.EndLocation);

            return null; // cursor outside both branches (e.g. trailing ORDER BY) — caller falls back to whole statement
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

        private void PopulateFromTokenManagerRange(
            TokenManager tokenManager, int startIndex, int endIndex, Dictionary<string, TableInfo> map) {
            int index = Math.Max(0, startIndex);
            int boundedCount = Math.Min(tokenManager.Count, endIndex + 1);
            while (index < boundedCount) {
                string tokenText = tokenManager.GetText(index) ?? "";
                bool isFromOrJoin = string.Equals(tokenText, "FROM", StringComparison.OrdinalIgnoreCase)
                                 || string.Equals(tokenText, "JOIN",  StringComparison.OrdinalIgnoreCase);
                if (!isFromOrJoin) { index++; continue; }
                index = ExtractOneAlias(tokenManager, index, map);
            }
        }

        private int ExtractOneAlias(
            TokenManager tokenManager, int keywordIndex,
            Dictionary<string, TableInfo> map) {
            int nextIndex = NextSignificantIndex(tokenManager, keywordIndex);
            if (nextIndex < 0) return tokenManager.Count;
            string schema = "dbo", tableName;
            string firstToken = tokenManager.GetText(nextIndex) ?? "";
            int afterFirstIndex = NextSignificantIndex(tokenManager, nextIndex);
            if (afterFirstIndex >= 0 && tokenManager.GetText(afterFirstIndex) == ".") {
                schema = firstToken.Trim('[', ']');
                int tableIndex = NextSignificantIndex(tokenManager, afterFirstIndex);
                if (tableIndex < 0) return afterFirstIndex + 1;
                tableName  = (tokenManager.GetText(tableIndex) ?? "").Trim('[', ']');
                nextIndex  = tableIndex;
            } else {
                tableName = firstToken.Trim('[', ']');
            }
            if (string.IsNullOrEmpty(tableName)) return nextIndex + 1;
            int afterTableIndex = NextSignificantIndex(tokenManager, nextIndex);
            if (afterTableIndex >= 0 && string.Equals(tokenManager.GetText(afterTableIndex), "AS", StringComparison.OrdinalIgnoreCase))
                afterTableIndex = NextSignificantIndex(tokenManager, afterTableIndex);
            string alias;
            if (afterTableIndex >= 0 && !SqlKeywords.Contains(tokenManager.GetText(afterTableIndex) ?? "")) {
                alias = (tokenManager.GetText(afterTableIndex) ?? "").Trim('[', ']');
                nextIndex = afterTableIndex;
            } else {
                alias = tableName;
            }
            if (!string.IsNullOrEmpty(alias))
                map[alias.ToLowerInvariant()] = new TableInfo(schema, tableName);
            return nextIndex + 1;
        }

        private static int NextSignificantIndex(TokenManager tokenManager, int startIndex) {
            for (int i = startIndex + 1; i < tokenManager.Count; i++) {
                try { if (tokenManager.GetToken(i)?.IsSignificant == true) return i; }
                catch { }
            }
            return -1;
        }
    }
}
