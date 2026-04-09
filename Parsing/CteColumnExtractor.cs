using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SsmsAutocompletion {

    /// <summary>
    /// Extrait les colonnes exposées par une CTE à partir du SQL brut.
    ///
    /// Deux stratégies, dans l'ordre :
    ///   1. Colonne explicite  : WITH MaCte (col1, col2) AS (…)
    ///   2. Colonne implicite  : déduites du SELECT interne de la CTE
    ///      – alias AS nom     → nom
    ///      – table.colonne    → colonne
    ///      – colonne simple   → colonne
    ///      – fonction sans AS → ignorée (pas de nom déterministe)
    ///      – *                → ignoré
    /// </summary>
    internal sealed class CteColumnExtractor : ICteColumnExtractor {

        // "alias" dans  expr AS alias  (fin de l'expression)
        private static readonly Regex AsAliasRegex = new Regex(
            @"\bAS\s+(\[?\w+\]?)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // dernier mot dans  schema.table.colonne  ou  colonne
        private static readonly Regex LastWordRegex = new Regex(
            @"(\w+)\s*$",
            RegexOptions.Compiled);

        // SELECT [TOP n] [DISTINCT] …
        private static readonly Regex SelectPrefixRegex = new Regex(
            @"^\s*SELECT\s+(?:TOP\s+\d+\s+)?(?:DISTINCT\s+)?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

        // ── API publique ─────────────────────────────────────────────────────

        public IReadOnlyList<string> ExtractColumns(string sql, string cteName) {
            if (string.IsNullOrEmpty(sql) || string.IsNullOrEmpty(cteName))
                return Array.Empty<string>();

            // 1. Colonnes déclarées explicitement : WITH MaCte (col1, col2) AS (
            var explicit_ = TryExtractExplicitColumns(sql, cteName);
            if (explicit_ != null) return explicit_;

            // 2. Corps de la CTE → SELECT list
            string body = ExtractCteBody(sql, cteName);
            if (body == null) return Array.Empty<string>();

            return ParseSelectColumns(body);
        }

        // ── Colonnes explicites ──────────────────────────────────────────────

        private static IReadOnlyList<string> TryExtractExplicitColumns(string sql, string cteName) {
            // Forme :  cteName  ( col1, [col2], … )  AS  (
            var pattern = new Regex(
                $@"\b{Regex.Escape(cteName)}\b\s*\(([^()]+)\)\s*AS\s*\(",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var m = pattern.Match(sql);
            if (!m.Success) return null;

            var cols = new List<string>();
            foreach (string part in m.Groups[1].Value.Split(',')) {
                string col = part.Trim().Trim('[', ']');
                if (!string.IsNullOrEmpty(col)) cols.Add(col);
            }
            return cols.Count > 0 ? cols.AsReadOnly() : null;
        }

        // ── Extraction du corps de la CTE (parenthèses équilibrées) ─────────

        private static string ExtractCteBody(string sql, string cteName) {
            // Cherche  cteName  AS  (  (sans liste de colonnes explicite)
            var startPattern = new Regex(
                $@"\b{Regex.Escape(cteName)}\b\s+AS\s*\(",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var m = startPattern.Match(sql);
            if (!m.Success) return null;

            int openPos = m.Index + m.Length - 1; // position du '('
            int depth   = 1;
            for (int i = openPos + 1; i < sql.Length; i++) {
                char c = sql[i];
                if (c == '\'') {               // sauter les littéraux chaîne
                    i++;
                    while (i < sql.Length && sql[i] != '\'') i++;
                }
                else if (c == '(') depth++;
                else if (c == ')') {
                    depth--;
                    if (depth == 0) return sql.Substring(openPos + 1, i - openPos - 1);
                }
            }
            return null; // parenthèse non fermée (SQL incomplet en cours de frappe)
        }

        // ── Analyse du SELECT interne ────────────────────────────────────────

        private static IReadOnlyList<string> ParseSelectColumns(string body) {
            var selectMatch = SelectPrefixRegex.Match(body);
            if (!selectMatch.Success) return Array.Empty<string>();

            string afterSelect = body.Substring(selectMatch.Index + selectMatch.Length);

            // S'arrête au premier FROM non imbriqué
            int fromIdx = FindFromIndex(afterSelect);
            string selectList = fromIdx >= 0
                ? afterSelect.Substring(0, fromIdx)
                : afterSelect;

            var columns = new List<string>();
            foreach (string part in SplitRespectingParens(selectList)) {
                string col = ExtractColumnName(part.Trim());
                if (!string.IsNullOrEmpty(col)) columns.Add(col);
            }
            return columns.AsReadOnly();
        }

        // Trouve l'index du mot-clé FROM au niveau 0 (hors sous-requêtes)
        private static int FindFromIndex(string text) {
            int depth = 0;
            for (int i = 0; i < text.Length; i++) {
                char c = text[i];
                if (c == '(') { depth++; continue; }
                if (c == ')') { depth--; continue; }
                if (depth > 0) continue;
                if (i + 4 <= text.Length
                    && text.Substring(i, 4).Equals("FROM", StringComparison.OrdinalIgnoreCase)
                    && (i == 0            || !char.IsLetterOrDigit(text[i - 1]))
                    && (i + 4 >= text.Length || !char.IsLetterOrDigit(text[i + 4])))
                    return i;
            }
            return -1;
        }

        // Split par virgule en respectant les parenthèses imbriquées
        private static List<string> SplitRespectingParens(string text) {
            var parts = new List<string>();
            int depth = 0, start = 0;
            for (int i = 0; i < text.Length; i++) {
                if (text[i] == '(')      depth++;
                else if (text[i] == ')') depth--;
                else if (text[i] == ',' && depth == 0) {
                    parts.Add(text.Substring(start, i - start));
                    start = i + 1;
                }
            }
            parts.Add(text.Substring(start));
            return parts;
        }

        // Extrait le nom de colonne d'un item du SELECT
        private static string ExtractColumnName(string item) {
            if (string.IsNullOrWhiteSpace(item)) return null;
            string trimmed = item.Trim();

            // SELECT * ou table.*
            if (trimmed == "*" || trimmed.EndsWith(".*")) return null;

            // expr AS alias  →  alias
            var asMatch = AsAliasRegex.Match(trimmed);
            if (asMatch.Success)
                return asMatch.Groups[1].Value.Trim('[', ']');

            // Fonction sans alias : SUM(…), COUNT(…), CAST(…) → pas de nom fiable
            if (trimmed.EndsWith(")")) return null;

            // Dernier mot : schema.table.colonne → colonne  /  colonne → colonne
            var lastWord = LastWordRegex.Match(trimmed);
            return lastWord.Success ? lastWord.Value.Trim('[', ']') : null;
        }
    }
}
