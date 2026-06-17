using Microsoft.SqlServer.Management.SqlParser.Parser;
using Microsoft.VisualStudio.Text;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SsmsAutocompletion {

    internal sealed class SqlContextDetector : IContextDetector {

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
            ParseResult parseResult, int line, int column) {
            try {
                var tokenManager = parseResult?.Script?.TokenManager;
                if (tokenManager == null) return (false, null);
                int cursorToken = tokenManager.FindToken(line, column);
                if (cursorToken < 0) return (false, null);
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

        public bool IsAfterExecKeyword(ParseResult parseResult, int line, int column) =>
            IsAfterKeyword(parseResult, line, column, "EXEC") ||
            IsAfterKeyword(parseResult, line, column, "EXECUTE");

        public (bool isInside, string procedureName, IReadOnlyList<string> alreadyProvided)
            GetProcedureCallContext(ITextSnapshot snapshot, int caretPosition) {
            if (caretPosition <= 0)
                return (false, null, Array.Empty<string>());
            string text = snapshot.GetText(0, Math.Min(caretPosition, snapshot.Length));
            int execPos = FindLastExecInText(text);
            if (execPos < 0) return (false, null, Array.Empty<string>());

            bool isExecute = execPos + 7 <= text.Length
                && string.Compare(text, execPos, "EXECUTE", 0, 7, StringComparison.OrdinalIgnoreCase) == 0
                && (execPos + 7 >= text.Length || !IsWordCharacter(text[execPos + 7]));
            int pos = execPos + (isExecute ? 7 : 4);

            while (pos < text.Length && (text[pos] == ' ' || text[pos] == '\t' || text[pos] == '\r' || text[pos] == '\n'))
                pos++;
            if (pos >= text.Length) return (false, null, Array.Empty<string>());

            int nameStart = pos;
            while (pos < text.Length && (IsWordCharacter(text[pos]) || text[pos] == '.')) pos++;
            if (pos == nameStart) return (false, null, Array.Empty<string>());

            string procName = text.Substring(nameStart, pos - nameStart);
            var alreadyProvided = ExtractAlreadyProvidedParams(text.Substring(pos));
            return (true, procName, new ReadOnlyCollection<string>(alreadyProvided));
        }

        public (bool isInside, string targetTable) DetectInsertContext(ParseResult parseResult, int line, int column) {
            try {
                var tokenManager = parseResult?.Script?.TokenManager;
                if (tokenManager == null) return (false, null);
                int cursorIndex = tokenManager.FindToken(line, column);
                if (cursorIndex < 0) return (false, null);

                // State: 0=init 1=sawINSERT 2=sawINTO 3=sawTableFirst 4=sawDot 5=sawTableFull 6=insideColumnList
                int    state        = 0;
                string schemaPart   = null;
                string tableFirst   = null;
                string bestTable    = null;
                int    parenDepth   = 0;

                for (int i = 0; i < cursorIndex; i++) {
                    var tok = tokenManager.GetToken(i);
                    if (tok == null || !tok.IsSignificant) continue;
                    string text  = tokenManager.GetText(i)?.Trim() ?? "";
                    string upper = text.ToUpperInvariant();

                    switch (state) {
                        case 0:
                            if (upper == "INSERT") { state = 1; schemaPart = null; tableFirst = null; bestTable = null; }
                            break;
                        case 1:
                            if (upper == "INTO") state = 2;
                            else if (upper == "INSERT") { schemaPart = null; tableFirst = null; bestTable = null; }
                            else state = 0;
                            break;
                        case 2:
                            if (!string.IsNullOrEmpty(text)) { tableFirst = text; state = 3; }
                            break;
                        case 3:
                            if (text == ".") { schemaPart = tableFirst; tableFirst = null; state = 4; }
                            else if (text == "(") { bestTable = tableFirst; parenDepth = 1; state = 6; }
                            else state = 0;
                            break;
                        case 4:
                            if (!string.IsNullOrEmpty(text)) { tableFirst = text; state = 5; }
                            break;
                        case 5:
                            if (text == "(") { bestTable = schemaPart + "." + tableFirst; parenDepth = 1; state = 6; }
                            else state = 0;
                            break;
                        case 6:
                            if (text == "(") parenDepth++;
                            else if (text == ")") { parenDepth--; if (parenDepth == 0) { state = 0; bestTable = null; } }
                            break;
                    }
                }
                if (state == 6) return (true, bestTable);
            }
            catch { }
            return (false, null);
        }

        public (bool isInside, string targetTable) DetectUpdateSetContext(ParseResult parseResult, int line, int column) {
            try {
                var tokenManager = parseResult?.Script?.TokenManager;
                if (tokenManager == null) return (false, null);
                int cursorIndex = tokenManager.FindToken(line, column);
                if (cursorIndex < 0) return (false, null);

                // State: 0=init 1=sawUPDATE 2=sawTable1 3=sawDot 4=sawTable2 5=inSET 6=pastWHERE
                int    state      = 0;
                string schemaPart = null;
                string tableFirst = null;
                string tableAfterUpdate = null;

                for (int i = 0; i < cursorIndex; i++) {
                    var tok = tokenManager.GetToken(i);
                    if (tok == null || !tok.IsSignificant) continue;
                    string text  = tokenManager.GetText(i)?.Trim() ?? "";
                    string upper = text.ToUpperInvariant();

                    switch (state) {
                        case 0:
                            if (upper == "UPDATE") { state = 1; schemaPart = null; tableFirst = null; tableAfterUpdate = null; }
                            break;
                        case 1:
                            if (upper == "UPDATE") { schemaPart = null; tableFirst = null; tableAfterUpdate = null; }
                            else if (!string.IsNullOrEmpty(text)) { tableFirst = text; state = 2; }
                            break;
                        case 2:
                            if (text == ".") { schemaPart = tableFirst; tableFirst = null; state = 3; }
                            else if (upper == "SET") { tableAfterUpdate = tableFirst; state = 5; }
                            else { /* alias or other tokens - keep state */ }
                            break;
                        case 3:
                            if (!string.IsNullOrEmpty(text)) { tableFirst = text; state = 4; }
                            break;
                        case 4:
                            if (upper == "SET") { tableAfterUpdate = schemaPart + "." + tableFirst; state = 5; }
                            else { /* alias, skip */ }
                            break;
                        case 5:
                            if (upper == "WHERE" || upper == "GROUP" || upper == "ORDER" || upper == "HAVING")
                                state = 6;
                            else if (upper == "UPDATE") { state = 1; schemaPart = null; tableFirst = null; tableAfterUpdate = null; }
                            break;
                        case 6:
                            if (upper == "UPDATE") { state = 1; schemaPart = null; tableFirst = null; tableAfterUpdate = null; }
                            break;
                    }
                }
                if (state == 5 && tableAfterUpdate != null) return (true, tableAfterUpdate);
            }
            catch { }
            return (false, null);
        }

        public string GetNearestClauseKeyword(ITextSnapshot snapshot, int caretPosition) {
            if (caretPosition <= 0) return null;
            var clauseKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                "SELECT", "FROM", "WHERE", "SET", "GROUP", "ORDER", "HAVING", "JOIN",
                "ON", "INTO", "VALUES", "INSERT", "UPDATE", "DELETE", "UNION"
            };
            int depth = 0;
            int pos   = Math.Min(caretPosition, snapshot.Length) - 1;
            while (pos >= 0) {
                char c = snapshot[pos];
                if (c == ')') { depth++; pos--; continue; }
                if (c == '(') { if (depth > 0) depth--; pos--; continue; }
                if (depth == 0 && (char.IsLetter(c) || c == '_')) {
                    int wordEnd = pos;
                    while (pos > 0 && IsWordCharacter(snapshot[pos - 1])) pos--;
                    int wordStart = pos;
                    string word = snapshot.GetText(wordStart, wordEnd - wordStart + 1);
                    bool afterOk = wordEnd + 1 >= snapshot.Length || !IsWordCharacter(snapshot[wordEnd + 1]);
                    if (afterOk && clauseKeywords.Contains(word))
                        return word.ToUpperInvariant();
                    pos--;
                    continue;
                }
                pos--;
            }
            return null;
        }

        private static int FindLastExecInText(string text) {
            int lastPos = -1;
            for (int i = 0; i <= text.Length - 4; i++) {
                if (string.Compare(text, i, "EXEC", 0, 4, StringComparison.OrdinalIgnoreCase) != 0) continue;
                bool prevOk = i == 0 || !IsWordCharacter(text[i - 1]);
                bool isExe  = i + 7 <= text.Length
                    && string.Compare(text, i, "EXECUTE", 0, 7, StringComparison.OrdinalIgnoreCase) == 0;
                int  wordEnd = i + (isExe ? 7 : 4);
                bool nextOk = wordEnd >= text.Length || !IsWordCharacter(text[wordEnd]);
                if (prevOk && nextOk) lastPos = i;
            }
            return lastPos;
        }

        private static List<string> ExtractAlreadyProvidedParams(string textAfterProc) {
            var result = new List<string>();
            int i = 0;
            while (i < textAfterProc.Length) {
                if (textAfterProc[i] != '@') { i++; continue; }
                int start = i++;
                while (i < textAfterProc.Length && IsWordCharacter(textAfterProc[i])) i++;
                string paramName = textAfterProc.Substring(start, i - start);
                int j = i;
                while (j < textAfterProc.Length && (textAfterProc[j] == ' ' || textAfterProc[j] == '\t')) j++;
                if (j < textAfterProc.Length && textAfterProc[j] == '=')
                    result.Add(paramName.ToLowerInvariant());
            }
            return result;
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
