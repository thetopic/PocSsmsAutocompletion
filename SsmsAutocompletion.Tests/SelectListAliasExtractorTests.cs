using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace SsmsAutocompletion.Tests {

    [TestClass]
    public class SelectListAliasExtractorTests {

        private static readonly SsmsSqlParser            Parser    = new SsmsSqlParser();
        private static readonly SelectListAliasExtractor Extractor = new SelectListAliasExtractor();

        private static System.Collections.Generic.IReadOnlyList<string> Extract(string sql) {
            var parseResult  = Parser.Parse(sql);
            var (line, col)  = Parser.GetLineColumn(sql, sql.Length);
            return Extractor.Extract(parseResult, line, col);
        }

        [TestMethod]
        public void SimpleAlias_Extracted() {
            var aliases = Extract("SELECT x AS foo FROM T ORDER BY ");
            CollectionAssert.AreEqual(new[] { "foo" }, aliases.ToList());
        }

        [TestMethod]
        public void MultipleAliases_AllExtracted() {
            var aliases = Extract("SELECT x AS foo, y AS bar FROM T ORDER BY ");
            CollectionAssert.AreEqual(new[] { "foo", "bar" }, aliases.ToList());
        }

        [TestMethod]
        public void NoAlias_PlainColumnRef_NotIncluded() {
            var aliases = Extract("SELECT x FROM T ORDER BY ");
            Assert.AreEqual(0, aliases.Count);
        }

        [TestMethod]
        public void NestedSubquery_OuterAliasesOnly() {
            var aliases = Extract(
                "SELECT a AS outerAlias FROM (SELECT b AS innerAlias FROM T) AS d ORDER BY ");
            CollectionAssert.AreEqual(new[] { "outerAlias" }, aliases.ToList());
        }

        [TestMethod]
        public void EmptySql_ReturnsEmpty() {
            Assert.AreEqual(0, Extract("").Count);
        }

        [TestMethod]
        public void NullParseResult_ReturnsEmpty() {
            Assert.AreEqual(0, Extractor.Extract(null, 1, 1).Count);
        }
    }
}
