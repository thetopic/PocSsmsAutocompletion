using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.SmoMetadataProvider;
using Microsoft.SqlServer.Management.SqlParser.MetadataProvider;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace SsmsAutocompletion {

    internal sealed class DatabaseMetadataCache : IDatabaseMetadata {

        public static readonly IDatabaseMetadata Instance = new DatabaseMetadataCache();

        private static readonly object Lock  = new object();
        private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
        private static readonly Dictionary<string, CacheEntry> Entries =
            new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

        public void WarmAsync(ConnectionKey connectionKey, ServerConnection serverConnection) {
            if (connectionKey == null || connectionKey.IsEmpty || serverConnection == null) return;
            Task.Run(() => EnsureLoaded(connectionKey, serverConnection));
        }

        public IMetadataProvider GetMetadataProvider(ConnectionKey connectionKey) {
            if (connectionKey == null || connectionKey.IsEmpty) return null;
            lock (Lock) {
                if (Entries.TryGetValue(connectionKey.ToString(), out var entry) && !entry.IsExpired)
                    return entry.MetadataProvider;
            }
            return null;
        }

        public IReadOnlyList<TableInfo> GetTables(ConnectionKey connectionKey) {
            if (connectionKey == null || connectionKey.IsEmpty) return Array.Empty<TableInfo>();
            lock (Lock) {
                if (Entries.TryGetValue(connectionKey.ToString(), out var entry) && !entry.IsExpired)
                    return entry.Tables;
            }
            return Array.Empty<TableInfo>();
        }

        public IReadOnlyList<ColumnInfo> GetColumns(ConnectionKey connectionKey, string schema, string tableName) {
            if (connectionKey == null || connectionKey.IsEmpty) return Array.Empty<ColumnInfo>();
            lock (Lock) {
                if (Entries.TryGetValue(connectionKey.ToString(), out var entry) && !entry.IsExpired) {
                    if (entry.Columns.TryGetValue(MakeTableKey(schema, tableName), out var columns))
                        return columns;
                }
            }
            return Array.Empty<ColumnInfo>();
        }

        public IReadOnlyList<ForeignKeyInfo> GetForeignKeys(ConnectionKey connectionKey, string schema, string tableName) {
            if (connectionKey == null || connectionKey.IsEmpty) return Array.Empty<ForeignKeyInfo>();
            lock (Lock) {
                if (Entries.TryGetValue(connectionKey.ToString(), out var entry) && !entry.IsExpired) {
                    if (entry.ForeignKeys.TryGetValue(MakeTableKey(schema, tableName), out var foreignKeys))
                        return foreignKeys;
                }
            }
            return Array.Empty<ForeignKeyInfo>();
        }

        public void Invalidate(ConnectionKey connectionKey) {
            if (connectionKey == null || connectionKey.IsEmpty) return;
            lock (Lock) { Entries.Remove(connectionKey.ToString()); }
        }

        private static void EnsureLoaded(ConnectionKey connectionKey, ServerConnection serverConnection) {
            lock (Lock) {
                if (Entries.TryGetValue(connectionKey.ToString(), out var existing) && !existing.IsExpired)
                    return;
            }
            var newEntry = LoadFromSql(serverConnection);
            lock (Lock) {
                if (!Entries.TryGetValue(connectionKey.ToString(), out var existing2) || existing2.IsExpired)
                    Entries[connectionKey.ToString()] = newEntry;
            }
        }

        // Replaces the SMO N+1 approach (1 query per table) with 3 bulk SQL queries
        // against system catalog views, reducing 30min → a few seconds on large databases.
        private static CacheEntry LoadFromSql(ServerConnection serverConnection) {
            var tables        = new List<TableInfo>();
            var columnMap     = new Dictionary<string, List<ColumnInfo>>(StringComparer.OrdinalIgnoreCase);
            var foreignKeyMap = new Dictionary<string, List<ForeignKeyInfo>>(StringComparer.OrdinalIgnoreCase);
            IMetadataProvider metadataProvider = null;
            try {
                metadataProvider = SmoMetadataProvider.CreateConnectedProvider(serverConnection);
                using (var conn = BuildSqlConnection(serverConnection)) {
                    conn.Open();
                    LoadTables(conn, tables);
                    LoadColumns(conn, columnMap);
                    LoadForeignKeys(conn, foreignKeyMap);
                }
            }
            catch { }
            return BuildCacheEntry(metadataProvider, tables, columnMap, foreignKeyMap);
        }

        private static SqlConnection BuildSqlConnection(ServerConnection sc) {
            var builder = new SqlConnectionStringBuilder {
                DataSource         = sc.ServerInstance,
                InitialCatalog     = sc.DatabaseName,
                IntegratedSecurity = sc.LoginSecure,
                ConnectTimeout     = 30,
            };
            if (!sc.LoginSecure) {
                builder.UserID   = sc.Login;
                builder.Password = sc.Password;
            }
            return new SqlConnection(builder.ConnectionString);
        }

        private static void LoadTables(SqlConnection conn, List<TableInfo> tables) {
            const string sql = @"
                SELECT s.name, t.name
                FROM sys.tables t
                INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                WHERE t.is_ms_shipped = 0
                ORDER BY s.name, t.name";
            using (var cmd = new SqlCommand(sql, conn) { CommandTimeout = 60 })
            using (var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess)) {
                while (reader.Read())
                    tables.Add(new TableInfo(reader.GetString(0), reader.GetString(1)));
            }
        }

        private static void LoadColumns(SqlConnection conn, Dictionary<string, List<ColumnInfo>> columnMap) {
            const string sql = @"
                SELECT s.name, t.name, c.name, tp.name
                FROM sys.columns c
                INNER JOIN sys.tables  t  ON c.object_id    = t.object_id
                INNER JOIN sys.schemas s  ON t.schema_id    = s.schema_id
                INNER JOIN sys.types   tp ON c.user_type_id = tp.user_type_id
                WHERE t.is_ms_shipped = 0
                ORDER BY s.name, t.name, c.column_id";
            using (var cmd = new SqlCommand(sql, conn) { CommandTimeout = 60 })
            using (var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess)) {
                while (reader.Read()) {
                    string key = MakeTableKey(reader.GetString(0), reader.GetString(1));
                    if (!columnMap.TryGetValue(key, out var list)) {
                        list           = new List<ColumnInfo>();
                        columnMap[key] = list;
                    }
                    list.Add(new ColumnInfo(reader.GetString(2), reader.GetString(3)));
                }
            }
        }

        private static void LoadForeignKeys(SqlConnection conn, Dictionary<string, List<ForeignKeyInfo>> foreignKeyMap) {
            // fk.name is first so columns are read in strict sequential order (0,1,2,3,4,5,6)
            // as required by CommandBehavior.SequentialAccess.
            const string sql = @"
                SELECT
                    fk.name,
                    ss.name, st.name,
                    COL_NAME(fkc.parent_object_id,     fkc.parent_column_id)     AS FkCol,
                    rs.name, rt.name,
                    COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id) AS RefCol
                FROM sys.foreign_keys fk
                INNER JOIN sys.tables  st  ON fk.parent_object_id     = st.object_id
                INNER JOIN sys.schemas ss  ON st.schema_id            = ss.schema_id
                INNER JOIN sys.tables  rt  ON fk.referenced_object_id = rt.object_id
                INNER JOIN sys.schemas rs  ON rt.schema_id            = rs.schema_id
                INNER JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
                ORDER BY fk.name, fkc.constraint_column_id";

            // Aggregate multi-column FKs before building ForeignKeyInfo objects
            var fkData = new Dictionary<string, FkAccumulator>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = new SqlCommand(sql, conn) { CommandTimeout = 60 })
            using (var reader = cmd.ExecuteReader(CommandBehavior.SequentialAccess)) {
                while (reader.Read()) {
                    // Read all columns in strict order 0→6 as required by SequentialAccess
                    string fkName      = reader.GetString(0); // fk.name
                    string ownerSchema = reader.GetString(1); // ss.name
                    string ownerTable  = reader.GetString(2); // st.name
                    string fkCol       = reader.GetString(3); // FkCol
                    string refSchema   = reader.GetString(4); // rs.name
                    string refTable    = reader.GetString(5); // rt.name
                    string refCol      = reader.GetString(6); // RefCol
                    if (!fkData.TryGetValue(fkName, out var acc)) {
                        acc            = new FkAccumulator(ownerSchema, ownerTable, refSchema, refTable);
                        fkData[fkName] = acc;
                    }
                    acc.FkCols.Add(fkCol);
                    acc.RefCols.Add(refCol);
                }
            }

            foreach (var acc in fkData.Values) {
                var fkInfo    = new ForeignKeyInfo(
                    acc.OwnerSchema, acc.OwnerTable, acc.FkCols.AsReadOnly(),
                    acc.RefSchema,   acc.RefTable,   acc.RefCols.AsReadOnly());
                AddToListMap(foreignKeyMap, MakeTableKey(acc.OwnerSchema, acc.OwnerTable), fkInfo);
                AddToListMap(foreignKeyMap, MakeTableKey(acc.RefSchema,   acc.RefTable),   fkInfo);
            }
        }

        private static void AddToListMap<T>(Dictionary<string, List<T>> map, string key, T value) {
            if (!map.TryGetValue(key, out var list)) {
                list     = new List<T>();
                map[key] = list;
            }
            list.Add(value);
        }

        private static CacheEntry BuildCacheEntry(
            IMetadataProvider metadataProvider, List<TableInfo> tables,
            Dictionary<string, List<ColumnInfo>> columnMap,
            Dictionary<string, List<ForeignKeyInfo>> foreignKeyMap) {
            var frozenColumns = new Dictionary<string, IReadOnlyList<ColumnInfo>>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in columnMap) frozenColumns[kv.Key] = kv.Value.AsReadOnly();
            var frozenForeignKeys = new Dictionary<string, IReadOnlyList<ForeignKeyInfo>>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in foreignKeyMap) frozenForeignKeys[kv.Key] = kv.Value.AsReadOnly();
            return new CacheEntry(metadataProvider, tables.AsReadOnly(), frozenColumns, frozenForeignKeys);
        }

        private static string MakeTableKey(string schema, string tableName) =>
            $"{schema ?? "dbo"}.{tableName}";

        private sealed class FkAccumulator {
            public readonly string OwnerSchema, OwnerTable, RefSchema, RefTable;
            public readonly List<string> FkCols  = new List<string>();
            public readonly List<string> RefCols = new List<string>();
            public FkAccumulator(string ownerSchema, string ownerTable, string refSchema, string refTable) {
                OwnerSchema = ownerSchema; OwnerTable = ownerTable;
                RefSchema   = refSchema;   RefTable   = refTable;
            }
        }

        private sealed class CacheEntry {
            public readonly IMetadataProvider MetadataProvider;
            public readonly IReadOnlyList<TableInfo> Tables;
            public readonly IReadOnlyDictionary<string, IReadOnlyList<ColumnInfo>> Columns;
            public readonly IReadOnlyDictionary<string, IReadOnlyList<ForeignKeyInfo>> ForeignKeys;
            private readonly DateTime _loadedAt;

            public bool IsExpired => DateTime.UtcNow - _loadedAt > Ttl;

            public CacheEntry(
                IMetadataProvider metadataProvider,
                IReadOnlyList<TableInfo> tables,
                IReadOnlyDictionary<string, IReadOnlyList<ColumnInfo>> columns,
                IReadOnlyDictionary<string, IReadOnlyList<ForeignKeyInfo>> foreignKeys) {
                MetadataProvider = metadataProvider;
                Tables           = tables;
                Columns          = columns;
                ForeignKeys      = foreignKeys;
                _loadedAt        = DateTime.UtcNow;
            }

            public static CacheEntry Empty() => new CacheEntry(
                null,
                Array.Empty<TableInfo>(),
                new Dictionary<string, IReadOnlyList<ColumnInfo>>(),
                new Dictionary<string, IReadOnlyList<ForeignKeyInfo>>());
        }
    }
}
