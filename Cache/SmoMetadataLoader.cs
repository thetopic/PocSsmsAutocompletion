using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;
using System.Collections.Generic;

namespace SsmsAutocompletion {

    // Loads metadata by navigating the SMO object tree (same path as SSMS Object Explorer).
    // PrefetchObjects issues one bulk SMO query per type, but is slow on databases with many
    // tables because SMO materialises every object into managed memory before returning.
    internal sealed class SmoMetadataLoader : IMetadataLoader {

        public void Load(
            ServerConnection connection,
            List<TableInfo> tables,
            Dictionary<string, List<ColumnInfo>> columnMap,
            Dictionary<string, List<ForeignKeyInfo>> foreignKeyMap) {

            var server = new Server(connection);

            server.SetDefaultInitFields(typeof(Table),
                nameof(Table.Schema), nameof(Table.Name), nameof(Table.IsSystemObject));
            server.SetDefaultInitFields(typeof(Column), true);
            server.SetDefaultInitFields(typeof(ForeignKey),
                nameof(ForeignKey.Name),
                nameof(ForeignKey.ReferencedTable),
                nameof(ForeignKey.ReferencedTableSchema));
            server.SetDefaultInitFields(typeof(ForeignKeyColumn),
                nameof(ForeignKeyColumn.Name),
                nameof(ForeignKeyColumn.ReferencedColumn));

            var db = server.Databases[connection.DatabaseName];
            if (db == null) return;

            try { db.PrefetchObjects(typeof(Table));            } catch { }
            try { db.PrefetchObjects(typeof(Column));           } catch { }
            try { db.PrefetchObjects(typeof(ForeignKey));       } catch { }
            try { db.PrefetchObjects(typeof(ForeignKeyColumn)); } catch { }

            foreach (Table table in db.Tables) {
                if (table.IsSystemObject) continue;
                tables.Add(new TableInfo(table.Schema, table.Name));

                var colList = new List<ColumnInfo>();
                foreach (Column col in table.Columns)
                    colList.Add(new ColumnInfo(col.Name, col.DataType.Name));
                columnMap[MakeKey(table.Schema, table.Name)] = colList;

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
                    AddToMap(foreignKeyMap, MakeKey(table.Schema, table.Name), fkInfo);
                    AddToMap(foreignKeyMap, MakeKey(fk.ReferencedTableSchema, fk.ReferencedTable), fkInfo);
                }
            }
        }

        private static string MakeKey(string schema, string table) =>
            $"{schema ?? "dbo"}.{table}";

        private static void AddToMap<T>(Dictionary<string, List<T>> map, string key, T value) {
            if (!map.TryGetValue(key, out var list)) { list = new List<T>(); map[key] = list; }
            list.Add(value);
        }
    }
}
