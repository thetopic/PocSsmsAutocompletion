using Microsoft.SqlServer.Management.SqlParser.Parser;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Collections.Generic;
using System.Linq;

namespace SsmsAutocompletion.Tests {

    [TestClass]
    public class InlineJoinCompletionProviderTests {

        // ── Test helpers ───────────────────────────────────────────────────────

        private static ForeignKeyInfo Fk(
            string fkSchema, string fkTable, string[] fkCols,
            string refSchema, string refTable, string[] refCols) =>
            new ForeignKeyInfo(fkSchema, fkTable, fkCols, refSchema, refTable, refCols);

        private static IDatabaseMetadata MetaWith(
            string schema, string table, params ForeignKeyInfo[] fks) {
            var mock = new Mock<IDatabaseMetadata>();
            mock.Setup(m => m.GetForeignKeys(
                    It.IsAny<ConnectionKey>(),
                    It.Is<string>(s => s.ToLower() == schema.ToLower()),
                    It.Is<string>(t => t.ToLower() == table.ToLower())))
                .Returns(new List<ForeignKeyInfo>(fks).AsReadOnly() as IReadOnlyList<ForeignKeyInfo>);
            return mock.Object;
        }

        private static IAliasExtractor ExtractorWith(
            params (string alias, string schema, string table)[] entries) {
            var dict = new Dictionary<string, TableInfo>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var (alias, schema, table) in entries)
                dict[alias] = new TableInfo(schema, table);
            var mock = new Mock<IAliasExtractor>();
            mock.Setup(e => e.Extract(It.IsAny<ParseResult>())).Returns(dict);
            return mock.Object;
        }

        private static CompletionRequest Request(
            bool isDotContext            = false,
            bool isAfterTableInFromJoin  = true,
            string tableNameBefore       = "Orders",
            ConnectionKey connectionKey  = null) =>
            new CompletionRequest(
                sql: "SELECT * FROM Orders ", caretPosition: 20,
                line: 1, column: 21,
                connectionKey: connectionKey ?? new ConnectionKey("Server|DB"),
                parseResult: null, metadataProvider: null,
                isDotContext: isDotContext, qualifier: null,
                isAfterFromKeyword: false,
                isJoinOnContext: false, isAfterJoinKeyword: false,
                isWhereContext: false,
                isAfterTableInFromJoin: isAfterTableInFromJoin,
                tableNameBeforeCursor: tableNameBefore,
                snapshot: null);

        // ── Context gating ─────────────────────────────────────────────────────

        [TestMethod]
        public void DotContext_ReturnsEmpty() {
            var p = new InlineJoinCompletionProvider(
                MetaWith("dbo", "Orders"),
                ExtractorWith(("orders", "dbo", "Orders")));
            Assert.AreEqual(0, p.GetCompletions(Request(isDotContext: true)).Count);
        }

        [TestMethod]
        public void NotAfterTableContext_ReturnsEmpty() {
            var p = new InlineJoinCompletionProvider(
                MetaWith("dbo", "Orders"),
                ExtractorWith(("orders", "dbo", "Orders")));
            Assert.AreEqual(0, p.GetCompletions(Request(isAfterTableInFromJoin: false)).Count);
        }

        [TestMethod]
        public void EmptyConnectionKey_ReturnsEmpty() {
            var p = new InlineJoinCompletionProvider(
                MetaWith("dbo", "Orders"),
                ExtractorWith(("orders", "dbo", "Orders")));
            Assert.AreEqual(0, p.GetCompletions(Request(connectionKey: new ConnectionKey(""))).Count);
        }

        [TestMethod]
        public void NullTableNameBefore_ReturnsEmpty() {
            var p = new InlineJoinCompletionProvider(
                MetaWith("dbo", "Orders"),
                ExtractorWith(("orders", "dbo", "Orders")));
            Assert.AreEqual(0, p.GetCompletions(Request(tableNameBefore: null)).Count);
        }

