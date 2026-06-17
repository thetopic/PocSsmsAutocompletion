using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.SmoMetadataProvider;
using Microsoft.SqlServer.Management.SqlParser.MetadataProvider;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SsmsAutocompletion {

    internal sealed class DatabaseMetadataCache : IDatabaseMetadata {

        public static readonly IDatabaseMetadata Instance = new DatabaseMetadataCache();

        // Switch between SystemCatalogMetadataLoader (fast, default) and SmoMetadataLoader.
        public static IMetadataLoader Loader { get; set; } = new SystemCatalogMetadataLoader();

        private static readonly object Lock  = new object();
        private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
        private static readonly Dictionary<string, CacheEntry> Entries =
            new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

        public void Warm(ConnectionKey connectionKey, ServerConnection serverConnection) {
            if (connectionKey == null || connectionKey.IsEmpty || serverConnection == null) return;
            _ = Task.Run(() => EnsureLoaded(connectionKey, serverConnection));
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

        public IReadOnlyList<ProcedureInfo> GetProcedures(ConnectionKey connectionKey) {
            if (connectionKey == null || connectionKey.IsEmpty) return Array.Empty<ProcedureInfo>();
            lock (Lock) {
                if (Entries.TryGetValue(connectionKey.ToString(), out var entry) && !entry.IsExpired)
                    return entry.Procedures;
            }
            return Array.Empty<ProcedureInfo>();
        }

        public IReadOnlyList<UserFunctionInfo> GetUserDefinedFunctions(ConnectionKey connectionKey) {
            if (connectionKey == null || connectionKey.IsEmpty) return Array.Empty<UserFunctionInfo>();
            lock (Lock) {
                if (Entries.TryGetValue(connectionKey.ToString(), out var entry) && !entry.IsExpired)
                    return entry.UserFunctions;
            }
            return Array.Empty<UserFunctionInfo>();
        }

        public IReadOnlyList<string> GetSchemas(ConnectionKey connectionKey) {
            if (connectionKey == null || connectionKey.IsEmpty) return Array.Empty<string>();
            lock (Lock) {
                if (Entries.TryGetValue(connectionKey.ToString(), out var entry) && !entry.IsExpired)
                    return entry.Schemas;
            }
            return Array.Empty<string>();
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
            var newEntry = Load(serverConnection);
            lock (Lock) {
                if (!Entries.TryGetValue(connectionKey.ToString(), out var existing2) || existing2.IsExpired)
                    Entries[connectionKey.ToString()] = newEntry;
            }
        }

        private static CacheEntry Load(ServerConnection serverConnection) {
            var tables        = new List<TableInfo>();
            var columnMap     = new Dictionary<string, List<ColumnInfo>>(StringComparer.OrdinalIgnoreCase);
            var foreignKeyMap = new Dictionary<string, List<ForeignKeyInfo>>(StringComparer.OrdinalIgnoreCase);
            var procedures    = new List<ProcedureInfo>();
            var userFunctions = new List<UserFunctionInfo>();
            var schemas       = new List<string>();
            IMetadataProvider metadataProvider = null;
            try {
                metadataProvider = SmoMetadataProvider.CreateConnectedProvider(serverConnection);
                Loader.Load(serverConnection, tables, columnMap, foreignKeyMap, procedures, userFunctions, schemas);
            }
            catch { }
            return BuildCacheEntry(metadataProvider, tables, columnMap, foreignKeyMap, procedures, userFunctions, schemas);
        }

        private static string MakeTableKey(string schema, string tableName) =>
            $"{schema ?? "dbo"}.{tableName}";

        private static CacheEntry BuildCacheEntry(
            IMetadataProvider metadataProvider, List<TableInfo> tables,
            Dictionary<string, List<ColumnInfo>> columnMap,
            Dictionary<string, List<ForeignKeyInfo>> foreignKeyMap,
            List<ProcedureInfo> procedures,
            List<UserFunctionInfo> userFunctions,
            List<string> schemas) {
            var frozenColumns = new Dictionary<string, IReadOnlyList<ColumnInfo>>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in columnMap) frozenColumns[kv.Key] = kv.Value.AsReadOnly();
            var frozenForeignKeys = new Dictionary<string, IReadOnlyList<ForeignKeyInfo>>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in foreignKeyMap) frozenForeignKeys[kv.Key] = kv.Value.AsReadOnly();
            return new CacheEntry(metadataProvider, tables.AsReadOnly(), frozenColumns, frozenForeignKeys,
                procedures.AsReadOnly(), userFunctions.AsReadOnly(), schemas.AsReadOnly());
        }

        private sealed class CacheEntry {
            public readonly IMetadataProvider MetadataProvider;
            public readonly IReadOnlyList<TableInfo> Tables;
            public readonly IReadOnlyDictionary<string, IReadOnlyList<ColumnInfo>> Columns;
            public readonly IReadOnlyDictionary<string, IReadOnlyList<ForeignKeyInfo>> ForeignKeys;
            public readonly IReadOnlyList<ProcedureInfo>    Procedures;
            public readonly IReadOnlyList<UserFunctionInfo> UserFunctions;
            public readonly IReadOnlyList<string>           Schemas;
            private readonly DateTime _loadedAt;

            public bool IsExpired => DateTime.UtcNow - _loadedAt > Ttl;

            public CacheEntry(
                IMetadataProvider metadataProvider,
                IReadOnlyList<TableInfo> tables,
                IReadOnlyDictionary<string, IReadOnlyList<ColumnInfo>> columns,
                IReadOnlyDictionary<string, IReadOnlyList<ForeignKeyInfo>> foreignKeys,
                IReadOnlyList<ProcedureInfo> procedures,
                IReadOnlyList<UserFunctionInfo> userFunctions,
                IReadOnlyList<string> schemas) {
                MetadataProvider = metadataProvider;
                Tables           = tables;
                Columns          = columns;
                ForeignKeys      = foreignKeys;
                Procedures       = procedures;
                UserFunctions    = userFunctions;
                Schemas          = schemas;
                _loadedAt        = DateTime.UtcNow;
            }
        }
    }
}
