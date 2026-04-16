using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;
using Microsoft.SqlServer.Management.SmoMetadataProvider;
using Microsoft.SqlServer.Management.SqlParser.MetadataProvider;
using System;
using System.Collections.Generic;
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
            var newEntry = LoadFromObjectExplorer(serverConnection);
            lock (Lock) {
                if (!Entries.TryGetValue(connectionKey.ToString(), out var existing2) || existing2.IsExpired)
                    Entries[connectionKey.ToString()] = newEntry;
            }
        }

        // Navigates the SMO object tree (the same path as the SSMS Object Explorer) without
        // any hand-written SQL.  PrefetchObjects issues one SMO bulk query per type so that
        // iterating tables/columns/FKs afterwards hits no additional round-trips.
        private static CacheEntry LoadFromObjectExplorer(ServerConnection serverConnection) {
            var tables        = new List<TableInfo>();
            var columnMap     = new Dictionary<string, List<ColumnInfo>>(StringComparer.OrdinalIgnoreCase);
            var foreignKeyMap = new Dictionary<string, List<ForeignKeyInfo>>(StringComparer.OrdinalIgnoreCase);
            IMetadataProvider metadataProvider = null;
            try {
                metadataProvider = SmoMetadataProvider.CreateConnectedProvider(serverConnection);

                var server = new Server(serverConnection);

                // Restrict which properties SMO fetches per type so bulk queries stay lean.
                server.SetDefaultInitFields(typeof(Table),
                    nameof(Table.Schema), nameof(Table.Name), nameof(Table.IsSystemObject));
                server.SetDefaultInitFields(typeof(Column), true); // 'true' = all fields; needed so DataType.Name resolves without a second round-trip
                server.SetDefaultInitFields(typeof(ForeignKey),
                    nameof(ForeignKey.Name),
                    nameof(ForeignKey.ReferencedTable),
                    nameof(ForeignKey.ReferencedTableSchema));
                server.SetDefaultInitFields(typeof(ForeignKeyColumn),
                    nameof(ForeignKeyColumn.Name),
                    nameof(ForeignKeyColumn.ReferencedColumn));

                var db = server.Databases[serverConnection.DatabaseName];
                if (db == null) return BuildCacheEntry(metadataProvider, tables, columnMap, foreignKeyMap);

                // Bulk-load each type in a single SMO round-trip (best-effort per type).
                try { db.PrefetchObjects(typeof(Table));           } catch { }
                try { db.PrefetchObjects(typeof(Column));          } catch { }
                try { db.PrefetchObjects(typeof(ForeignKey));      } catch { }
                try { db.PrefetchObjects(typeof(ForeignKeyColumn)); } catch { }

                foreach (Table table in db.Tables) {
                    if (table.IsSystemObject) continue;
                    tables.Add(new TableInfo(table.Schema, table.Name));

                    var colList = new List<ColumnInfo>();
                    foreach (Column col in table.Columns)
                        colList.Add(new ColumnInfo(col.Name, col.DataType.Name));
                    columnMap[MakeTableKey(table.Schema, table.Name)] = colList;

                    foreach (ForeignKey fk in table.ForeignKeys) {
                        var fkCols  = new List<string>();
                        var refCols = new List<string>();
                        foreach (ForeignKeyColumn fkc in fk.Columns) {
                            fkCols.Add(fkc.Name);
                            refCols.Add(fkc.ReferencedColumn);
                        }
                        var fkInfo = new ForeignKeyInfo(
                            table.Schema, table.Name, fkCols.AsReadOnly(),
                            fk.ReferencedTableSchema, fk.ReferencedTable, refCols.AsReadOnly());
                        AddToListMap(foreignKeyMap, MakeTableKey(table.Schema,        table.Name),        fkInfo);
                        AddToListMap(foreignKeyMap, MakeTableKey(fk.ReferencedTableSchema, fk.ReferencedTable), fkInfo);
                    }
                }
            }
            catch { }
            return BuildCacheEntry(metadataProvider, tables, columnMap, foreignKeyMap);
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
