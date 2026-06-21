using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SsmsAutocompletion.Tests {

    [TestClass]
    public class InsertColumnCompletionProviderTests {

        // ── Helpers ────────────────────────────────────────────────────────────

        private static ColumnInfo Col(string name, string type = "int") =>
            new ColumnInfo(name, type);

        private static IDatabaseMetadata MetaWith(string schema, string table, params ColumnInfo[] cols) {
            var mock = new Mock<IDatabaseMetadata>();
            mock.Setup(m => m.GetColumns(It.IsAny<ConnectionKey>(), schema, table))
                .Returns(new ReadOnlyCollection<ColumnInfo>(new List<ColumnInfo>(cols))
                    as IReadOnlyList<ColumnInfo>);
            return mock.Object;
        }

        private static CompletionRequest Make(
            string sql,
            int caretPosition,
            bool isInsertColumnList = true,
            string targetTable = "dbo.Orders",
            ConnectionKey key = null) =>
            new CompletionRequest(
                sql: sql, caretPosition: caretPosition, line: 1, column: caretPosition + 1,
                connectionKey: key ?? new ConnectionKey("S|DB"),
                parseResult: null, metadataProvider: null,
                isDotContext: false, qualifier: null,
                isAfterFromKeyword: false, isJoinOnContext: false, isAfterJoinKeyword: false,
                isWhereContext: false, isAfterTableInFromJoin: false,
                tableNameBeforeCursor: null, snapshot: null,
                isInsertColumnList: isInsertColumnList,
                insertUpdateTargetTable: isInsertColumnList ? targetTable : null);

        // ── Context gating ─────────────────────────────────────────────────────

        [TestMethod]
        public void NotInInsertContext_ReturnsEmpty() {
            var p = new InsertColumnCompletionProvider(MetaWith("dbo", "Orders", Col("ID")));
            var req = Make("SELECT *", 8, isInsertColumnList: false);
            Assert.AreEqual(0, p.GetCompletions(req).Count);
        }

        [TestMethod]
        public void EmptyConnectionKey_ReturnsEmpty() {
            var p = new InsertColumnCompletionProvider(MetaWith("dbo", "Orders", Col("ID")));
            var req = Make("INSERT INTO dbo.Orders (", 23, key: new ConnectionKey(""));
            Assert.AreEqual(0, p.GetCompletions(req).Count);
        }

        [TestMethod]
        public void CacheMiss_ReturnsEmpty() {
            var meta = new Mock<IDatabaseMetadata>();
            meta.Setup(m => m.GetColumns(It.IsAny<ConnectionKey>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(System.Array.Empty<ColumnInfo>());
            var p   = new InsertColumnCompletionProvider(meta.Object);
            var req = Make("INSERT INTO dbo.Orders (", 23);
            Assert.AreEqual(0, p.GetCompletions(req).Count);
        }

        // ── All columns shown on empty list ────────────────────────────────────

        [TestMethod]
        public void EmptyColumnList_ShowsAllColumns() {
            var p = new InsertColumnCompletionProvider(
                MetaWith("dbo", "Orders", Col("CustomerID"), Col("OrderDate")));
            var sql = "INSERT INTO dbo.Orders (";
            var req = Make(sql, sql.Length);
            // 2 individual columns + 1 synthetic full-template item
            Assert.AreEqual(3, p.GetCompletions(req).Count);
        }

        [TestMethod]
        public void EmptyColumnList_IncludesFullTemplateItem() {
            var p = new InsertColumnCompletionProvider(
                MetaWith("dbo", "Orders", Col("CustomerID"), Col("OrderDate")));
            var sql = "INSERT INTO dbo.Orders (";
            var result = p.GetCompletions(Make(sql, sql.Length));
            var template = result.Single(i => i.InsertText.Contains("VALUES"));
            Assert.AreEqual("CustomerID, OrderDate) VALUES (@CustomerID, @OrderDate)", template.InsertText);
        }

        [TestMethod]
        public void PartiallyTypedColumnList_NoTemplateItem() {
            var p = new InsertColumnCompletionProvider(
                MetaWith("dbo", "Orders", Col("CustomerID"), Col("OrderDate")));
            string sql = "INSERT INTO dbo.Orders (CustomerID, ";
            var result = p.GetCompletions(Make(sql, sql.Length));
            Assert.IsFalse(result.Any(i => i.InsertText.Contains("VALUES")));
        }

        [TestMethod]
        public void Items_HaveColumnKind() {
            var p   = new InsertColumnCompletionProvider(MetaWith("dbo", "Orders", Col("ID")));
            var sql = "INSERT INTO dbo.Orders (";
            var item = p.GetCompletions(Make(sql, sql.Length)).Single(i => i.DisplayText == "ID");
            Assert.AreEqual(CompletionItemKind.Column, item.Kind);
        }

        // ── Already-listed columns are excluded ────────────────────────────────

        [TestMethod]
        public void AlreadyListedColumn_IsExcluded() {
            var p   = new InsertColumnCompletionProvider(
                MetaWith("dbo", "Orders", Col("CustomerID"), Col("OrderDate")));
            string sql = "INSERT INTO dbo.Orders (CustomerID, ";
            var result = p.GetCompletions(Make(sql, sql.Length));
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("OrderDate", result[0].DisplayText);
        }

        [TestMethod]
        public void AlreadyListedCaseInsensitive_Excluded() {
            var p   = new InsertColumnCompletionProvider(
                MetaWith("dbo", "Orders", Col("CustomerID")));
            string sql = "INSERT INTO dbo.Orders (customerid, ";
            Assert.AreEqual(0, p.GetCompletions(Make(sql, sql.Length)).Count);
        }

        // ── Schema-qualified target table ──────────────────────────────────────

        [TestMethod]
        public void NonDboSchema_TableLookedUpCorrectly() {
            var p   = new InsertColumnCompletionProvider(MetaWith("hr", "Employees", Col("Name")));
            var sql = "INSERT INTO hr.Employees (";
            var result = p.GetCompletions(Make(sql, sql.Length, targetTable: "hr.Employees"));
            Assert.AreEqual(1, result.Count(i => i.Kind == CompletionItemKind.Column && i.DisplayText == "Name"));
        }
    }
}
