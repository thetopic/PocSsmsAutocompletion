using Microsoft.SqlServer.Management.SqlParser.Parser;
using System;
using System.Collections.Generic;

namespace SsmsAutocompletion {

    internal sealed class CteColumnExtractor : ICteColumnExtractor {

        public IReadOnlyList<string> ExtractColumns(ParseResult parseResult, string cteName) {
            var tokenManager = parseResult?.Script?.TokenManager;
            if (tokenManager == null) return Array.Empty<string>();
            int count = tokenManager.Count;

            for (int i = 0; i < count; i++) {
                string text = (tokenManager.GetText(i) ?? "").Trim('[', ']');
                if (!string.Equals(text, cteName, StringComparison.OrdinalIgnoreCase)) continue;

                int afterNameIdx = NextSignificantIndex(tokenManager, i, count);
                if (afterNameIdx < 0) continue;
                string afterNameText = tokenManager.GetText(afterNameIdx) ?? "";

                if (afterNameText == "(") {
                    // Explicit column list: cte (col1, col2) AS (body)
                    var explicitCols = TryExtractExplicitColumns(tokenManager, afterNameIdx, count);
                    if (explicitCols != null) return explicitCols;
                    continue;
                }
                if (!string.Equals(afterNameText, "AS", StringComparison.OrdinalIgnoreCase)) continue;
                int bodyOpenIdx = NextSignificantIndex(tokenManager, afterNameIdx, count);
                if (bodyOpenIdx < 0 || tokenManager.GetText(bodyOpenIdx) != "(") continue;

                return ExtractColumnsFromBody(tokenManager, bodyOpenIdx, count);
            }

            return Array.Empty<string>();
        }

        private static IReadOnlyList<string> TryExtractExplicitColumns(
            TokenManager tokenManager, int openParen, int count) {
            var cols = new List<string>();
            int depth = 1;
            int closeIdx = -1;
            for (int i = openParen + 1; i < count && depth > 0; i++) {
                string t = tokenManager.GetText(i) ?? "";
                if (t == "(") { depth++; continue; }
                if (t == ")") {
                    depth--;
                    if (depth == 0) { closeIdx = i; break; }
                    continue;
                }
                if (depth != 1 || t == ",") continue;
                bool sig = false;
                try { sig = tokenManager.GetToken(i)?.IsSignificant == true; } catch { }
                if (!sig) continue;
                string col = t.Trim('[', ']');
                if (!string.IsNullOrEmpty(col)) cols.Add(col);
            }
            if (closeIdx < 0) return null;
            int asIdx = NextSignificantIndex(tokenManager, closeIdx, count);
            if (asIdx < 0 || !string.Equals(tokenManager.GetText(asIdx), "AS", StringComparison.OrdinalIgnoreCase))
                return null;
            int bodyOpenIdx = NextSignificantIndex(tokenManager, asIdx, count);
            if (bodyOpenIdx < 0 || tokenManager.GetText(bodyOpenIdx) != "(") return null;
            return cols.Count > 0 ? cols.AsReadOnly() : null;
        }

        private static IReadOnlyList<string> ExtractColumnsFromBody(
            TokenManager tokenManager, int bodyOpen, int count) {
            // Find SELECT at depth 1 inside the CTE body
            int depth = 1;
            int selectIdx = -1;
            for (int i = bodyOpen + 1; i < count && depth > 0; i++) {
                string t = tokenManager.GetText(i) ?? "";
                if (t == "(") { depth++; continue; }
                if (t == ")") { depth--; continue; }
                if (depth != 1) continue;
                if (string.Equals(t, "SELECT", StringComparison.OrdinalIgnoreCase)) { selectIdx = i; break; }
            }
            if (selectIdx < 0) return Array.Empty<string>();

            // Skip TOP [n] and DISTINCT after SELECT
            int pos = NextSignificantIndex(tokenManager, selectIdx, count);
            if (pos < 0) return Array.Empty<string>();
            if (string.Equals(tokenManager.GetText(pos), "TOP", StringComparison.OrdinalIgnoreCase)) {
                pos = NextSignificantIndex(tokenManager, pos, count); // the number/expression
                if (pos < 0) return Array.Empty<string>();
                pos = NextSignificantIndex(tokenManager, pos, count); // token after it
                if (pos < 0) return Array.Empty<string>();
            }
            if (string.Equals(tokenManager.GetText(pos), "DISTINCT", StringComparison.OrdinalIgnoreCase)) {
                pos = NextSignificantIndex(tokenManager, pos, count);
                if (pos < 0) return Array.Empty<string>();
            }

            // Collect column tokens; split by comma at depth == 1 (inside the CTE body)
            // depth == 1 means "directly inside the CTE body, not inside a nested paren"
            var chunks = new List<List<string>>();
            var current = new List<string>();
            depth = 1; // still inside the CTE body's outermost paren

            for (int i = pos; i < count; i++) {
                string t = tokenManager.GetText(i) ?? "";

                if (t == "(") {
                    depth++;
                } else if (t == ")") {
                    depth--;
                    if (depth == 0) {
                        if (current.Count > 0) chunks.Add(new List<string>(current));
                        break;
                    }
                } else if (depth == 1) {
                    if (string.Equals(t, "FROM", StringComparison.OrdinalIgnoreCase)) {
                        if (current.Count > 0) chunks.Add(new List<string>(current));
                        break;
                    }
                    if (t == ",") {
                        if (current.Count > 0) chunks.Add(new List<string>(current));
                        current = new List<string>();
                        continue;
                    }
                }

                bool sig = false;
                try { sig = tokenManager.GetToken(i)?.IsSignificant == true; } catch { }
                if (sig) current.Add(t);
            }
            if (current.Count > 0) chunks.Add(current);

            var columns = new List<string>();
            foreach (var chunk in chunks) {
                string col = ExtractColumnName(chunk);
                if (!string.IsNullOrEmpty(col)) columns.Add(col);
            }
            return columns.AsReadOnly();
        }

        private static string ExtractColumnName(List<string> tokens) {
            if (tokens.Count == 0) return null;
            string last = tokens[tokens.Count - 1].Trim('[', ']');
            if (last == "*") return null;
            if (last == ")") return null; // function without alias
            if (tokens.Count >= 2) {
                string prev = tokens[tokens.Count - 2];
                if (string.Equals(prev, "AS", StringComparison.OrdinalIgnoreCase)) return last;
                if (prev == ".") return last;
            }
            return last;
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
