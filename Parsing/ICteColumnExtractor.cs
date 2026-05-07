using Microsoft.SqlServer.Management.SqlParser.Parser;
using System.Collections.Generic;

namespace SsmsAutocompletion {

    internal interface ICteColumnExtractor {
        IReadOnlyList<string> ExtractColumns(ParseResult parseResult, string cteName);
    }
}
