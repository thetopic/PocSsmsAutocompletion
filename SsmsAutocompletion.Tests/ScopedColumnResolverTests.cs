using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;

namespace SsmsAutocompletion.Tests {

    [TestClass]
    public class ScopedColumnResolverTests {

        private static readonly SsmsSqlParser         Parser             = new SsmsSqlParser();
        private static readonly AliasExtractor        AliasExtractor     = new AliasExtractor();
        private static readonly CteExtractor          CteExtractor       = new CteExtractor();
        private static readonly CteColumnExtractor    CteColumnExtractor = new CteColumnExtractor();
        private static readonly DerivedTableExtractor DerivedTableExtractor = new DerivedTableExtractor();

        private sealed class FakeMetadata : IDatabaseMetadata {
            private readonly Dictionary<string, IReadOnlyList<ColumnInfo>> _columns =
                new Dictionary<string, IReadOnlyList<ColumnInfo>>();

            public void AddColumns(string schema, string table, params string[] columnNames) {
                _columns[(schema + "." + table).ToLowerInvariant()] =
                    columnNames.Select(c => new ColumnInfo(c, "int")).ToList();
            }

            public void Warm(ConnectionKey k, Microsoft.SqlServer.Management.Common.ServerConnection s) { }
            public Microsoft.SqlServer.Management.SqlParser.MetadataProvider.IMetadataProvider GetMetadataProvider(ConnectionKey k) => null;
            public IReadOnlyList<TableInfo> GetTables(ConnectionKey k) => System.Array.Empty<TableInfo>();
            public IReadOnlyList<ColumnInfo> GetColumns(ConnectionKey k, string schema, string tableName) =>
                _columns.TryGetValue((schema + "." + tableName).ToLowerInvariant(), out var cols)
                    ? cols : System.Array.Empty<ColumnInfo>();
            public IReadOnlyList<ForeignKeyInfo> GetForeignKeys(ConnectionKey k, string schema, string tableName) => System.Array.Empty<ForeignKeyInfo>();
            public IReadOnlyList<ProcedureInfo> GetProcedures(ConnectionKey k) => System.Array.Empty<ProcedureInfo>();
            public IReadOnlyList<UserFunctionInfo> GetUserDefinedFunctions(ConnectionKey k) => System.Array.Empty<UserFunctionInfo>();
            public IReadOnlyList<string> GetSchemas(ConnectionKey k) => System.Array.Empty<string>();
            public void Invalidate(ConnectionKey k) { }
        }

        private static CompletionRequest BuildRequest(string sql, IDatabaseMetadata metadata) {
            var parseResult = Parser.Parse(sql);
            return new CompletionRequest(
                sql, 0, 1, 1,
                new ConnectionKey("srv|db"), parseResult, null,
                isDotContext: false, qualifier: null,
                isAfterFromKeyword: false, isJoinOnContext: false, isAfterJoinKeyword: false,
                isWhereContext: false, isAfterTableInFromJoin: false, tableNameBeforeCursor: null,
                snapshot: null);
        }

        private static ScopedColumnResolver Build(IDatabaseMetadata metadata) =>
            new ScopedColumnResolver(metadata, AliasExtractor, CteExtractor, CteColumnExtractor, DerivedTableExtractor);

        [TestMethod]
        public void ReturnsColumnsFromAliasedTable() {
            var metadata = new FakeMetadata();
            metadata.AddColumns("dbo", "Orders", "Id", "Total");
            var resolver = Build(metadata);
            var request  = BuildRequest("SELECT * FROM Orders o", metadata);

            var items = resolver.GetVisibleColumns(request);
            CollectionAssert.Contains(items.Select(i => i.DisplayText).ToList(), "Id");
            CollectionAssert.Contains(items.Select(i => i.DisplayText).ToList(), "Total");
        }

        [TestMethod]
        public void ReturnsColumnsFromCte() {
            var metadata = new FakeMetadata();
            var resolver  = Build(metadata);
            var request   = BuildRequest(
                "WITH cte AS (SELECT Id, Name FROM dbo.Users) SELECT * FROM cte", metadata);

            var items = resolver.GetVisibleColumns(request);
            CollectionAssert.Contains(items.Select(i => i.DisplayText).ToList(), "Id");
            CollectionAssert.Contains(items.Select(i => i.DisplayText).ToList(), "Name");
        }

        [TestMethod]
        public void ReturnsColumnsFromDerivedTable() {
            var metadata = new FakeMetadata();
            var resolver  = Build(metadata);
            var request   = BuildRequest(
                "SELECT * FROM (SELECT a, b FROM Orders) AS d", metadata);

            var items = resolver.GetVisibleColumns(request);
            CollectionAssert.Contains(items.Select(i => i.DisplayText).ToList(), "a");
            CollectionAssert.Contains(items.Select(i => i.DisplayText).ToList(), "b");
        }

        [TestMethod]
        public void DedupsAcrossSources() {
            var metadata = new FakeMetadata();
            metadata.AddColumns("dbo", "Orders", "Id");
            metadata.AddColumns("dbo", "Customers", "Id");
            var resolver = Build(metadata);
            var request  = BuildRequest("SELECT * FROM Orders o JOIN Customers c ON 1=1", metadata);

            var items = resolver.GetVisibleColumns(request);
            Assert.AreEqual(1, items.Count(i => i.DisplayText == "Id"));
        }

        [TestMethod]
        public void NoAliases_ReturnsEmpty() {
            var metadata = new FakeMetadata();
            var resolver  = Build(metadata);
            var request   = BuildRequest("SELECT 1", metadata);

            var items = resolver.GetVisibleColumns(request);
            Assert.AreEqual(0, items.Count);
        }
    }
}
