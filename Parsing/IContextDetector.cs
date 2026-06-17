using Microsoft.SqlServer.Management.SqlParser.Parser;
using Microsoft.VisualStudio.Text;
using System.Collections.Generic;

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
        (bool isAliasContext, string tableNameBefore) DetectAliasContext(ParseResult parseResult, int line, int column);
        bool IsAfterExecKeyword(ParseResult parseResult, int line, int column);

        // Procedure call context
        (bool isInside, string procedureName, IReadOnlyList<string> alreadyProvided)
            GetProcedureCallContext(ITextSnapshot snapshot, int caretPosition);

        // INSERT column list context
        (bool isInside, string targetTable) DetectInsertContext(ParseResult parseResult, int line, int column);

        // UPDATE SET context
        (bool isInside, string targetTable) DetectUpdateSetContext(ParseResult parseResult, int line, int column);

        // Nearest clause keyword (e.g. SELECT, FROM, WHERE) — text-based backward scan
        string GetNearestClauseKeyword(ITextSnapshot snapshot, int caretPosition);
    }
}
