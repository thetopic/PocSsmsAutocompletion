using Microsoft.SqlServer.Management.Common;
using System.Collections.Generic;

namespace SsmsAutocompletion {

    internal interface IMetadataLoader {
        void Load(
            ServerConnection connection,
            List<TableInfo> tables,
            Dictionary<string, List<ColumnInfo>> columnMap,
            Dictionary<string, List<ForeignKeyInfo>> foreignKeyMap,
            List<ProcedureInfo> procedures,
            List<UserFunctionInfo> userFunctions,
            List<string> schemas);
    }
}
