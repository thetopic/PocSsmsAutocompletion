using Microsoft.SqlServer.Management.SqlParser.Parser;
using System.Collections.Generic;

namespace SsmsAutocompletion {

    internal interface IAliasExtractor {
        IReadOnlyDictionary<string, TableInfo> Extract(ParseResult parseResult);

        /// <summary>
        /// Like <see cref="Extract"/>, but scoped to the UNION branch (or plain statement)
        /// enclosing the given cursor position, so aliases from other branches of the same
        /// UNION/UNION ALL query don't leak into completion for an unrelated branch.
        /// </summary>
        IReadOnlyDictionary<string, TableInfo> ExtractInScope(ParseResult parseResult, int line, int column);
    }
}
