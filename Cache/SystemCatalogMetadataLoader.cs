using Microsoft.SqlServer.Management.Common;
using System;
using System.Collections.Generic;
using System.Data;

namespace SsmsAutocompletion {

    // Loads metadata by querying SQL Server system catalog views (sys.tables, sys.views,
    // sys.columns, sys.foreign_keys). Four round-trips regardless of object count — much
    // faster than PrefetchObjects on large databases.
    internal sealed class SystemCatalogMetadataLoader : IMetadataLoader {

        public void Load(
            ServerConnection connection,
            List<TableInfo> tables,
            Dictionary<string, List<ColumnInfo>> columnMap,
            Dictionary<string, List<ForeignKeyInfo>> foreignKeyMap,
            List<ProcedureInfo> procedures,
            List<UserFunctionInfo> userFunctions,
            List<string> schemas) {

            var db = EscapeName(connection.DatabaseName);
            LoadTables(connection, db, tables, columnMap);
            LoadViews(connection, db, tables, columnMap);
            LoadColumns(connection, db, columnMap);
            LoadForeignKeys(connection, db, foreignKeyMap);
            LoadProcedures(connection, db, procedures);
            LoadUserDefinedFunctions(connection, db, userFunctions);
            LoadSchemas(connection, db, schemas);
        }

