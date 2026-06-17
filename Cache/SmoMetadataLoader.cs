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
            Dictionary<string, List<ForeignKeyInfo>> foreignKeyMap,
            List<ProcedureInfo> procedures,
            List<UserFunctionInfo> userFunctions,
            List<string> schemas) {

            var server = new Server(connection);

            server.SetDefaultInitFields(typeof(Table),
                nameof(Table.Schema), nameof(Table.Name), nameof(Table.IsSystemObject));
            server.SetDefaultInitFields(typeof(View),
                nameof(View.Schema), nameof(View.Name), nameof(View.IsSystemObject));
            server.SetDefaultInitFields(typeof(UserDefinedFunction),
                nameof(UserDefinedFunction.Schema), nameof(UserDefinedFunction.Name),
                nameof(UserDefinedFunction.IsSystemObject),
                nameof(UserDefinedFunction.FunctionType));
            server.SetDefaultInitFields(typeof(UserDefinedFunctionParameter), true);
            server.SetDefaultInitFields(typeof(StoredProcedure),
                nameof(StoredProcedure.Schema), nameof(StoredProcedure.Name),
                nameof(StoredProcedure.IsSystemObject));
            server.SetDefaultInitFields(typeof(StoredProcedureParameter), true);
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
            try { db.PrefetchObjects(typeof(View));             } catch { }
            try { db.PrefetchObjects(typeof(UserDefinedFunction)); } catch { }
            try { db.PrefetchObjects(typeof(StoredProcedure)); } catch { }
            try { db.PrefetchObjects(typeof(Column));           } catch { }
            try { db.PrefetchObjects(typeof(ForeignKey));       } catch { }
            try { db.PrefetchObjects(typeof(ForeignKeyColumn)); } catch { }

            LoadUserDefinedFunctions(db, userFunctions);
            LoadProcedures(db, procedures);
            LoadViews(db, tables, columnMap);

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

        private static void LoadUserDefinedFunctions(
            Database db,
            List<UserFunctionInfo> userFunctions) {

            foreach (UserDefinedFunction udf in db.UserDefinedFunctions) {
                if (udf.IsSystemObject) continue;
                UserFunctionType funcType;
                switch (udf.FunctionType) {
                    case UserDefinedFunctionType.Table:  funcType = UserFunctionType.TableValued;       break;
                    case UserDefinedFunctionType.Inline: funcType = UserFunctionType.InlineTableValued; break;
                    default:                             funcType = UserFunctionType.Scalar;            break;
                }
                var paramList = new List<ParameterInfo>();
                foreach (UserDefinedFunctionParameter param in udf.Parameters) {
                    paramList.Add(new ParameterInfo(
                        param.Name, param.DataType.Name,
                        isOutput: false,
                        hasDefault: !string.IsNullOrEmpty(param.DefaultValue)));
                }
                userFunctions.Add(new UserFunctionInfo(udf.Schema, udf.Name, funcType, paramList.AsReadOnly()));
            }
        }

        private static void LoadProcedures(
            Database db,
            List<ProcedureInfo> procedures) {

            foreach (StoredProcedure sp in db.StoredProcedures) {
                if (sp.IsSystemObject) continue;
                var paramList = new List<ParameterInfo>();
                foreach (StoredProcedureParameter param in sp.Parameters) {
                    paramList.Add(new ParameterInfo(
                        param.Name,
                        param.DataType.Name,
                        param.IsOutputParameter,
                        !string.IsNullOrEmpty(param.DefaultValue)));
                }
                procedures.Add(new ProcedureInfo(sp.Schema, sp.Name, paramList.AsReadOnly()));
            }
        }

        private static void LoadViews(
            Database db,
            List<TableInfo> tables,
            Dictionary<string, List<ColumnInfo>> columnMap) {

            foreach (View view in db.Views) {
                if (view.IsSystemObject) continue;
                tables.Add(new TableInfo(view.Schema, view.Name, SqlObjectType.View));
                var colList = new List<ColumnInfo>();
                foreach (Column col in view.Columns)
                    colList.Add(new ColumnInfo(col.Name, col.DataType.Name));
                columnMap[MakeKey(view.Schema, view.Name)] = colList;
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
