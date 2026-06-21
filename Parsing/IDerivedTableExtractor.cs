using Microsoft.SqlServer.Management.SqlParser.Parser;
using System.Collections.Generic;

namespace SsmsAutocompletion {

    internal interface IDerivedTableExtractor {
        IReadOnlyDictionary<string, IReadOnlyList<string>> Extract(ParseResult parseResult);
    }
}
