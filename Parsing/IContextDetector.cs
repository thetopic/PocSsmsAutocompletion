using Microsoft.SqlServer.Management.SqlParser.Parser;
using Microsoft.VisualStudio.Text;

namespace SsmsAutocompletion {

    internal interface IContextDetector {
        (int line, int column) GetLineColumn(ITextSnapshot snapshot, int position);
        bool IsDotContext(ITextSnapshot snapshot, int caretPosition);
        string GetQualifier(ITextSnapshot snapshot, int caretPosition);
        bool IsAfterKeyword(ParseResult parseResult, int line, int column, string keyword);
        bool IsInsideWhereClause(ParseResult parseResult, int line, int column);
        string GetCurrentWord(ITextSnapshot snapshot, int caretPosition);
        ITrackingSpan GetWordSpan(ITextSnapshot snapshot, int caretPosition);
        string GetWordBefore(ITextSnapshot snapshot, int caretPosition);
        (bool isAliasContext, string tableNameBefore) DetectAliasContext(ParseResult parseResult, string sql, int line, int column, int caretPosition);
    }
}
