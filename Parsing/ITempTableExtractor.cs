using Microsoft.SqlServer.Management.SqlParser.Parser;
using System.Collections.Generic;

namespace SsmsAutocompletion {

    /// <summary>
    /// Resolves locally-defined tables not present in the database catalog:
    /// temp tables (#x, via CREATE TABLE or SELECT ... INTO) and table variables (@x, via DECLARE ... TABLE).
    /// </summary>
    internal interface ITempTableExtractor {
        IReadOnlyDictionary<string, IReadOnlyList<string>> Extract(ParseResult parseResult);
    }
}
