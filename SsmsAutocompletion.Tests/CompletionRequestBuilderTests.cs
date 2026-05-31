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
    }
}
