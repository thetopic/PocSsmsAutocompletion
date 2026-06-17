using System;
using System.Collections.Generic;

namespace SsmsAutocompletion {

    internal sealed class UpdateSetCompletionProvider : ICompletionProvider {
        private readonly IDatabaseMetadata _databaseMetadata;
        private readonly IAliasExtractor   _aliasExtractor;

        public UpdateSetCompletionProvider(
            IDatabaseMetadata databaseMetadata, IAliasExtractor aliasExtractor) {
            _databaseMetadata = databaseMetadata;
            _aliasExtractor   = aliasExtractor;
        }

        public IReadOnlyList<CompletionItem> GetCompletions(CompletionRequest request) {
            if (!request.IsUpdateSetClause)                                        return Array.Empty<CompletionItem>();
            if (string.IsNullOrEmpty(request.InsertUpdateTargetTable))             return Array.Empty<CompletionItem>();
            if (request.ConnectionKey == null || request.ConnectionKey.IsEmpty)    return Array.Empty<CompletionItem>();

            // Resolve alias to actual table if needed
            ParseTableName(request.InsertUpdateTargetTable, out string schema, out string tableName);
            var aliasMap = _aliasExtractor.Extract(request.ParseResult);
            if (aliasMap.TryGetValue(tableName.ToLowerInvariant(), out TableInfo resolved)) {
                schema    = resolved.Schema;
                tableName = resolved.TableName;
            }

            var columns = _databaseMetadata.GetColumns(request.ConnectionKey, schema, tableName);
            if (columns.Count == 0) return Array.Empty<CompletionItem>();

            var alreadyAssigned = ExtractAlreadyAssignedColumns(request.Sql, request.CaretPosition);

            var items = new List<CompletionItem>(columns.Count);
            foreach (var col in columns) {
                if (alreadyAssigned.Contains(col.ColumnName.ToLowerInvariant())) continue;
                string insertText = col.ColumnName + " = ";
                items.Add(new CompletionItem(col.ColumnName, insertText, col.DataType, CompletionItemKind.Column));
            }
            return items.AsReadOnly();
        }

        private static void ParseTableName(string fullName, out string schema, out string tableName) {
            int dot = fullName.IndexOf('.');
            if (dot >= 0) {
                schema    = fullName.Substring(0, dot);
                tableName = fullName.Substring(dot + 1);
            } else {
                schema    = "dbo";
                tableName = fullName;
            }
        }

        private static HashSet<string> ExtractAlreadyAssignedColumns(string sql, int caretPosition) {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string textBefore = sql.Substring(0, Math.Min(caretPosition, sql.Length));

            // Find last SET keyword
            int setPos = -1;
            int i      = 0;
            while (i <= textBefore.Length - 3) {
                if (string.Compare(textBefore, i, "SET", 0, 3, StringComparison.OrdinalIgnoreCase) == 0) {
                    bool prevOk = i == 0 || !char.IsLetterOrDigit(textBefore[i - 1]);
                    bool nextOk = i + 3 >= textBefore.Length || !char.IsLetterOrDigit(textBefore[i + 3]);
                    if (prevOk && nextOk) setPos = i;
                }
                i++;
            }
            if (setPos < 0) return result;

            string setClause = textBefore.Substring(setPos + 3);
            // Extract "colName = " patterns from the SET clause
            foreach (var assignment in setClause.Split(',')) {
                int eqIdx = assignment.IndexOf('=');
                if (eqIdx <= 0) continue;
                string colPart = assignment.Substring(0, eqIdx).Trim().Trim('[', ']');
                // Handle alias.colName
                int dot = colPart.IndexOf('.');
                if (dot >= 0) colPart = colPart.Substring(dot + 1).Trim();
                if (!string.IsNullOrEmpty(colPart))
                    result.Add(colPart.ToLowerInvariant());
            }
            return result;
        }
    }
}
