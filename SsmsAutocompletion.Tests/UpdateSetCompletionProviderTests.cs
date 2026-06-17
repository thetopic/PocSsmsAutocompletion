using Microsoft.SqlServer.Management.SqlParser.Parser;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SsmsAutocompletion.Tests {

    [TestClass]
    public class UpdateSetCompletionProviderTests {

        // ── Helpers ────────────────────────────────────────────────────────────

        private static ColumnInfo Col(string name, string type = "nvarchar") =>
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
            bool isUpdateSetClause = true,
            string targetTable = "dbo.Orders",
            ConnectionKey key = null,
            ParseResult parseResult = null) =>
            new CompletionRequest(
                sql: sql, caretPosition: caretPosition, line: 1, column: caretPosition + 1,
                connectionKey: key ?? new ConnectionKey("S|DB"),
                parseResult: parseResult, metadataProvider: null,
                isDotContext: false, qualifier: null,
                isAfterFromKeyword: false, isJoinOnContext: false, isAfterJoinKeyword: false,
                isWhereContext: false, isAfterTableInFromJoin: false,
                tableNameBeforeCursor: null, snapshot: null,
                isUpdateSetClause: isUpdateSetClause,
                insertUpdateTargetTable: isUpdateSetClause ? targetTable : null);

        // ── Context gating ─────────────────────────────────────────────────────

        [TestMethod]
        public void NotInUpdateSetContext_ReturnsEmpty() {
            var p = new UpdateSetCompletionProvider(
                MetaWith("dbo", "Orders", Col("Status")),
                new AliasExtractor());
            var req = Make("SELECT *", 8, isUpdateSetClause: false);
            Assert.AreEqual(0, p.GetCompletions(req).Count);
        }

        [TestMethod]
        public void EmptyConnectionKey_ReturnsEmpty() {
            var p = new UpdateSetCompletionProvider(
                MetaWith("dbo", "Orders", Col("Status")),
                new AliasExtractor());
            var req = Make("UPDATE dbo.Orders SET ", 21, key: new ConnectionKey(""));
            Assert.AreEqual(0, p.GetCompletions(req).Count);
        }

        [TestMethod]
        public void CacheMiss_ReturnsEmpty() {
            var meta = new Mock<IDatabaseMetadata>();
            meta.Setup(m => m.GetColumns(It.IsAny<ConnectionKey>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(System.Array.Empty<ColumnInfo>());
            var p   = new UpdateSetCompletionProvider(meta.Object, new AliasExtractor());
            var req = Make("UPDATE dbo.Orders SET ", 21);
            Assert.AreEqual(0, p.GetCompletions(req).Count);
        }

        // ── Columns shown after SET ───────────────────────────────────────────

        [TestMethod]
        public void AllColumnsShownAfterSet() {
            var p = new UpdateSetCompletionProvider(
                MetaWith("dbo", "Orders", Col("Status"), Col("OrderDate")),
                new AliasExtractor());
            var sql = "UPDATE dbo.Orders SET ";
            var result = p.GetCompletions(Make(sql, sql.Length));
            Assert.AreEqual(2, result.Count);
        }

        [TestMethod]
        public void Items_InsertTextEndsWithEquals() {
            var p = new UpdateSetCompletionProvider(
                MetaWith("dbo", "Orders", Col("Status")),
                new AliasExtractor());
            var sql  = "UPDATE dbo.Orders SET ";
            var item = p.GetCompletions(Make(sql, sql.Length)).Single();
            Assert.AreEqual("Status = ", item.InsertText);
        }

        [TestMethod]
        public void Items_HaveColumnKind() {
            var p = new UpdateSetCompletionProvider(
                MetaWith("dbo", "Orders", Col("Status")),
                new AliasExtractor());
            var sql  = "UPDATE dbo.Orders SET ";
            var item = p.GetCompletions(Make(sql, sql.Length)).Single();
            Assert.AreEqual(CompletionItemKind.Column, item.Kind);
        }

        // ── Already-assigned columns are excluded ─────────────────────────────

        [TestMethod]
        public void AlreadyAssignedColumn_Excluded() {
            var p = new UpdateSetCompletionProvider(
                MetaWith("dbo", "Orders", Col("Status"), Col("OrderDate")),
                new AliasExtractor());
            string sql = "UPDATE dbo.Orders SET Status = 'A', ";
            var result = p.GetCompletions(Make(sql, sql.Length));
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("OrderDate", result[0].DisplayText);
        }

        [TestMethod]
        public void AlreadyAssignedCaseInsensitive_Excluded() {
            var p = new UpdateSetCompletionProvider(
                MetaWith("dbo", "Orders", Col("Status")),
                new AliasExtractor());
            string sql = "UPDATE dbo.Orders SET status = 'A', ";
            Assert.AreEqual(0, p.GetCompletions(Make(sql, sql.Length)).Count);
        }
    }
}
