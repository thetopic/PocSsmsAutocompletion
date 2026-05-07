using Microsoft.SqlServer.Management.SqlParser.Parser;
using System;
using System.Collections.Generic;

namespace SsmsAutocompletion {

    internal sealed class CteExtractor : ICteExtractor {

        public IReadOnlyList<string> Extract(ParseResult parseResult) {
            var tokenManager = parseResult?.Script?.TokenManager;
            if (tokenManager == null) return Array.Empty<string>();

            var names = new List<string>();
            var seen  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int count = tokenManager.Count;

            for (int i = 0; i < count; i++) {
                if (!string.Equals(tokenManager.GetText(i), "WITH", StringComparison.OrdinalIgnoreCase))
                    continue;
                // Walk the CTE chain: WITH name AS (...), name AS (...), ...
                int pos = i;
                while (pos >= 0) {
                    int nameIdx = NextSignificantIndex(tokenManager, pos, count);
                    if (nameIdx < 0) break;
                    string name = (tokenManager.GetText(nameIdx) ?? "").Trim('[', ']');
                    if (string.IsNullOrEmpty(name)) break;

                    int afterNameIdx = NextSignificantIndex(tokenManager, nameIdx, count);
                    if (afterNameIdx < 0) break;
                    string afterNameText = tokenManager.GetText(afterNameIdx) ?? "";

                    int bodyOpenIdx;
                    if (afterNameText == "(") {
                        // Explicit column list: name (col, col) AS (body)
                        int closeIdx = FindMatchingClose(tokenManager, afterNameIdx, count);
                        if (closeIdx < 0) break;
                        int asIdx = NextSignificantIndex(tokenManager, closeIdx, count);
                        if (asIdx < 0 || !string.Equals(tokenManager.GetText(asIdx), "AS", StringComparison.OrdinalIgnoreCase)) break;
                        int openIdx = NextSignificantIndex(tokenManager, asIdx, count);
                        if (openIdx < 0 || tokenManager.GetText(openIdx) != "(") break;
                        bodyOpenIdx = openIdx;
                    } else if (string.Equals(afterNameText, "AS", StringComparison.OrdinalIgnoreCase)) {
                        int openIdx = NextSignificantIndex(tokenManager, afterNameIdx, count);
                        if (openIdx < 0 || tokenManager.GetText(openIdx) != "(") break;
                        bodyOpenIdx = openIdx;
                    } else {
                        break;
                    }

                    if (seen.Add(name)) names.Add(name);

                    int bodyCloseIdx = FindMatchingClose(tokenManager, bodyOpenIdx, count);
                    if (bodyCloseIdx < 0) break; // Body not closed yet (still being typed)

                    int afterBodyIdx = NextSignificantIndex(tokenManager, bodyCloseIdx, count);
                    if (afterBodyIdx >= 0 && tokenManager.GetText(afterBodyIdx) == ",")
                        pos = afterBodyIdx; // Continue to next CTE
                    else
                        break;
                }
            }

            return names.AsReadOnly();
        }

        private static int FindMatchingClose(TokenManager tokenManager, int openIdx, int count) {
            int depth = 1;
            for (int i = openIdx + 1; i < count; i++) {
                string t = tokenManager.GetText(i);
                if (t == "(") depth++;
                else if (t == ")") { depth--; if (depth == 0) return i; }
            }
            return -1;
        }

        private static int NextSignificantIndex(TokenManager tokenManager, int startIndex, int count) {
            for (int i = startIndex + 1; i < count; i++) {
                try { if (tokenManager.GetToken(i)?.IsSignificant == true) return i; }
                catch { }
            }
            return -1;
        }
    }
}