        private static void LoadSchemas(
            ServerConnection connection, string db,
            List<string> schemas) {
            try {
                var ds = connection.ExecuteWithResults($@"
                    SELECT name FROM {db}.sys.schemas WHERE schema_id < 16384 ORDER BY name");
                foreach (DataRow row in ds.Tables[0].Rows)
                    schemas.Add((string)row["name"]);
            }
            catch { }
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

        private static void LoadViews(
            ServerConnection connection, string db,
            List<TableInfo> tables,
            Dictionary<string, List<ColumnInfo>> columnMap) {

            var ds = connection.ExecuteWithResults($@"
                SELECT s.name AS SchemaName, v.name AS ViewName
                FROM {db}.sys.views   v
                JOIN {db}.sys.schemas s ON v.schema_id = s.schema_id
                WHERE v.is_ms_shipped = 0
                ORDER BY s.name, v.name");

            foreach (DataRow row in ds.Tables[0].Rows) {
                var schema = (string)row["SchemaName"];
                var view   = (string)row["ViewName"];
                tables.Add(new TableInfo(schema, view, SqlObjectType.View));
                columnMap[MakeKey(schema, view)] = new List<ColumnInfo>();
            }
        }

        private static void LoadColumns(
            ServerConnection connection, string db,
            Dictionary<string, List<ColumnInfo>> columnMap) {

            var ds = connection.ExecuteWithResults($@"
                SELECT s.name  AS SchemaName,
                       o.name  AS TableName,
                       c.name  AS ColumnName,
                       tp.name AS TypeName
                FROM {db}.sys.columns  c
                JOIN {db}.sys.objects  o  ON c.object_id    = o.object_id
                JOIN {db}.sys.schemas  s  ON o.schema_id    = s.schema_id
                JOIN {db}.sys.types    tp ON c.user_type_id = tp.user_type_id
                WHERE o.type IN ('U', 'V')
                  AND o.is_ms_shipped = 0
                ORDER BY s.name, o.name, c.column_id");

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

        private static void LoadProcedures(
            ServerConnection connection, string db,
            List<ProcedureInfo> procedures) {

            var ds = connection.ExecuteWithResults($@"
                SELECT s.name  AS SchemaName,
                       p.name  AS ProcedureName,
                       pr.name AS ParameterName,
                       tp.name AS TypeName,
                       pr.is_output         AS IsOutput,
                       pr.has_default_value AS HasDefault
                FROM {db}.sys.procedures p
                JOIN {db}.sys.schemas    s  ON p.schema_id     = s.schema_id
                LEFT JOIN {db}.sys.parameters pr ON p.object_id    = pr.object_id
                                               AND pr.parameter_id > 0
                LEFT JOIN {db}.sys.types      tp ON pr.user_type_id = tp.user_type_id
                WHERE p.is_ms_shipped = 0
                ORDER BY s.name, p.name, pr.parameter_id");

            var acc = new Dictionary<string, ProcAccumulator>();
            foreach (DataRow row in ds.Tables[0].Rows) {
                var schema = (string)row["SchemaName"];
                var name   = (string)row["ProcedureName"];
                var key    = MakeKey(schema, name);
                if (!acc.TryGetValue(key, out var entry)) {
                    entry = new ProcAccumulator(schema, name);
                    acc[key] = entry;
                }
                if (row["ParameterName"] != DBNull.Value) {
                    entry.Parameters.Add(new ParameterInfo(
                        (string)row["ParameterName"],
                        (string)row["TypeName"],
                        (bool)row["IsOutput"],
                        (bool)row["HasDefault"]));
                }
            }
            foreach (var entry in acc.Values)
                procedures.Add(new ProcedureInfo(entry.Schema, entry.Name, entry.Parameters.AsReadOnly()));
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

        private static void LoadUserDefinedFunctions(
            ServerConnection connection, string db,
            List<UserFunctionInfo> userFunctions) {

            var ds = connection.ExecuteWithResults($@"
                SELECT s.name  AS SchemaName,
                       o.name  AS FunctionName,
                       o.type  AS FunctionType,
                       pr.name AS ParameterName,
                       tp.name AS TypeName,
                       pr.has_default_value AS HasDefault
                FROM {db}.sys.objects  o
                JOIN {db}.sys.schemas  s  ON o.schema_id  = s.schema_id
                LEFT JOIN {db}.sys.parameters pr ON o.object_id    = pr.object_id
                                               AND pr.parameter_id > 0
                LEFT JOIN {db}.sys.types      tp ON pr.user_type_id = tp.user_type_id
                WHERE o.type IN ('FN', 'TF', 'IF')
                  AND o.is_ms_shipped = 0
                ORDER BY s.name, o.name, pr.parameter_id");

            var acc = new Dictionary<string, UdfAccumulator>();
            foreach (DataRow row in ds.Tables[0].Rows) {
                var schema = (string)row["SchemaName"];
                var name   = (string)row["FunctionName"];
                var key    = MakeKey(schema, name);
                if (!acc.TryGetValue(key, out var entry)) {
                    var sqlType  = ((string)row["FunctionType"]).Trim();
                    var funcType = SqlTypeToFunctionType(sqlType);
                    entry = new UdfAccumulator(schema, name, funcType);
                    acc[key] = entry;
                }
                if (row["ParameterName"] != DBNull.Value) {
                    entry.Parameters.Add(new ParameterInfo(
                        (string)row["ParameterName"],
                        (string)row["TypeName"],
                        isOutput: false,
                        hasDefault: (bool)row["HasDefault"]));
                }
            }
            foreach (var entry in acc.Values)
                userFunctions.Add(new UserFunctionInfo(
                    entry.Schema, entry.Name, entry.FunctionType,
                    entry.Parameters.AsReadOnly()));
        }

        private static UserFunctionType SqlTypeToFunctionType(string sqlType) {
            if (sqlType == "TF") return UserFunctionType.TableValued;
            if (sqlType == "IF") return UserFunctionType.InlineTableValued;
            return UserFunctionType.Scalar;
        }

        private sealed class UdfAccumulator {
            public readonly string Schema, Name;
            public readonly UserFunctionType FunctionType;
            public readonly List<ParameterInfo> Parameters = new List<ParameterInfo>();
            public UdfAccumulator(string schema, string name, UserFunctionType funcType) {
                Schema = schema; Name = name; FunctionType = funcType;
            }
        }

        private sealed class ProcAccumulator {
            public readonly string Schema, Name;
            public readonly List<ParameterInfo> Parameters = new List<ParameterInfo>();
            public ProcAccumulator(string schema, string name) { Schema = schema; Name = name; }
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
