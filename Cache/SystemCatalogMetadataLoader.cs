using Microsoft.SqlServer.Management.Common;
using System.Collections.Generic;
using System.Data;

namespace SsmsAutocompletion {

    // Loads metadata by querying SQL Server system catalog views (sys.tables, sys.columns,
    // sys.foreign_keys). Three round-trips regardless of object count — much faster than
    // PrefetchObjects on large databases.
    internal sealed class SystemCatalogMetadataLoader : IMetadataLoader {

        public void Load(
            ServerConnection connection,
            List<TableInfo> tables,
            Dictionary<string, List<ColumnInfo>> columnMap,
            Dictionary<string, List<ForeignKeyInfo>> foreignKeyMap) {

            var db = EscapeName(connection.DatabaseName);
            LoadTables(connection, db, tables, columnMap);
            LoadColumns(connection, db, columnMap);
            LoadForeignKeys(connection, db, foreignKeyMap);
        }

        private static void LoadTables(
            ServerConnection connection, string db,
            List<TableInfo> tables,
            Dictionary<string, List<ColumnInfo>> columnMap) {

            var ds = connection.ExecuteWithResults($@"
                SELECT s.name AS SchemaName, t.name AS TableName
                FROM {db}.sys.tables  t
                JOIN {db}.sys.schemas s ON t.schema_id = s.schema_id
                WHERE t.is_ms_shipped = 0
                ORDER BY s.name, t.name");

            foreach (DataRow row in ds.Tables[0].Rows) {
                var schema = (string)row["SchemaName"];
                var table  = (string)row["TableName"];
                tables.Add(new TableInfo(schema, table));
                columnMap[MakeKey(schema, table)] = new List<ColumnInfo>();
            }
        }

        private static void LoadColumns(
            ServerConnection connection, string db,
            Dictionary<string, List<ColumnInfo>> columnMap) {

            var ds = connection.ExecuteWithResults($@"
                SELECT s.name  AS SchemaName,
                       t.name  AS TableName,
                       c.name  AS ColumnName,
                       tp.name AS TypeName
                FROM {db}.sys.columns  c
                JOIN {db}.sys.tables   t  ON c.object_id     = t.object_id
                JOIN {db}.sys.schemas  s  ON t.schema_id     = s.schema_id
                JOIN {db}.sys.types    tp ON c.user_type_id  = tp.user_type_id
                WHERE t.is_ms_shipped = 0
                ORDER BY s.name, t.name, c.column_id");

            foreach (DataRow row in ds.Tables[0].Rows) {
                var key = MakeKey((string)row["SchemaName"], (string)row["TableName"]);
                if (!columnMap.TryGetValue(key, out var cols)) {
                    cols = new List<ColumnInfo>();
                    columnMap[key] = cols;
                }
                cols.Add(new ColumnInfo((string)row["ColumnName"], (string)row["TypeName"]));
            }
        }

        private static void LoadForeignKeys(
            ServerConnection connection, string db,
            Dictionary<string, List<ForeignKeyInfo>> foreignKeyMap) {

            var ds = connection.ExecuteWithResults($@"
                SELECT ss.name AS SchemaName,
                       st.name AS TableName,
                       fk.name AS FkName,
                       c.name  AS ColumnName,
                       rs.name AS RefSchemaName,
                       rt.name AS RefTableName,
                       rc.name AS RefColumnName
                FROM {db}.sys.foreign_keys        fk
                JOIN {db}.sys.tables              st  ON fk.parent_object_id      = st.object_id
                JOIN {db}.sys.schemas             ss  ON st.schema_id             = ss.schema_id
                JOIN {db}.sys.tables              rt  ON fk.referenced_object_id  = rt.object_id
                JOIN {db}.sys.schemas             rs  ON rt.schema_id             = rs.schema_id
                JOIN {db}.sys.foreign_key_columns fkc ON fk.object_id             = fkc.constraint_object_id
                JOIN {db}.sys.columns             c   ON fkc.parent_object_id     = c.object_id
                                                     AND fkc.parent_column_id     = c.column_id
                JOIN {db}.sys.columns             rc  ON fkc.referenced_object_id = rc.object_id
                                                     AND fkc.referenced_column_id = rc.column_id
                WHERE st.is_ms_shipped = 0
                ORDER BY ss.name, st.name, fk.name, fkc.constraint_column_id");

            // One row per FK column — accumulate columns per FK before building ForeignKeyInfo.
            var acc = new Dictionary<string, FkAccumulator>();
            foreach (DataRow row in ds.Tables[0].Rows) {
                var accumKey = $"{row["SchemaName"]}.{row["TableName"]}.{row["FkName"]}";
                if (!acc.TryGetValue(accumKey, out var entry)) {
                    entry = new FkAccumulator(
                        (string)row["SchemaName"], (string)row["TableName"],
                        (string)row["RefSchemaName"], (string)row["RefTableName"]);
                    acc[accumKey] = entry;
                }
                entry.FkCols.Add((string)row["ColumnName"]);
                entry.RefCols.Add((string)row["RefColumnName"]);
            }

            foreach (var entry in acc.Values) {
                var fkInfo = new ForeignKeyInfo(
                    entry.Schema, entry.Table, entry.FkCols.AsReadOnly(),
                    entry.RefSchema, entry.RefTable, entry.RefCols.AsReadOnly());
                AddToMap(foreignKeyMap, MakeKey(entry.Schema, entry.Table), fkInfo);
                AddToMap(foreignKeyMap, MakeKey(entry.RefSchema, entry.RefTable), fkInfo);
            }
        }

        private static string MakeKey(string schema, string table) =>
            $"{schema ?? "dbo"}.{table}";

        // Wraps a database name in brackets and escapes any existing brackets.
        private static string EscapeName(string name) =>
            $"[{name.Replace("]", "]]")}]";

        private static void AddToMap<T>(Dictionary<string, List<T>> map, string key, T value) {
            if (!map.TryGetValue(key, out var list)) { list = new List<T>(); map[key] = list; }
            list.Add(value);
        }

        private sealed class FkAccumulator {
            public readonly string Schema, Table, RefSchema, RefTable;
            public readonly List<string> FkCols  = new List<string>();
            public readonly List<string> RefCols = new List<string>();
            public FkAccumulator(string schema, string table, string refSchema, string refTable) {
                Schema = schema; Table = table; RefSchema = refSchema; RefTable = refTable;
            }
        }
    }
}
