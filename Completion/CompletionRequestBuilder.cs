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
            var parseResult    = _sqlParser.Parse(sql);
            var metadataProvider = _databaseMetadata.GetMetadataProvider(connectionKey);
            bool isDotContext  = _contextDetector.IsDotContext(snapshot, caretPosition);
            string qualifier   = isDotContext ? _contextDetector.GetQualifier(snapshot, caretPosition) : null;
            bool isJoinOnContext  = _contextDetector.IsAfterKeyword(parseResult, line, column, "ON");
            bool isWhereContext   = _contextDetector.IsInsideWhereClause(parseResult, line, column);
            var (isAfterTable, tableNameBefore) = _contextDetector.DetectAliasContext(parseResult, sql, line, column, caretPosition);
            return new CompletionRequest(
                sql, caretPosition, line, column,
                connectionKey, parseResult, metadataProvider,
                isDotContext, qualifier,
                isJoinOnContext, isWhereContext,
                isAfterTable, tableNameBefore,
                snapshot);
        }
    }
}