        [TestMethod]
        public void TableNotInAliasMap_ReturnsEmpty() {
            var p = new InlineJoinCompletionProvider(
                MetaWith("dbo", "Orders"),
                ExtractorWith(/* empty map */));
            Assert.AreEqual(0, p.GetCompletions(Request()).Count);
        }

        [TestMethod]
        public void NoForeignKeys_ReturnsEmpty() {
            var p = new InlineJoinCompletionProvider(
                MetaWith("dbo", "Orders" /* no FKs */),
                ExtractorWith(("orders", "dbo", "Orders")));
            Assert.AreEqual(0, p.GetCompletions(Request()).Count);
        }

        // ── FK owner direction (Orders.CustomerId → Customers.CustomerId) ──────

        [TestMethod]
        public void FkOwner_ReturnsOneSuggestion() {
            var fk = Fk("dbo", "Orders", new[] { "CustomerId" },
                        "dbo", "Customers", new[] { "CustomerId" });
            var p = new InlineJoinCompletionProvider(
                MetaWith("dbo", "Orders", fk),
                ExtractorWith(("orders", "dbo", "Orders")));
            Assert.AreEqual(1, p.GetCompletions(Request()).Count);
        }

        [TestMethod]
        public void FkOwner_DisplayStartsWithJoin() {
            var fk = Fk("dbo", "Orders", new[] { "CustomerId" },
                        "dbo", "Customers", new[] { "CustomerId" });
            var p = new InlineJoinCompletionProvider(
                MetaWith("dbo", "Orders", fk),
                ExtractorWith(("orders", "dbo", "Orders")));
            var item = p.GetCompletions(Request())[0];
            Assert.IsTrue(item.DisplayText.StartsWith("JOIN "),
                $"Expected 'JOIN ...', got: {item.DisplayText}");
        }

        [TestMethod]
        public void FkOwner_DisplayContainsRelatedTable() {
            var fk = Fk("dbo", "Orders", new[] { "CustomerId" },
                        "dbo", "Customers", new[] { "CustomerId" });
            var p = new InlineJoinCompletionProvider(
                MetaWith("dbo", "Orders", fk),
                ExtractorWith(("orders", "dbo", "Orders")));
            var item = p.GetCompletions(Request())[0];
            StringAssert.Contains(item.DisplayText, "Customers");
        }

        [TestMethod]
        public void FkOwner_DisplayContainsOnKeyword() {
            var fk = Fk("dbo", "Orders", new[] { "CustomerId" },
                        "dbo", "Customers", new[] { "CustomerId" });
            var p = new InlineJoinCompletionProvider(
                MetaWith("dbo", "Orders", fk),
                ExtractorWith(("orders", "dbo", "Orders")));
            var item = p.GetCompletions(Request())[0];
            StringAssert.Contains(item.DisplayText, " ON ");
        }

        [TestMethod]
        public void FkOwner_DisplayContainsFkColumnName() {
            var fk = Fk("dbo", "Orders", new[] { "CustomerId" },
                        "dbo", "Customers", new[] { "CustomerId" });
            var p = new InlineJoinCompletionProvider(
                MetaWith("dbo", "Orders", fk),
                ExtractorWith(("orders", "dbo", "Orders")));
            var item = p.GetCompletions(Request())[0];
            StringAssert.Contains(item.DisplayText, "CustomerId");
        }

        [TestMethod]
        public void FkOwner_KindIsJoin() {
            var fk = Fk("dbo", "Orders", new[] { "CustomerId" },
                        "dbo", "Customers", new[] { "CustomerId" });
            var p = new InlineJoinCompletionProvider(
                MetaWith("dbo", "Orders", fk),
                ExtractorWith(("orders", "dbo", "Orders")));
            var item = p.GetCompletions(Request())[0];
            Assert.AreEqual(CompletionItemKind.Join, item.Kind);
        }

        // ── FK referenced direction (Customers ← Orders.CustomerId) ──────────

        [TestMethod]
        public void FkReferenced_ReturnsOneSuggestion() {
            // Query has Customers; FK says Orders references Customers
            var fk = Fk("dbo", "Orders", new[] { "CustomerId" },
                        "dbo", "Customers", new[] { "CustomerId" });
            var p = new InlineJoinCompletionProvider(
                MetaWith("dbo", "Customers", fk),
                ExtractorWith(("customers", "dbo", "Customers")));
            Assert.AreEqual(1, p.GetCompletions(Request(tableNameBefore: "Customers")).Count);
        }

