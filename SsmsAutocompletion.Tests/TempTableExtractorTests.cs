using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace SsmsAutocompletion.Tests {

    [TestClass]
    public class TempTableExtractorTests {

        private static readonly SsmsSqlParser      Parser    = new SsmsSqlParser();
        private static readonly TempTableExtractor Extractor = new TempTableExtractor();

        private static System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IReadOnlyList<string>>
            Extract(string sql) => Extractor.Extract(Parser.Parse(sql));

        // ── CREATE TABLE #x (...) ─────────────────────────────────────────────

        [TestMethod]
        public void CreateTable_ExtractsColumns() {
            var result = Extract("CREATE TABLE #temp (Id INT, Name VARCHAR(50))");
            Assert.IsTrue(result.ContainsKey("#temp"));
            CollectionAssert.AreEqual(new[] { "Id", "Name" }, result["#temp"].ToList());
        }

        [TestMethod]
        public void CreateTable_CaseInsensitiveLookup() {
            var result = Extract("CREATE TABLE #Temp (Id INT)");
            Assert.IsTrue(result.ContainsKey("#temp"));
        }

        [TestMethod]
        public void CreateTable_RegularTable_NotIncluded() {
            var result = Extract("CREATE TABLE Orders (Id INT)");
            Assert.AreEqual(0, result.Count);
        }

        // ── SELECT ... INTO #x ─────────────────────────────────────────────────

        [TestMethod]
        public void SelectInto_ExtractsColumns() {
            var result = Extract("SELECT Id, Name AS FullName INTO #temp FROM Orders");
            Assert.IsTrue(result.ContainsKey("#temp"));
            CollectionAssert.AreEqual(new[] { "Id", "FullName" }, result["#temp"].ToList());
        }

        [TestMethod]
        public void SelectInto_RegularTable_NotIncluded() {
            var result = Extract("SELECT Id INTO ArchivedOrders FROM Orders");
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void SelectWithoutInto_NotIncluded() {
            var result = Extract("SELECT Id FROM Orders");
            Assert.AreEqual(0, result.Count);
        }

        // ── DECLARE @t TABLE (...) ───────────────────────────────────────────────

        [TestMethod]
        public void DeclareTableVariable_ExtractsColumns() {
            var result = Extract("DECLARE @t TABLE (Id INT, Name VARCHAR(50))");
            Assert.IsTrue(result.ContainsKey("@t"));
            CollectionAssert.AreEqual(new[] { "Id", "Name" }, result["@t"].ToList());
        }

        [TestMethod]
        public void DeclareTableVariable_CaseInsensitiveLookup() {
            var result = Extract("DECLARE @T TABLE (Id INT)");
            Assert.IsTrue(result.ContainsKey("@t"));
        }

        // ── Multiple temp tables in same batch ─────────────────────────────────

        [TestMethod]
        public void MultipleTempTables_BothExtracted() {
            var result = Extract(
                "CREATE TABLE #a (X INT); SELECT Y INTO #b FROM Orders;");
            Assert.IsTrue(result.ContainsKey("#a"));
            Assert.IsTrue(result.ContainsKey("#b"));
        }

        [TestMethod]
        public void TempTableAndTableVariable_BothExtracted() {
            var result = Extract(
                "CREATE TABLE #a (X INT); DECLARE @b TABLE (Y INT);");
            Assert.IsTrue(result.ContainsKey("#a"));
            Assert.IsTrue(result.ContainsKey("@b"));
        }

        // ── Null/empty safety ──────────────────────────────────────────────────

        [TestMethod]
        public void EmptySql_ReturnsEmpty() {
            Assert.AreEqual(0, Extract("").Count);
        }

        [TestMethod]
        public void NullParseResult_ReturnsEmpty() {
            Assert.AreEqual(0, Extractor.Extract(null).Count);
        }
    }
}
