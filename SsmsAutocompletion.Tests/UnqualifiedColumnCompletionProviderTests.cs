using Microsoft.SqlServer.Management.SqlParser.Parser;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SsmsAutocompletion.Tests {

    [TestClass]
    public class UnqualifiedColumnCompletionProviderTests {

        // ── Helpers ────────────────────────────────────────────────────────────

        private static ColumnInfo Col(string name, string type = "nvarchar") =>
            new ColumnInfo(name, type);

        private static TableInfo T(string schema, string name) => new TableInfo(schema, name);

        private static CompletionRequest Make(
            bool isDotContext        = false,
            bool isInSelectList     = true,
            bool isWhereContext     = false,
            ConnectionKey key       = null,
            ParseResult parseResult = null) =>
            new CompletionRequest(
                sql: "SELECT ", caretPosition: 7, line: 1, column: 8,
                connectionKey: key ?? new ConnectionKey("S|DB"),
                parseResult: parseResult, metadataProvider: null,
                isDotContext: isDotContext, qualifier: null,
                isAfterFromKeyword: false, isJoinOnContext: false, isAfterJoinKeyword: false,
                isWhereContext: isWhereContext, isAfterTableInFromJoin: false,
                tableNameBeforeCursor: null, snapshot: null,
                isInSelectList: isInSelectList);

        private static UnqualifiedColumnCompletionProvider MakeProvider(
            IDatabaseMetadata meta = null, IAliasExtractor aliasExtractor = null) =>
            new UnqualifiedColumnCompletionProvider(
                meta ?? new Mock<IDatabaseMetadata>().Object,
                aliasExtractor ?? new Mock<IAliasExtractor>().Object);

        private static IAliasExtractor AliasMapWith(
            params (string alias, TableInfo table)[] entries) {
            var mock = new Mock<IAliasExtractor>();
            var map  = new Dictionary<string, TableInfo>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var (alias, table) in entries) map[alias] = table;
            mock.Setup(e => e.Extract(It.IsAny<ParseResult>())).Returns(map);
            mock.Setup(e => e.ExtractInScope(It.IsAny<ParseResult>(), It.IsAny<int>(), It.IsAny<int>())).Returns(map);
            return mock.Object;
        }

        // ── Context gating ─────────────────────────────────────────────────────

        [TestMethod]
        public void DotContext_ReturnsEmpty() {
            var p = MakeProvider();
            Assert.AreEqual(0, p.GetCompletions(Make(isDotContext: true, isInSelectList: true)).Count);
        }

        [TestMethod]
        public void NeitherSelectNorWhere_ReturnsEmpty() {
            var p = MakeProvider();
            Assert.AreEqual(0, p.GetCompletions(Make(isInSelectList: false, isWhereContext: false)).Count);
        }

        [TestMethod]
        public void EmptyConnectionKey_ReturnsEmpty() {
            var p = MakeProvider();
            Assert.AreEqual(0, p.GetCompletions(Make(key: new ConnectionKey(""))).Count);
        }

        // ── SELECT list: returns columns from FROM tables ─────────────────────

        [TestMethod]
        public void SelectList_SingleTable_ReturnsColumns() {
            var meta = new Mock<IDatabaseMetadata>();
            meta.Setup(m => m.GetColumns(It.IsAny<ConnectionKey>(), "dbo", "Customers"))
                .Returns(new ReadOnlyCollection<ColumnInfo>(
                    new List<ColumnInfo> { Col("FirstName"), Col("LastName") })
                    as IReadOnlyList<ColumnInfo>);
            var alias = AliasMapWith(("c", T("dbo", "Customers")));
            var p = MakeProvider(meta.Object, alias);
            var result = p.GetCompletions(Make(isInSelectList: true));
            Assert.AreEqual(2, result.Count);
        }

        [TestMethod]
        public void SelectList_MultipleTables_ColumnsMerged() {
            var meta = new Mock<IDatabaseMetadata>();
            meta.Setup(m => m.GetColumns(It.IsAny<ConnectionKey>(), "dbo", "Orders"))
                .Returns(new ReadOnlyCollection<ColumnInfo>(new List<ColumnInfo> { Col("OrderID") })
                    as IReadOnlyList<ColumnInfo>);
            meta.Setup(m => m.GetColumns(It.IsAny<ConnectionKey>(), "dbo", "Customers"))
                .Returns(new ReadOnlyCollection<ColumnInfo>(new List<ColumnInfo> { Col("CustomerID") })
                    as IReadOnlyList<ColumnInfo>);
            var alias = AliasMapWith(
                ("o", T("dbo", "Orders")),
                ("c", T("dbo", "Customers")));
            var p = MakeProvider(meta.Object, alias);
            var result = p.GetCompletions(Make(isInSelectList: true));
            Assert.AreEqual(2, result.Count);
        }

        [TestMethod]
        public void SelectList_DuplicateColumnName_DeduplicatedByDisplayText() {
            var meta = new Mock<IDatabaseMetadata>();
            meta.Setup(m => m.GetColumns(It.IsAny<ConnectionKey>(), "dbo", "Orders"))
                .Returns(new ReadOnlyCollection<ColumnInfo>(new List<ColumnInfo> { Col("Status") })
                    as IReadOnlyList<ColumnInfo>);
            meta.Setup(m => m.GetColumns(It.IsAny<ConnectionKey>(), "dbo", "Products"))
                .Returns(new ReadOnlyCollection<ColumnInfo>(new List<ColumnInfo> { Col("Status") })
                    as IReadOnlyList<ColumnInfo>);
            var alias = AliasMapWith(
                ("o", T("dbo", "Orders")),
                ("p", T("dbo", "Products")));
            var p = MakeProvider(meta.Object, alias);
            var result = p.GetCompletions(Make(isInSelectList: true));
            Assert.AreEqual(1, result.Count, "Duplicate column name should be deduplicated");
        }

        // ── WHERE clause ──────────────────────────────────────────────────────

        [TestMethod]
        public void WhereClause_ReturnsColumnsFromFromTables() {
            var meta = new Mock<IDatabaseMetadata>();
            meta.Setup(m => m.GetColumns(It.IsAny<ConnectionKey>(), "dbo", "Orders"))
                .Returns(new ReadOnlyCollection<ColumnInfo>(new List<ColumnInfo> { Col("Status") })
                    as IReadOnlyList<ColumnInfo>);
            var alias = AliasMapWith(("o", T("dbo", "Orders")));
            var p = MakeProvider(meta.Object, alias);
            var result = p.GetCompletions(Make(isInSelectList: false, isWhereContext: true));
            Assert.AreEqual(1, result.Count);
        }

        // ── Cache miss ────────────────────────────────────────────────────────

        [TestMethod]
        public void CacheMissForOneTable_OtherTableStillReturned() {
            var meta = new Mock<IDatabaseMetadata>();
            meta.Setup(m => m.GetColumns(It.IsAny<ConnectionKey>(), "dbo", "Known"))
                .Returns(new ReadOnlyCollection<ColumnInfo>(new List<ColumnInfo> { Col("Id") })
                    as IReadOnlyList<ColumnInfo>);
            meta.Setup(m => m.GetColumns(It.IsAny<ConnectionKey>(), "dbo", "Unknown"))
                .Returns(System.Array.Empty<ColumnInfo>());
            var alias = AliasMapWith(
                ("a", T("dbo", "Known")),
                ("b", T("dbo", "Unknown")));
            var p = MakeProvider(meta.Object, alias);
            var result = p.GetCompletions(Make(isInSelectList: true));
            Assert.AreEqual(1, result.Count, "Known table columns should still appear when other table misses cache");
        }

        // ── Items shape ───────────────────────────────────────────────────────

        [TestMethod]
        public void Items_HaveColumnKind() {
            var meta = new Mock<IDatabaseMetadata>();
            meta.Setup(m => m.GetColumns(It.IsAny<ConnectionKey>(), "dbo", "T"))
                .Returns(new ReadOnlyCollection<ColumnInfo>(new List<ColumnInfo> { Col("ID") })
                    as IReadOnlyList<ColumnInfo>);
            var alias = AliasMapWith(("t", T("dbo", "T")));
            var p = MakeProvider(meta.Object, alias);
            var item = p.GetCompletions(Make(isInSelectList: true)).Single();
            Assert.AreEqual(CompletionItemKind.Column, item.Kind);
        }
    }
}
