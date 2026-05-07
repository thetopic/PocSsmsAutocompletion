using Microsoft.SqlServer.Management.SqlParser.Parser;
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
            try { PopulateFromTokenManager(parseResult, map); }
            catch { }
            return map;
        }

        private void PopulateFromTokenManager(ParseResult parseResult, Dictionary<string, TableInfo> map) {
            var tokenManager = parseResult.Script?.TokenManager;
            if (tokenManager == null) return;
            int count = tokenManager.Count;
            int index = 0;
            while (index < count) {
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
