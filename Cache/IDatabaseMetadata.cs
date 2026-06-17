using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.SqlParser.MetadataProvider;
using System.Collections.Generic;

namespace SsmsAutocompletion {

    internal interface IDatabaseMetadata {
        void Warm(ConnectionKey connectionKey, ServerConnection serverConnection);
        IMetadataProvider GetMetadataProvider(ConnectionKey connectionKey);
        IReadOnlyList<TableInfo> GetTables(ConnectionKey connectionKey);
        IReadOnlyList<ColumnInfo> GetColumns(ConnectionKey connectionKey, string schema, string tableName);
        IReadOnlyList<ForeignKeyInfo> GetForeignKeys(ConnectionKey connectionKey, string schema, string tableName);
        IReadOnlyList<ProcedureInfo>      GetProcedures(ConnectionKey connectionKey);
        IReadOnlyList<UserFunctionInfo>   GetUserDefinedFunctions(ConnectionKey connectionKey);
        IReadOnlyList<string>             GetSchemas(ConnectionKey connectionKey);
        void Invalidate(ConnectionKey connectionKey);
    }
}
