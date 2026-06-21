using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

namespace SsmsAutocompletion.Tests {

    [TestClass]
    public class AliasExtractorTests {

        private static readonly SsmsSqlParser  Parser    = new SsmsSqlParser();
        private static readonly AliasExtractor Extractor = new AliasExtractor();

        private IReadOnlyDictionary<string, TableInfo> Extract(string sql) =>
            Extractor.Extract(Parser.Parse(sql));

        // ── Explicit aliases ───────────────────────────────────────────────────

        [TestMethod]
        public void From_WithAlias_NoAs() {
            var map = Extract("SELECT * FROM Orders o");
            Assert.IsTrue(map.ContainsKey("o"));
            Assert.AreEqual("Orders", map["o"].TableName);
        }

        [TestMethod]
        public void From_WithAlias_WithAs() {
            var map = Extract("SELECT * FROM Orders AS o");
            Assert.IsTrue(map.ContainsKey("o"));
            Assert.AreEqual("Orders", map["o"].TableName);
        }

        [TestMethod]
        public void Join_WithAlias() {
            var map = Extract(
                "SELECT * FROM Customers c INNER JOIN Orders o ON c.Id = o.CustomerId");
            Assert.IsTrue(map.ContainsKey("c"));
            Assert.IsTrue(map.ContainsKey("o"));
            Assert.AreEqual("Customers", map["c"].TableName);
            Assert.AreEqual("Orders",    map["o"].TableName);
        }

        // ── No alias → table name used as key ─────────────────────────────────

        [TestMethod]
        public void From_NoAlias_TableNameIsKey() {
            var map = Extract("SELECT * FROM Orders");
            Assert.IsTrue(map.ContainsKey("orders")); // key lowercased
            Assert.AreEqual("Orders", map["orders"].TableName);
        }

        // ── Schema-qualified ───────────────────────────────────────────────────

        [TestMethod]
        public void Schema_Qualified_ExtractsTableAndSchema() {
            var map = Extract("SELECT * FROM dbo.Orders o");
            Assert.IsTrue(map.ContainsKey("o"));
            Assert.AreEqual("Orders", map["o"].TableName);
            Assert.AreEqual("dbo",    map["o"].Schema);
        }

        [TestMethod]
        public void Schema_WithBrackets() {
            var map = Extract("SELECT * FROM [dbo].[Orders] o");
            Assert.IsTrue(map.ContainsKey("o"));
            Assert.AreEqual("Orders", map["o"].TableName);
        }

        // ── Multiple joins ─────────────────────────────────────────────────────

        [TestMethod]
        public void MultipleJoins_AllAliasesExtracted() {
            var map = Extract(
                "SELECT * FROM A a LEFT JOIN B b ON a.Id = b.AId RIGHT JOIN C c ON b.Id = c.BId");
            Assert.AreEqual(3, map.Count);
            Assert.IsTrue(map.ContainsKey("a"));
            Assert.IsTrue(map.ContainsKey("b"));
            Assert.IsTrue(map.ContainsKey("c"));
        }

        // ── Null safety ────────────────────────────────────────────────────────

        [TestMethod]
        public void NullParseResult_ReturnsEmpty() {
            var map = Extractor.Extract(null);
            Assert.AreEqual(0, map.Count);
        }

        // ── Alias lookup is case-insensitive ──────────────────────────────────

        [TestMethod]
        public void AliasKey_CaseInsensitive() {
            var map = Extract("SELECT * FROM Orders O");
            // key is stored lowercased
            Assert.IsTrue(map.ContainsKey("o"));
        }

        // ── DELETE statements (alias detection is statement-agnostic) ───────────

        [TestMethod]
        public void Delete_WithAlias_Resolved() {
            var map = Extract("DELETE FROM Orders AS o WHERE o.Id = 1");
            Assert.IsTrue(map.ContainsKey("o"));
            Assert.AreEqual("Orders", map["o"].TableName);
        }

        [TestMethod]
        public void DeleteOldStyle_MultiTableAlias_Resolved() {
            var map = Extract(
                "DELETE o FROM Orders AS o JOIN Customers c ON c.Id = o.CustomerId WHERE c.Active = 0");
            Assert.IsTrue(map.ContainsKey("o"));
            Assert.IsTrue(map.ContainsKey("c"));
        }

        // ── ExtractInScope: UNION branch isolation ──────────────────────────────

        private static (int line, int col) Pos(string sql, int position) =>
            Parser.GetLineColumn(sql, position);

        [TestMethod]
        public void ExtractInScope_Union_FirstBranch_OnlyT1Visible() {
            string sql = "SELECT a FROM T1 t1 WHERE t1.a = 1 UNION SELECT b FROM T2 t2 WHERE t2.b = 1";
            int cursor = sql.IndexOf(" UNION"); // end of first branch, before UNION
            var (line, col) = Pos(sql, cursor);
            var map = Extractor.ExtractInScope(Parser.Parse(sql), line, col);
            Assert.IsTrue(map.ContainsKey("t1"));
            Assert.IsFalse(map.ContainsKey("t2"));
        }

        [TestMethod]
        public void ExtractInScope_Union_SecondBranch_OnlyT2Visible() {
            string sql = "SELECT a FROM T1 t1 UNION SELECT b FROM T2 t2 WHERE ";
            var (line, col) = Pos(sql, sql.Length);
            var map = Extractor.ExtractInScope(Parser.Parse(sql), line, col);
            Assert.IsTrue(map.ContainsKey("t2"));
            Assert.IsFalse(map.ContainsKey("t1"));
        }

        [TestMethod]
        public void ExtractInScope_NoUnion_BehavesLikeExtract() {
            string sql = "SELECT a FROM Orders o WHERE ";
            var (line, col) = Pos(sql, sql.Length);
            var scoped = Extractor.ExtractInScope(Parser.Parse(sql), line, col);
            Assert.IsTrue(scoped.ContainsKey("o"));
        }

        [TestMethod]
        public void ExtractInScope_NullParseResult_ReturnsEmpty() {
            var map = Extractor.ExtractInScope(null, 1, 1);
            Assert.AreEqual(0, map.Count);
        }
    }
}
