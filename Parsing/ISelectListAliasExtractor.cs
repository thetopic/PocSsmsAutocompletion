using Microsoft.SqlServer.Management.SqlParser.Parser;
using System.Collections.Generic;

namespace SsmsAutocompletion {

    internal interface ISelectListAliasExtractor {
        IReadOnlyList<string> Extract(ParseResult parseResult, int line, int column);
    }
}
