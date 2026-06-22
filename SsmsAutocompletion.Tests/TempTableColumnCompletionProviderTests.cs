using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace SsmsAutocompletion.Tests {

    [TestClass]
    public class TempTableColumnCompletionProviderTests {

        private static readonly SsmsSqlParser        Parser         = new SsmsSqlParser();
        private static readonly TempTableExtractor   Extractor      = new TempTableExtractor();
        private static readonly AliasExtractor       AliasExtractor = new AliasExtractor();
        private static readonly TempTableColumnCompletionProvider Provider =
            new TempTableColumnCompletionProvider(Extractor, AliasExtractor);

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
        public void DotAfterTempTable_ReturnsColumns() {
            var request = BuildRequest("CREATE TABLE #temp (Id INT, Name VARCHAR(50)); SELECT * FROM #temp", "#temp");
            var items = Provider.GetCompletions(request);
            CollectionAssert.AreEqual(new[] { "Id", "Name" }, items.Select(i => i.DisplayText).ToList());
            Assert.IsTrue(items.All(i => i.Kind == CompletionItemKind.Column));
        }

        [TestMethod]
        public void NotDotContext_ReturnsEmpty() {
            var request = BuildRequest("CREATE TABLE #temp (Id INT)", "#temp");
            var notDot = new CompletionRequest(
                request.Sql, 0, 1, 1, request.ConnectionKey, request.ParseResult, null,
                isDotContext: false, qualifier: "#temp",
                isAfterFromKeyword: false, isJoinOnContext: false, isAfterJoinKeyword: false,
                isWhereContext: false, isAfterTableInFromJoin: false, tableNameBeforeCursor: null,
                snapshot: null);
            Assert.AreEqual(0, Provider.GetCompletions(notDot).Count);
        }

        [TestMethod]
        public void UnknownQualifier_ReturnsEmpty() {
            var request = BuildRequest("CREATE TABLE #temp (Id INT)", "#other");
            Assert.AreEqual(0, Provider.GetCompletions(request).Count);
        }

        [TestMethod]
        public void DotAfterTableVariable_ReturnsColumns() {
            var request = BuildRequest("DECLARE @t TABLE (Id INT, Name VARCHAR(50)); SELECT * FROM @t", "@t");
            var items = Provider.GetCompletions(request);
            CollectionAssert.AreEqual(new[] { "Id", "Name" }, items.Select(i => i.DisplayText).ToList());
        }

        [TestMethod]
        public void AliasOnTableVariable_ResolvesColumns() {
            var request = BuildRequest(
                "DECLARE @t TABLE (Id INT); SELECT * FROM @t v WHERE v.", "v");
            var items = Provider.GetCompletions(request);
            CollectionAssert.AreEqual(new[] { "Id" }, items.Select(i => i.DisplayText).ToList());
        }

        [TestMethod]
        public void AliasOnTempTable_ResolvesColumns() {
            var request = BuildRequest(
                "CREATE TABLE #temp (Id INT, Name VARCHAR(50)); SELECT * FROM #temp t WHERE t.", "t");
            var items = Provider.GetCompletions(request);
            CollectionAssert.AreEqual(new[] { "Id", "Name" }, items.Select(i => i.DisplayText).ToList());
        }
    }
}
