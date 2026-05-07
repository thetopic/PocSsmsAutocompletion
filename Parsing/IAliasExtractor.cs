using Microsoft.SqlServer.Management.SqlParser.Parser;
using System.Collections.Generic;

namespace SsmsAutocompletion {

    internal interface IAliasExtractor {
        IReadOnlyDictionary<string, TableInfo> Extract(ParseResult parseResult);
    }
}
