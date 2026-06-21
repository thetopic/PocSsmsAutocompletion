using Microsoft.SqlServer.Management.SqlParser.Parser;
using System.Collections.Generic;

namespace SsmsAutocompletion {

    internal interface ICteExtractor {
        IReadOnlyList<string> Extract(ParseResult parseResult);
        bool IsRecursive(ParseResult parseResult, string cteName);
    }
}
