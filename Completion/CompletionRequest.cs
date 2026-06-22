using Microsoft.SqlServer.Management.SqlParser.MetadataProvider;
using Microsoft.SqlServer.Management.SqlParser.Parser;
using Microsoft.VisualStudio.Text;
using System.Collections.Generic;

namespace SsmsAutocompletion {

    internal sealed class CompletionRequest {
        public string            Sql               { get; }
        public int               CaretPosition     { get; }
        public int               Line              { get; }
        public int               Column            { get; }
        public ConnectionKey     ConnectionKey     { get; }
        public ParseResult       ParseResult       { get; }
        public IMetadataProvider MetadataProvider  { get; }
        public bool              IsDotContext      { get; }
        public string            Qualifier         { get; }
        public bool              IsAfterFromKeyword     { get; }
        public bool              IsJoinOnContext        { get; }
        public bool              IsAfterJoinKeyword     { get; }
        public bool              IsWhereContext         { get; }
        public bool              IsAfterTableInFromJoin { get; }
        public string            TableNameBeforeCursor  { get; }
        public ITextSnapshot     Snapshot               { get; }
        public bool              IsAfterExecKeyword     { get; }

        // Stored procedure parameter completion
        public bool                   IsInsideProcedureCall      { get; }
        public string                 ProcedureNameBeforeCursor  { get; }
        public IReadOnlyList<string>  AlreadyProvidedParameters  { get; }

        // INSERT / UPDATE completion
        public bool   IsInsertColumnList       { get; }
        public bool   IsUpdateSetClause        { get; }
        public string InsertUpdateTargetTable  { get; }

        // SELECT list unqualified column completion
        public bool   IsInSelectList  { get; }

        // GROUP BY column completion
        public bool   IsGroupByContext { get; }

        // HAVING column completion
        public bool   IsHavingContext { get; }

        // ORDER BY column completion
        public bool   IsOrderByContext { get; }

        // CTE skeleton snippet
        public bool   IsAfterWithKeyword { get; }

        // Window function OVER(PARTITION BY ... ORDER BY ...) column completion
        public bool   IsWindowContext { get; }

        public CompletionRequest(
            string sql, int caretPosition, int line, int column,
            ConnectionKey connectionKey, ParseResult parseResult,
            IMetadataProvider metadataProvider, bool isDotContext, string qualifier,
            bool isAfterFromKeyword, bool isJoinOnContext, bool isAfterJoinKeyword,
            bool isWhereContext, bool isAfterTableInFromJoin, string tableNameBeforeCursor,
            ITextSnapshot snapshot, bool isAfterExecKeyword = false,
            bool isInsideProcedureCall = false, string procedureNameBeforeCursor = null,
            IReadOnlyList<string> alreadyProvidedParameters = null,
            bool isInsertColumnList = false, bool isUpdateSetClause = false,
            string insertUpdateTargetTable = null, bool isInSelectList = false,
            bool isGroupByContext = false, bool isHavingContext = false,
            bool isOrderByContext = false, bool isAfterWithKeyword = false,
            bool isWindowContext = false) {
            Sql                       = sql;
            CaretPosition             = caretPosition;
            Line                      = line;
            Column                    = column;
            ConnectionKey             = connectionKey;
            ParseResult               = parseResult;
            MetadataProvider          = metadataProvider;
            IsDotContext              = isDotContext;
            Qualifier                 = qualifier;
            IsAfterFromKeyword        = isAfterFromKeyword;
            IsJoinOnContext           = isJoinOnContext;
            IsAfterJoinKeyword        = isAfterJoinKeyword;
            IsWhereContext            = isWhereContext;
            IsAfterTableInFromJoin    = isAfterTableInFromJoin;
            TableNameBeforeCursor     = tableNameBeforeCursor;
            Snapshot                  = snapshot;
            IsAfterExecKeyword        = isAfterExecKeyword;
            IsInsideProcedureCall     = isInsideProcedureCall;
            ProcedureNameBeforeCursor = procedureNameBeforeCursor;
            AlreadyProvidedParameters = alreadyProvidedParameters ?? System.Array.Empty<string>();
            IsInsertColumnList        = isInsertColumnList;
            IsUpdateSetClause         = isUpdateSetClause;
            InsertUpdateTargetTable   = insertUpdateTargetTable;
            IsInSelectList            = isInSelectList;
            IsGroupByContext          = isGroupByContext;
            IsHavingContext           = isHavingContext;
            IsOrderByContext          = isOrderByContext;
            IsAfterWithKeyword        = isAfterWithKeyword;
            IsWindowContext           = isWindowContext;
        }
    }
}
