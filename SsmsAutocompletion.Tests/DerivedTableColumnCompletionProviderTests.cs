using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;

namespace SsmsAutocompletion.Tests {

    [TestClass]
    public class DerivedTableColumnCompletionProviderTests {

        private static readonly SsmsSqlParser             Parser    = new SsmsSqlParser();
        private static readonly DerivedTableExtractor     Extractor = new DerivedTableExtractor();
        private static readonly DerivedTableColumnCompletionProvider Provider =
            new DerivedTableColumnCompletionProvider(Extractor);

        private static CompletionRequest BuildRequest(string sql, string qualifier) {
            var parseResult = Parser.Parse(sql);
            return new CompletionRequest(
                sql, 0, 1, 1,
                new ConnectionKey("srv|db"), parseResult, null,
                isDotContext: true, qualifier: qualifier,
                isAfterFromKeyword: false, isJoinOnContext: false, isAfterJoinKeyword: false,
                isWhereContext: false, isAfterTableInFromJoin: false, tableNameBeforeCursor: null,
                snapshot: null);
        }

        [TestMethod]
        public void DotAfterDerivedTableAlias_ReturnsSyntheticColumns() {
            var request = BuildRequest("SELECT * FROM (SELECT a, b FROM Orders) AS d", "d");
            var items = Provider.GetCompletions(request);
            CollectionAssert.AreEqual(
                new[] { "a", "b" },
                items.Select(i => i.DisplayText).ToList());
            Assert.IsTrue(items.All(i => i.Kind == CompletionItemKind.Column));
        }

        [TestMethod]
        public void NotDotContext_ReturnsEmpty() {
            var request = BuildRequest("SELECT * FROM (SELECT a FROM Orders) AS d", "d");
            var notDot = new CompletionRequest(
                request.Sql, 0, 1, 1, request.ConnectionKey, request.ParseResult, null,
                isDotContext: false, qualifier: "d",
                isAfterFromKeyword: false, isJoinOnContext: false, isAfterJoinKeyword: false,
                isWhereContext: false, isAfterTableInFromJoin: false, tableNameBeforeCursor: null,
                snapshot: null);
            var items = Provider.GetCompletions(notDot);
            Assert.AreEqual(0, items.Count);
        }

        [TestMethod]
        public void UnknownQualifier_ReturnsEmpty() {
            var request = BuildRequest("SELECT * FROM (SELECT a FROM Orders) AS d", "notd");
            var items = Provider.GetCompletions(request);
            Assert.AreEqual(0, items.Count);
        }

        [TestMethod]
        public void EmptyQualifier_ReturnsEmpty() {
            var request = BuildRequest("SELECT * FROM (SELECT a FROM Orders) AS d", "");
            var items = Provider.GetCompletions(request);
            Assert.AreEqual(0, items.Count);
        }
    }
}
