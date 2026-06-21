using System;
using System.Collections.Generic;
using System.Linq;

namespace SsmsAutocompletion {

    internal sealed class InsertColumnCompletionProvider : ICompletionProvider {
        private readonly IDatabaseMetadata _databaseMetadata;

        public InsertColumnCompletionProvider(IDatabaseMetadata databaseMetadata) {
            _databaseMetadata = databaseMetadata;
        }

        public IReadOnlyList<CompletionItem> GetCompletions(CompletionRequest request) {
            if (!request.IsInsertColumnList)                                       return Array.Empty<CompletionItem>();
            if (string.IsNullOrEmpty(request.InsertUpdateTargetTable))             return Array.Empty<CompletionItem>();
            if (request.ConnectionKey == null || request.ConnectionKey.IsEmpty)    return Array.Empty<CompletionItem>();

            ParseTableName(request.InsertUpdateTargetTable, out string schema, out string tableName);
            var columns = _databaseMetadata.GetColumns(request.ConnectionKey, schema, tableName);
            if (columns.Count == 0) return Array.Empty<CompletionItem>();

            var alreadyListed = ExtractAlreadyListedColumns(request.Sql, request.CaretPosition);

            var items = new List<CompletionItem>(columns.Count);
            foreach (var col in columns) {
                if (alreadyListed.Contains(col.ColumnName.ToLowerInvariant())) continue;
                items.Add(new CompletionItem(col.ColumnName, col.ColumnName, col.DataType, CompletionItemKind.Column));
            }

            if (alreadyListed.Count == 0)
                items.Add(BuildFullTemplateItem(columns));

            return items.AsReadOnly();
        }

        private static CompletionItem BuildFullTemplateItem(IReadOnlyList<ColumnInfo> columns) {
            string columnList = string.Join(", ", columns.Select(c => c.ColumnName));
            string valuesList = string.Join(", ", columns.Select(c => "@" + c.ColumnName));
            string insertText = columnList + ") VALUES (" + valuesList + ")";
            return new CompletionItem("* (toutes les colonnes)", insertText, "Modèle INSERT complet", CompletionItemKind.Column);
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

        private static HashSet<string> ExtractAlreadyListedColumns(string sql, int caretPosition) {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Find the opening '(' of the column list before the cursor
            int openParen = -1;
            for (int i = Math.Min(caretPosition - 1, sql.Length - 1); i >= 0; i--) {
                if (sql[i] == '(') { openParen = i; break; }
                if (sql[i] == ')') break; // not inside a paren
            }
            if (openParen < 0) return result;
            string columnListText = sql.Substring(openParen + 1, Math.Min(caretPosition, sql.Length) - openParen - 1);
            foreach (var part in columnListText.Split(',')) {
                string col = part.Trim().Trim('[', ']');
                if (!string.IsNullOrEmpty(col))
                    result.Add(col.ToLowerInvariant());
            }
            return result;
        }
    }
}
