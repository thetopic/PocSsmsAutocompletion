using Microsoft.SqlServer.Management.SqlParser.Parser;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SsmsAutocompletion.Tests.Helpers;

namespace SsmsAutocompletion.Tests {

    [TestClass]
    public class SimilarColumnJoinCompletionProviderTests {

        // ── Helpers ────────────────────────────────────────────────────────────

        private static TableInfo T(string schema, string name) => new TableInfo(schema, name);

        private static ColumnInfo Col(string name) => new ColumnInfo(name, "int");

        private static ForeignKeyInfo Fk(
            string fkSchema, string fkTable, string[] fkCols,
            string refSchema, string refTable, string[] refCols) =>
            new ForeignKeyInfo(fkSchema, fkTable, new ReadOnlyCollection<string>(new List<string>(fkCols)),
                               refSchema, refTable, new ReadOnlyCollection<string>(new List<string>(refCols)));

        private static readonly SsmsSqlParser Parser = new SsmsSqlParser();

        private static CompletionRequest MakeRequest(
            ParseResult parseResult,
            bool isDotContext = false,
            bool isJoinOnContext = true,
            bool isWhereContext = false,
            string connectionKey = "Server|DB") =>
            new CompletionRequest(
                sql: "SELECT * FROM A a JOIN B b ON ",
                caretPosition: 30, line: 1, column: 31,
                connectionKey: new ConnectionKey(connectionKey),
                parseResult: parseResult, metadataProvider: null,
                isDotContext: isDotContext, qualifier: null,
                isAfterFromKeyword: false,
                isJoinOnContext: isJoinOnContext, isAfterJoinKeyword: false,
                isWhereContext: isWhereContext, isAfterTableInFromJoin: false,
                tableNameBeforeCursor: null, snapshot: null);

        // Sets up a mock where aliasMap returns {a→TableA, b→TableB}
        private static (SimilarColumnJoinCompletionProvider provider, ParseResult parseResult)
            MakeProvider(
                ColumnInfo[] colsA, ColumnInfo[] colsB,
                ForeignKeyInfo[] fksForA = null) {

            const string sql = "SELECT * FROM A a JOIN B b ON ";
            var parseResult = Parser.Parse(sql);

            var meta = new Mock<IDatabaseMetadata>();
            meta.Setup(m => m.GetColumns(It.IsAny<ConnectionKey>(), "dbo", "A"))
                .Returns(new ReadOnlyCollection<ColumnInfo>(new List<ColumnInfo>(colsA)));
            meta.Setup(m => m.GetColumns(It.IsAny<ConnectionKey>(), "dbo", "B"))
                .Returns(new ReadOnlyCollection<ColumnInfo>(new List<ColumnInfo>(colsB)));
            meta.Setup(m => m.GetForeignKeys(It.IsAny<ConnectionKey>(), "dbo", "A"))
                .Returns(new ReadOnlyCollection<ForeignKeyInfo>(
                    new List<ForeignKeyInfo>(fksForA ?? new ForeignKeyInfo[0])));
            meta.Setup(m => m.GetForeignKeys(It.IsAny<ConnectionKey>(), "dbo", "B"))
                .Returns(new ReadOnlyCollection<ForeignKeyInfo>(new List<ForeignKeyInfo>()));

            var aliasExt = new Mock<IAliasExtractor>();
            var map = new Dictionary<string, TableInfo>(System.StringComparer.OrdinalIgnoreCase) {
                ["a"] = T("dbo", "A"),
                ["b"] = T("dbo", "B")
            };
            aliasExt.Setup(e => e.Extract(It.IsAny<ParseResult>()))
                    .Returns(map);

            return (new SimilarColumnJoinCompletionProvider(meta.Object, aliasExt.Object), parseResult);
        }

        // ── Behaviour guard (unchanged from existing) ─────────────────────────

        [TestMethod]
        public void DotContext_ReturnsEmpty() {
            var (p, pr) = MakeProvider(new[] { Col("ID") }, new[] { Col("ID") });
            var req = MakeRequest(pr, isDotContext: true);
            Assert.AreEqual(0, p.GetCompletions(req).Count);
        }

        [TestMethod]
        public void NotInJoinOrWhere_ReturnsEmpty() {
            var (p, pr) = MakeProvider(new[] { Col("ID") }, new[] { Col("ID") });
            var req = MakeRequest(pr, isJoinOnContext: false, isWhereContext: false);
            Assert.AreEqual(0, p.GetCompletions(req).Count);
        }

        // ── FK exclusion ──────────────────────────────────────────────────────

        [TestMethod]
        public void FkCoveredPair_IsExcluded() {
            // A.CustomerID → FK → B.CustomerID
            var fk = Fk("dbo", "A", new[] { "CustomerID" }, "dbo", "B", new[] { "CustomerID" });
            var (p, pr) = MakeProvider(
                new[] { Col("CustomerID") },
                new[] { Col("CustomerID") },
                fksForA: new[] { fk });
            var req = MakeRequest(pr);
            Assert.AreEqual(0, p.GetCompletions(req).Count,
                "FK-covered pair should not be suggested by SimilarColumnJoinCompletionProvider");
        }

        [TestMethod]
        public void FkCoveredPairReverseDirection_IsExcluded() {
            // B.OrderID → FK → A.OrderID (reverse: B owns the FK, A has the PK)
            var fk = Fk("dbo", "B", new[] { "OrderID" }, "dbo", "A", new[] { "OrderID" });
            var (p, pr) = MakeProvider(
                new[] { Col("OrderID") },
                new[] { Col("OrderID") },
                fksForA: new[] { fk });
            var req = MakeRequest(pr);
            Assert.AreEqual(0, p.GetCompletions(req).Count,
                "Reverse-direction FK-covered pair should be excluded");
        }

        [TestMethod]
        public void CompositeFkPair_EachColumnExcluded() {
            // Composite FK: A.(Col1, Col2) → B.(Col1, Col2)
            var fk = Fk("dbo", "A", new[] { "Col1", "Col2" }, "dbo", "B", new[] { "Col1", "Col2" });
            var (p, pr) = MakeProvider(
                new[] { Col("Col1"), Col("Col2") },
                new[] { Col("Col1"), Col("Col2") },
                fksForA: new[] { fk });
            var req = MakeRequest(pr);
            Assert.AreEqual(0, p.GetCompletions(req).Count,
                "All columns covered by composite FK should be excluded");
        }

        [TestMethod]
        public void SimilarPairWithNoFk_IsSuggested() {
            // No FK between the tables
            var (p, pr) = MakeProvider(
                new[] { Col("Status") },
                new[] { Col("Status") });
            var req = MakeRequest(pr);
            var result = p.GetCompletions(req);
            Assert.AreEqual(1, result.Count);
        }

        // ── Rank ──────────────────────────────────────────────────────────────

        [TestMethod]
        public void SuggestedItems_HaveRank2() {
            var (p, pr) = MakeProvider(
                new[] { Col("Status") },
                new[] { Col("Status") });
            var result = p.GetCompletions(MakeRequest(pr));
            Assert.AreEqual(2, result[0].Rank,
                "SimilarColumnJoinCompletionProvider items must carry Rank=2");
        }
    }
}
