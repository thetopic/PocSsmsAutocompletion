using Microsoft.VisualStudio.Text;

namespace SsmsAutocompletion {

    internal sealed class CompletionRequestBuilder {
        private readonly ISqlParser       _sqlParser;
        private readonly IContextDetector _contextDetector;
        private readonly IDatabaseMetadata _databaseMetadata;

        public CompletionRequestBuilder(
            ISqlParser sqlParser,
            IContextDetector contextDetector,
            IDatabaseMetadata databaseMetadata) {
            _sqlParser        = sqlParser;
            _contextDetector  = contextDetector;
            _databaseMetadata = databaseMetadata;
        }

        public CompletionRequest Build(
            ITextSnapshot snapshot, string sql, int caretPosition, ConnectionKey connectionKey) {
            var (line, column) = _contextDetector.GetLineColumn(snapshot, caretPosition);
            var parseResult      = _sqlParser.Parse(sql);
            var metadataProvider = _databaseMetadata.GetMetadataProvider(connectionKey);

            bool isDotContext  = _contextDetector.IsDotContext(snapshot, caretPosition);
            string qualifier   = isDotContext ? _contextDetector.GetQualifier(snapshot, caretPosition) : null;

            var    currentWord      = _contextDetector.GetCurrentWord(snapshot, caretPosition);
            string wordBefore       = _contextDetector.GetWordBefore(snapshot, caretPosition - currentWord.Length);
            bool isAfterFromKeyword = string.Equals(wordBefore, "FROM",    System.StringComparison.OrdinalIgnoreCase);
            bool isAfterExecKeyword = string.Equals(wordBefore, "EXEC",    System.StringComparison.OrdinalIgnoreCase)
                                   || string.Equals(wordBefore, "EXECUTE", System.StringComparison.OrdinalIgnoreCase);
            bool isAfterWithKeyword = string.Equals(wordBefore, "WITH",    System.StringComparison.OrdinalIgnoreCase);

            bool isJoinOnContext    = _contextDetector.IsAfterKeyword(parseResult, line, column, "ON");
            bool isAfterJoinKeyword = _contextDetector.IsAfterKeyword(parseResult, line, column, "JOIN");
            bool isWhereContext     = _contextDetector.IsInsideWhereClause(parseResult, line, column);
            var (isAfterTable, tableNameBefore) = _contextDetector.DetectAliasContext(parseResult, line, column);

            var  (isInsideProc, procName, alreadyProvided) =
                _contextDetector.GetProcedureCallContext(snapshot, caretPosition);
            var  (isInsertCols, insertTable)  = _contextDetector.DetectInsertContext(parseResult, line, column);
            var  (isUpdateSet,  updateTable)  = _contextDetector.DetectUpdateSetContext(parseResult, line, column);

            string nearestClause  = _contextDetector.GetNearestClauseKeyword(snapshot, caretPosition);
            bool   isInSelectList = string.Equals(nearestClause, "SELECT", System.StringComparison.OrdinalIgnoreCase);

            bool isGroupByContext = _contextDetector.IsInsideGroupByClause(parseResult, line, column);
            bool isHavingContext  = _contextDetector.IsInsideHavingClause(parseResult, line, column);
            bool isOrderByContext = _contextDetector.IsInsideOrderByClause(parseResult, line, column);

            return new CompletionRequest(
                sql, caretPosition, line, column,
                connectionKey, parseResult, metadataProvider,
                isDotContext, qualifier,
                isAfterFromKeyword, isJoinOnContext, isAfterJoinKeyword, isWhereContext,
                isAfterTable, tableNameBefore,
                snapshot, isAfterExecKeyword,
                isInsideProc, procName, alreadyProvided,
                isInsertCols, isUpdateSet,
                isInsertCols ? insertTable : (isUpdateSet ? updateTable : null),
                isInSelectList, isGroupByContext, isHavingContext, isOrderByContext,
                isAfterWithKeyword);
        }
    }
}
