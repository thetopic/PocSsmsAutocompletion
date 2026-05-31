using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Collections.Generic;
using System.Linq;

namespace SsmsAutocompletion.Tests {

    [TestClass]
    public class TableCompletionProviderTests {

        private static CompletionRequest Make(
            bool isDotContext       = false,
            bool isAfterFromKeyword = false,
            ConnectionKey connectionKey = null) =>
            new CompletionRequest(
                sql: "SELECT * FROM ", caretPosition: 14, line: 1, column: 15,
                connectionKey: connectionKey ?? new ConnectionKey("Server|DB"),
                parseResult: null, metadataProvider: null,
                isDotContext: isDotContext, qualifier: null,
                isAfterFromKeyword: isAfterFromKeyword,
                isJoinOnContext: false, isAfterJoinKeyword: false,
                isWhereContext: false, isAfterTableInFromJoin: false,
                tableNameBeforeCursor: null, snapshot: null);

        private static IDatabaseMetadata MetaWith(params TableInfo[] tables) {
            var mock = new Mock<IDatabaseMetadata>();
            mock.Setup(m => m.GetTables(It.IsAny<ConnectionKey>()))
                .Returns(new List<TableInfo>(tables).AsReadOnly() as IReadOnlyList<TableInfo>);
            return mock.Object;
        }

        // ── Context gating ─────────────────────────────────────────────────────

        [TestMethod]
        public void DotContext_ReturnsEmpty() {
            var p = new TableCompletionProvider(MetaWith(new TableInfo("dbo", "Orders")));
            Assert.AreEqual(0, p.GetCompletions(Make(isDotContext: true)).Count);
        }

        [TestMethod]
        public void EmptyConnectionKey_ReturnsEmpty() {
            var p = new TableCompletionProvider(MetaWith(new TableInfo("dbo", "Orders")));
            Assert.AreEqual(0, p.GetCompletions(Make(connectionKey: new ConnectionKey(""))).Count);
        }

        // ── Table items ────────────────────────────────────────────────────────

        [TestMethod]
        public void Table_KindIsTable() {
            var p = new TableCompletionProvider(MetaWith(new TableInfo("dbo", "Orders", SqlObjectType.Table)));
            var item = p.GetCompletions(Make()).Single();
            Assert.AreEqual(CompletionItemKind.Table, item.Kind);
        }

        [TestMethod]
        public void Table_DescriptionIsTable() {
            var p = new TableCompletionProvider(MetaWith(new TableInfo("dbo", "Orders", SqlObjectType.Table)));
            var item = p.GetCompletions(Make()).Single();
            Assert.AreEqual("Table", item.Description);
        }

        // ── View items ─────────────────────────────────────────────────────────

        [TestMethod]
        public void View_KindIsView() {
            var p = new TableCompletionProvider(MetaWith(new TableInfo("dbo", "vwOrders", SqlObjectType.View)));
            var item = p.GetCompletions(Make()).Single();
            Assert.AreEqual(CompletionItemKind.View, item.Kind);
        }

        [TestMethod]
        public void View_DescriptionIsView() {
            var p = new TableCompletionProvider(MetaWith(new TableInfo("dbo", "vwOrders", SqlObjectType.View)));
            var item = p.GetCompletions(Make()).Single();
            Assert.AreEqual("View", item.Description);
        }

        [TestMethod]
        public void View_AppearsAlongsideTables() {
            var p = new TableCompletionProvider(MetaWith(
                new TableInfo("dbo", "Orders",   SqlObjectType.Table),
                new TableInfo("dbo", "vwOrders", SqlObjectType.View)));
            Assert.AreEqual(2, p.GetCompletions(Make()).Count);
        }

        // ── Insert text shape ──────────────────────────────────────────────────

        [TestMethod]
        public void AllItems_InsertText_EndsWithSpace() {
            var p = new TableCompletionProvider(MetaWith(
                new TableInfo("dbo", "Orders",   SqlObjectType.Table),
                new TableInfo("dbo", "vwOrders", SqlObjectType.View)));
            Assert.IsTrue(p.GetCompletions(Make()).All(i => i.InsertText.EndsWith(" ")));
        }

        [TestMethod]
        public void NonDboSchema_DisplayTextIncludesSchema() {
            var p = new TableCompletionProvider(MetaWith(
                new TableInfo("sales", "Orders", SqlObjectType.Table)));
            var item = p.GetCompletions(Make()).Single();
            StringAssert.Contains(item.DisplayText, "sales.Orders");
        }

        [TestMethod]
        public void DboSchema_DisplayTextOmitsSchema() {
            var p = new TableCompletionProvider(MetaWith(
                new TableInfo("dbo", "Orders", SqlObjectType.Table)));
            var item = p.GetCompletions(Make()).Single();
            Assert.AreEqual("Orders", item.DisplayText);
        }
    }
}
