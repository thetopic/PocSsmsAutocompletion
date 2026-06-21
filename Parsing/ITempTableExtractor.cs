using Microsoft.SqlServer.Management.SqlParser.Parser;
using System.Collections.Generic;

namespace SsmsAutocompletion {

    internal interface ITempTableExtractor {
        IReadOnlyDictionary<string, IReadOnlyList<string>> Extract(ParseResult parseResult);
    }
}