        [TestMethod]
        public void FkReferenced_DisplayContainsFkTable() {
            var fk = Fk("dbo", "Orders", new[] { "CustomerId" },
                        "dbo", "Customers", new[] { "CustomerId" });
            var p = new InlineJoinCompletionProvider(
                MetaWith("dbo", "Customers", fk),
                ExtractorWith(("customers", "dbo", "Customers")));
            var item = p.GetCompletions(Request(tableNameBefore: "Customers"))[0];
            StringAssert.Contains(item.DisplayText, "Orders");
        }

        // ── Composite FK ──────────────────────────────────────────────────────

        [TestMethod]
        public void CompositeFk_OnClauseContainsBothColumns() {
            var fk = Fk("dbo", "OrderDetails", new[] { "OrderId", "ProductId" },
                        "dbo", "Orders", new[] { "OrderId", "ProductId" });
            var p = new InlineJoinCompletionProvider(
                MetaWith("dbo", "OrderDetails", fk),
                ExtractorWith(("orderdetails", "dbo", "OrderDetails")));
            var item = p.GetCompletions(Request(tableNameBefore: "OrderDetails"))[0];
            StringAssert.Contains(item.DisplayText, "OrderId");
            StringAssert.Contains(item.DisplayText, "ProductId");
        }

        // ── Deduplication / exclusion ─────────────────────────────────────────

        [TestMethod]
        public void RelatedTableAlreadyInQuery_NotSuggested() {
            var fk = Fk("dbo", "Orders", new[] { "CustomerId" },
                        "dbo", "Customers", new[] { "CustomerId" });
            // Customers is already in the alias map
            var p = new InlineJoinCompletionProvider(
                MetaWith("dbo", "Orders", fk),
                ExtractorWith(
                    ("orders", "dbo", "Orders"),
                    ("c",      "dbo", "Customers")));
            Assert.AreEqual(0, p.GetCompletions(Request()).Count);
        }

        [TestMethod]
        public void GeneratedAlias_AvoidsExistingAlias() {
            // "c" is already taken → alias for Customers should not be "c"
            var fk = Fk("dbo", "Orders", new[] { "CartId" },
                        "dbo", "Cart", new[] { "CartId" });
            var p = new InlineJoinCompletionProvider(
                MetaWith("dbo", "Orders", fk),
                ExtractorWith(
                    ("orders", "dbo", "Orders"),
                    ("c",      "dbo", "Categories")));  // "c" is taken
            var item = p.GetCompletions(Request())[0];
            // Alias for Cart should be "c2", not "c"
            Assert.IsFalse(item.DisplayText.Contains("JOIN Cart c ON"),
                $"Alias 'c' should be avoided; got: {item.DisplayText}");
            StringAssert.Contains(item.DisplayText, "c2");
        }

        // ── Multiple FK relationships ─────────────────────────────────────────

        [TestMethod]
        public void MultipleFks_ReturnsSuggestionForEach() {
            var fk1 = Fk("dbo", "Orders", new[] { "CustomerId" },
                         "dbo", "Customers", new[] { "CustomerId" });
            var fk2 = Fk("dbo", "Orders", new[] { "ShipperId" },
                         "dbo", "Shippers", new[] { "ShipperId" });
            var mock = new Mock<IDatabaseMetadata>();
            mock.Setup(m => m.GetForeignKeys(It.IsAny<ConnectionKey>(), "dbo", "Orders"))
                .Returns(new List<ForeignKeyInfo> { fk1, fk2 }.AsReadOnly() as IReadOnlyList<ForeignKeyInfo>);
            var p = new InlineJoinCompletionProvider(
                mock.Object,
                ExtractorWith(("orders", "dbo", "Orders")));
            Assert.AreEqual(2, p.GetCompletions(Request()).Count);
        }
    }
}
