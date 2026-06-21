using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace SsmsAutocompletion.Tests {

    [TestClass]
    public class DerivedTableExtractorTests {

        private static readonly SsmsSqlParser         Parser    = new SsmsSqlParser();
        private static readonly DerivedTableExtractor Extractor = new DerivedTableExtractor();

        private static System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IReadOnlyList<string>>
            Extract(string sql) => Extractor.Extract(Parser.Parse(sql));

        // ── Happy path ─────────────────────────────────────────────────────────

        [TestMethod]
        public void SingleDerivedTable_ExtractsAliasAndColumns() {
            var result = Extract("SELECT * FROM (SELECT a, b FROM Orders) AS d");
            Assert.IsTrue(result.ContainsKey("d"));
            CollectionAssert.AreEqual(new[] { "a", "b" }, result["d"].ToList());
        }

        [TestMethod]
        public void DerivedTable_WithColumnAliases() {
            var result = Extract("SELECT * FROM (SELECT x AS foo, y AS bar FROM T) AS d");
            CollectionAssert.AreEqual(new[] { "foo", "bar" }, result["d"].ToList());
        }

        [TestMethod]
        public void DerivedTable_InJoin_BothSidesExtracted() {
            var result = Extract(
                "SELECT * FROM (SELECT a FROM T1) AS d JOIN (SELECT b FROM T2) AS e ON 1=1");
            Assert.IsTrue(result.ContainsKey("d"));
            Assert.IsTrue(result.ContainsKey("e"));
            CollectionAssert.AreEqual(new[] { "a" }, result["d"].ToList());
            CollectionAssert.AreEqual(new[] { "b" }, result["e"].ToList());
        }

        [TestMethod]
        public void DerivedTable_AliasLookup_CaseInsensitive() {
            var result = Extract("SELECT * FROM (SELECT a FROM T) AS D");
            Assert.IsTrue(result.ContainsKey("d"));
        }

        [TestMethod]
        public void DerivedTable_PlainColumnRefAndAlias_Mixed() {
            var result = Extract("SELECT * FROM (SELECT Id, Status AS State FROM T) AS d");
            CollectionAssert.Contains(result["d"].ToList(), "Id");
            CollectionAssert.Contains(result["d"].ToList(), "State");
        }

        // ── No derived table ───────────────────────────────────────────────────

        [TestMethod]
        public void NoDerivedTable_ReturnsEmpty() {
            var result = Extract("SELECT * FROM Orders");
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void EmptySql_ReturnsEmpty() {
            var result = Extract("");
            Assert.AreEqual(0, result.Count);
        }

        // ── Null safety ────────────────────────────────────────────────────────

        [TestMethod]
        public void NullParseResult_ReturnsEmpty() {
            var result = Extractor.Extract(null);
            Assert.AreEqual(0, result.Count);
        }
    }
}
