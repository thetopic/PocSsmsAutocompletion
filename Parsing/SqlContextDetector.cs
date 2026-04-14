using Microsoft.SqlServer.Management.SqlParser.Parser;
using Microsoft.VisualStudio.Text;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SsmsAutocompletion {

    internal sealed class SqlContextDetector : IContextDetector {

        private static readonly Regex AliasContextRegex = new Regex(
            @"(?:FROM|JOIN)\s+((?:\[?\w+\]?\.)?(?:\[?\w+\]?))\s+$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);


        private static readonly HashSet<string> SqlKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "SELECT","FROM","WHERE","JOIN","INNER","LEFT","RIGHT","OUTER","CROSS","FULL",
            "ON","AS","AND","OR","NOT","IN","IS","NULL","LIKE","BETWEEN","ORDER","GROUP",
            "BY","HAVING","UNION","ALL","DISTINCT","TOP","INTO","VALUES","INSERT","UPDATE",
            "DELETE","SET","TABLE","WITH","EXISTS","CASE","WHEN","THEN","ELSE","END","ASC","DESC","LIMIT","OFFSET"
        };

        public (int line, int column) GetLineColumn(ITextSnapshot snapshot, int position) {
            var textLine = snapshot.GetLineFromPosition(Math.Min(position, snapshot.Length));
            return (textLine.LineNumber + 1, position - textLine.Start.Position + 1);
        }

        public bool IsDotContext(ITextSnapshot snapshot, int caretPosition) {
            int wordStart = FindWordStart(snapshot, caretPosition);
            return wordStart > 0 && wordStart <= snapshot.Length && snapshot[wordStart - 1] == '.';
        }

        public string GetQualifier(ITextSnapshot snapshot, int caretPosition) {
            int wordStart = FindWordStart(snapshot, caretPosition);
            if (wordStart <= 0) return null;
            int qualifierEnd   = wordStart - 1;
            int qualifierStart = qualifierEnd;
            while (qualifierStart > 0 && IsWordCharacter(snapshot[qualifierStart - 1]))
                qualifierStart--;
            if (qualifierStart >= qualifierEnd) return null;
            return snapshot.GetText(qualifierStart, qualifierEnd - qualifierStart);
        }

        public bool IsAfterKeyword(ParseResult parseResult, int line, int column, string keyword) {
            try {
                var tokenManager = parseResult?.Script?.TokenManager;
                if (tokenManager == null) return false;
                int tokenIndex = tokenManager.FindToken(line, column);
                if (tokenIndex < 0) return false;
                int previousIndex = tokenManager.GetPreviousSignificantTokenIndex(tokenIndex);
                if (previousIndex < 0) return false;
                string previousText = tokenManager.GetText(previousIndex)?.Trim();
                return string.Equals(previousText, keyword, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public bool IsInsideWhereClause(ParseResult parseResult, int line, int column) {
            try {
                var tokenManager = parseResult?.Script?.TokenManager;
                if (tokenManager == null) return false;
                int cursorToken = tokenManager.FindToken(line, column);
                int lastWhereIndex    = -1;
                int lastFromJoinIndex = -1;
                for (int i = 0; i < cursorToken && i < tokenManager.Count; i++) {
                    string text = tokenManager.GetText(i)?.ToUpperInvariant() ?? "";
                    if (text == "WHERE") lastWhereIndex    = i;
                    if (text == "FROM" || text == "JOIN") lastFromJoinIndex = i;
                }
                return lastWhereIndex > lastFromJoinIndex && lastWhereIndex >= 0;
            }
            catch { return false; }
        }

        public string GetCurrentWord(ITextSnapshot snapshot, int caretPosition) {
            if (caretPosition <= 0 || caretPosition > snapshot.Length) return "";
            int start = FindWordStart(snapshot, caretPosition);
            return snapshot.GetText(start, caretPosition - start);
        }

        public ITrackingSpan GetWordSpan(ITextSnapshot snapshot, int caretPosition) {
            int start  = Math.Min(caretPosition, snapshot.Length);
            start      = FindWordStart(snapshot, start);
            int length = Math.Min(caretPosition, snapshot.Length) - start;
            return snapshot.CreateTrackingSpan(
                new SnapshotSpan(snapshot, start, length), SpanTrackingMode.EdgeInclusive);
        }

        public string GetWordBefore(ITextSnapshot snapshot, int caretPosition) {
            int end = caretPosition - 1;
            while (end > 0 && snapshot[end - 1] == ' ') end--;
            if (end <= 0) return "";
            int start = end;
            while (start > 0 && IsWordCharacter(snapshot[start - 1])) start--;
            return snapshot.GetText(start, end - start);
        }

        public (bool isAliasContext, string tableNameBefore) DetectAliasContext(
            ParseResult parseResult, string sql, int line, int column, int caretPosition) {
            try {
                var tokenManager = parseResult?.Script?.TokenManager;
                if (tokenManager == null) return RegexDetectAliasContext(sql, caretPosition);
                int cursorToken = tokenManager.FindToken(line, column);
                if (cursorToken < 0) return RegexDetectAliasContext(sql, caretPosition);
                int prev1Index = tokenManager.GetPreviousSignificantTokenIndex(cursorToken);
                if (prev1Index < 0) return (false, null);
                string prev1Text = tokenManager.GetText(prev1Index)?.Trim() ?? "";
                int prev2Index = tokenManager.GetPreviousSignificantTokenIndex(prev1Index);
                if (prev2Index < 0) return (false, null);
                string prev2Upper = tokenManager.GetText(prev2Index)?.Trim().ToUpperInvariant() ?? "";
                bool isDirectlyAfterTable = (prev2Upper == "FROM" || prev2Upper == "JOIN")
                    && !string.IsNullOrEmpty(prev1Text)
                    && !SqlKeywords.Contains(prev1Text);
                if (isDirectlyAfterTable) return (true, prev1Text);
                return DetectAliasContextAfterAs(tokenManager, prev1Text, prev2Index);
            }
            catch { return (false, null); }
        }

        private static (bool, string) RegexDetectAliasContext(string sql, int caretPosition) {
            if (string.IsNullOrEmpty(sql)) return (false, null);
            string textUpToCaret = sql.Substring(0, Math.Min(caretPosition, sql.Length));
            var match = AliasContextRegex.Match(textUpToCaret);
            if (!match.Success) return (false, null);
            string tableName = match.Groups[1].Value.Trim('[', ']');
            if (string.IsNullOrEmpty(tableName) || SqlKeywords.Contains(tableName)) return (false, null);
            return (true, tableName);
        }

        private static (bool, string) DetectAliasContextAfterAs(
            TokenManager tokenManager, string prev1Text, int prev2Index) {
            if (!string.Equals(prev1Text, "AS", StringComparison.OrdinalIgnoreCase))
                return (false, null);
            int prev3Index = tokenManager.GetPreviousSignificantTokenIndex(prev2Index);
            if (prev3Index < 0) return (false, null);
            string prev3Upper = tokenManager.GetText(prev3Index)?.Trim().ToUpperInvariant() ?? "";
            if (prev3Upper != "FROM" && prev3Upper != "JOIN") return (false, null);
            string tableName = tokenManager.GetText(prev2Index)?.Trim() ?? "";
            if (string.IsNullOrEmpty(tableName)) return (false, null);
            return (true, tableName);
        }

        private static int FindWordStart(ITextSnapshot snapshot, int position) {
            int start = position;
            while (start > 0 && IsWordCharacter(snapshot[start - 1])) start--;
            return start;
        }

        private static bool IsWordCharacter(char character) =>
            char.IsLetterOrDigit(character) || character == '_';
    }
}
