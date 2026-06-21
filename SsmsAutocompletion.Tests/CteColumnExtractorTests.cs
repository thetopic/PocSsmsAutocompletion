using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace SsmsAutocompletion.Tests {

    [TestClass]
    public class CteColumnExtractorTests {

        private static readonly SsmsSqlParser     Parser    = new SsmsSqlParser();
        private static readonly CteColumnExtractor Extractor = new CteColumnExtractor();

        private System.Collections.Generic.IReadOnlyList<string> Extract(string sql, string cte) =>
            Extractor.ExtractColumns(Parser.Parse(sql), cte);

        // ── Explicit column list ───────────────────────────────────────────────

        [TestMethod]
        public void ExplicitColumns_TwoColumns() {
            var cols = Extract(
                "WITH cte(col1, col2) AS (SELECT 1, 2) SELECT * FROM cte", "cte");
            Assert.AreEqual(2, cols.Count);
            Assert.AreEqual("col1", cols[0]);
            Assert.AreEqual("col2", cols[1]);
        }

        [TestMethod]
        public void ExplicitColumns_ThreeColumns() {
            var cols = Extract(
                "WITH cte(id, name, age) AS (SELECT 1, 'a', 30) SELECT * FROM cte", "cte");
            CollectionAssert.AreEqual(new[] { "id", "name", "age" }, cols.ToList());
        }

        // ── Derived from SELECT clause ─────────────────────────────────────────

        [TestMethod]
        public void DerivedColumns_PlainColumnRefs() {
            var cols = Extract(
                "WITH cte AS (SELECT Id, Name FROM dbo.Users) SELECT * FROM cte", "cte");
            CollectionAssert.Contains(cols.ToList(), "Id");
            CollectionAssert.Contains(cols.ToList(), "Name");
        }

        [TestMethod]
        public void DerivedColumns_Aliases() {
            var cols = Extract(
                "WITH cte AS (SELECT Id AS UserId, Name AS UserName FROM dbo.Users) SELECT * FROM cte",
                "cte");
            CollectionAssert.AreEqual(new[] { "UserId", "UserName" }, cols.ToList());
        }

        [TestMethod]
        public void DerivedColumns_MixedAliasAndRef() {
            // Id has no alias → plain ref; expr2 has alias
            var cols = Extract(
                "WITH cte AS (SELECT Id, Status AS State FROM t) SELECT * FROM cte", "cte");
            CollectionAssert.Contains(cols.ToList(), "Id");
            CollectionAssert.Contains(cols.ToList(), "State");
            Assert.AreEqual(2, cols.Count);
        }

        [TestMethod]
        public void DerivedColumns_ExpressionWithoutAlias_Skipped() {
            // GETDATE() has no alias → skipped
            var cols = Extract(
                "WITH cte AS (SELECT Id, GETDATE() FROM t) SELECT * FROM cte", "cte");
            CollectionAssert.Contains(cols.ToList(), "Id");
            Assert.AreEqual(1, cols.Count);
        }

        // ── UNION: first branch used ───────────────────────────────────────────

        [TestMethod]
        public void Union_FirstBranchColumns() {
            var cols = Extract(
                "WITH cte AS (SELECT Id FROM t1 UNION SELECT Id FROM t2) SELECT * FROM cte",
                "cte");
            CollectionAssert.Contains(cols.ToList(), "Id");
        }

        // ── CTE not found ──────────────────────────────────────────────────────

        [TestMethod]
        public void CteNotFound_ReturnsEmpty() {
            var cols = Extract(
                "WITH cte AS (SELECT Id FROM t) SELECT * FROM cte", "nonexistent");
            Assert.AreEqual(0, cols.Count);
        }

        [TestMethod]
        public void CteNameLookup_CaseInsensitive() {
            var cols = Extract(
                "WITH MyCte AS (SELECT Id FROM t) SELECT * FROM MyCte", "mycte");
            CollectionAssert.Contains(cols.ToList(), "Id");
        }

        // ── Null safety ────────────────────────────────────────────────────────

        [TestMethod]
        public void NullParseResult_ReturnsEmpty() {
            var cols = Extractor.ExtractColumns(null, "cte");
            Assert.AreEqual(0, cols.Count);
        }

        // ── Recursive CTE (characterization: anchor member defines the columns) ─

        [TestMethod]
        public void RecursiveCte_ColumnsFromAnchorMember() {
            var cols = Extract(
                "WITH cte AS (SELECT 1 AS n UNION ALL SELECT n + 1 FROM cte WHERE n < 10) SELECT * FROM cte",
                "cte");
            CollectionAssert.AreEqual(new[] { "n" }, cols.ToList());
        }
    }
}
