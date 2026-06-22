using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using SsmsAutocompletion.Tests.Helpers;

namespace SsmsAutocompletion.Tests {

    [TestClass]
    public class CompletionRequestBuilderTests {

        private static CompletionRequestBuilder MakeBuilder() {
            var meta = new Mock<IDatabaseMetadata>();
            return new CompletionRequestBuilder(
                new SsmsSqlParser(),
                new SqlContextDetector(),
                meta.Object);
        }

        private static CompletionRequest Build(string sql) {
            var snap    = SnapshotHelper.Make(sql);
            var builder = MakeBuilder();
            return builder.Build(snap, sql, sql.Length, null);
        }

        // ── IsAfterFromKeyword ─────────────────────────────────────────────────

        [TestMethod]
        public void IsAfterFromKeyword_AfterFromSpace_True() {
            // "SELECT * FROM " — cursor right after the space following FROM
            Assert.IsTrue(Build("SELECT * FROM ").IsAfterFromKeyword);
        }

        [TestMethod]
        public void IsAfterFromKeyword_AfterFromPartialWord_True() {
            // "SELECT * FROM Ord" — cursor mid-word; FROM is still the governing keyword
            Assert.IsTrue(Build("SELECT * FROM Ord").IsAfterFromKeyword);
        }

        [TestMethod]
        public void IsAfterFromKeyword_AfterFullTableName_False() {
            // "SELECT * FROM Orders " — full table already typed, expect WHERE/JOIN next
            Assert.IsFalse(Build("SELECT * FROM Orders ").IsAfterFromKeyword);
        }

        [TestMethod]
        public void IsAfterFromKeyword_AfterWhere_False() {
            Assert.IsFalse(Build("SELECT * FROM Orders WHERE ").IsAfterFromKeyword);
        }

        [TestMethod]
        public void IsAfterFromKeyword_AfterJoin_False() {
            Assert.IsFalse(Build("SELECT * FROM Orders o INNER JOIN ").IsAfterFromKeyword);
        }

        [TestMethod]
        public void IsAfterFromKeyword_SelectOnly_False() {
            Assert.IsFalse(Build("SELECT ").IsAfterFromKeyword);
        }

        // ── IsAfterExecKeyword ─────────────────────────────────────────────────

        [TestMethod]
        public void IsAfterExecKeyword_AfterExecSpace_True() {
            Assert.IsTrue(Build("EXEC ").IsAfterExecKeyword);
        }

        [TestMethod]
        public void IsAfterExecKeyword_AfterExecuteSpace_True() {
            Assert.IsTrue(Build("EXECUTE ").IsAfterExecKeyword);
        }

        [TestMethod]
        public void IsAfterExecKeyword_PartialProcName_True() {
            Assert.IsTrue(Build("EXEC GetCust").IsAfterExecKeyword);
        }

        [TestMethod]
        public void IsAfterExecKeyword_AfterSelect_False() {
            Assert.IsFalse(Build("SELECT ").IsAfterExecKeyword);
        }

        [TestMethod]
        public void IsAfterExecKeyword_AfterFrom_False() {
            Assert.IsFalse(Build("SELECT * FROM ").IsAfterExecKeyword);
        }

        // ── IsGroupByContext ───────────────────────────────────────────────────

        [TestMethod]
        public void IsGroupByContext_AfterGroupBy_True() {
            Assert.IsTrue(Build("SELECT a, COUNT(*) FROM Orders GROUP BY ").IsGroupByContext);
        }

        [TestMethod]
        public void IsGroupByContext_BeforeGroupBy_False() {
            Assert.IsFalse(Build("SELECT a FROM Orders ").IsGroupByContext);
        }

        // ── IsHavingContext ────────────────────────────────────────────────────

        [TestMethod]
        public void IsHavingContext_AfterHaving_True() {
            Assert.IsTrue(Build("SELECT a, COUNT(*) FROM Orders GROUP BY a HAVING ").IsHavingContext);
        }

        [TestMethod]
        public void IsHavingContext_InGroupBy_False() {
            Assert.IsFalse(Build("SELECT a FROM Orders GROUP BY ").IsHavingContext);
        }

        // ── IsOrderByContext ───────────────────────────────────────────────────

        [TestMethod]
        public void IsOrderByContext_AfterOrderBy_True() {
            Assert.IsTrue(Build("SELECT a FROM Orders ORDER BY ").IsOrderByContext);
        }

        [TestMethod]
        public void IsOrderByContext_BeforeOrderBy_False() {
            Assert.IsFalse(Build("SELECT a FROM Orders ").IsOrderByContext);
        }

        // ── IsWindowContext ────────────────────────────────────────────────────

        [TestMethod]
        public void IsWindowContext_InsideOverPartitionBy_True() {
            Assert.IsTrue(Build("SELECT ROW_NUMBER() OVER (PARTITION BY ").IsWindowContext);
        }

        [TestMethod]
        public void IsWindowContext_OutsideOver_False() {
            Assert.IsFalse(Build("SELECT a FROM Orders ").IsWindowContext);
        }

        // ── IsAfterWithKeyword ─────────────────────────────────────────────────

        [TestMethod]
        public void IsAfterWithKeyword_AfterWithSpace_True() {
            Assert.IsTrue(Build("WITH ").IsAfterWithKeyword);
        }

        [TestMethod]
        public void IsAfterWithKeyword_AfterSelect_False() {
            Assert.IsFalse(Build("SELECT ").IsAfterWithKeyword);
        }

        // ── DELETE statements (FROM/WHERE detection is statement-agnostic) ──────

        [TestMethod]
        public void DeleteFrom_IsAfterFromKeyword_True() {
            Assert.IsTrue(Build("DELETE FROM ").IsAfterFromKeyword);
        }

        [TestMethod]
        public void DeleteFromTableWhere_IsWhereContext_True() {
            Assert.IsTrue(Build("DELETE FROM Orders WHERE ").IsWhereContext);
        }
    }
}
