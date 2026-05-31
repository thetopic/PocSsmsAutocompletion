namespace SsmsAutocompletion {

    internal enum SqlObjectType { Table, View }

    internal sealed class TableInfo {
        public string        Schema     { get; }
        public string        TableName  { get; }
        public SqlObjectType ObjectType { get; }

        public TableInfo(string schema, string tableName, SqlObjectType objectType = SqlObjectType.Table) {
            Schema     = schema;
            TableName  = tableName;
            ObjectType = objectType;
        }

        public override string ToString() =>
            Schema == "dbo" ? TableName : $"{Schema}.{TableName}";
    }
}
