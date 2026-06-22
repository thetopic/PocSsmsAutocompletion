using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace SsmsAutocompletion.Tests {

    [TestClass]
    public class TempTableCompletionProviderTests {

        private static readonly SsmsSqlParser      Parser    = new SsmsSqlParser();
        private static readonly TempTableExtractor Extractor = new TempTableExtractor();
        private static readonly TempTableCompletionProvider Provider =
            new TempTableCompletionProvider(Extractor);

        private static CompletionRequest BuildRequest(
            string sql, bool isAfterFromKeyword = true, bool isAfterJoinKeyword = false, bool isDotContext = false) {
            var parseResult = Parser.Parse(sql);
            return new CompletionRequest(
                sql, sql.Length, 1, sql.Length + 1,
                new ConnectionKey("srv|db"), parseResult, null,
                isDotContext: isDotContext, qualifier: null,
                isAfterFromKeyword: isAfterFromKeyword, isJoinOnContext: false, isAfterJoinKeyword: isAfterJoinKeyword,
                isWhereContext: false, isAfterTableInFromJoin: false, tableNameBeforeCursor: null,
                snapshot: null);
        }

        [TestMethod]
        public void AfterFromKeyword_ReturnsTempTableNames() {
            string sql = "CREATE TABLE #temp (Id INT); SELECT * FROM ";
            var items = Provider.GetCompletions(BuildRequest(sql));
            CollectionAssert.Contains(items.Select(i => i.DisplayText).ToList(), "#temp");
            Assert.IsTrue(items.All(i => i.Kind == CompletionItemKind.Table));
        }

        [TestMethod]
        public void AfterJoinKeyword_ReturnsTempTableNames() {
            string sql = "CREATE TABLE #temp (Id INT); SELECT * FROM Orders JOIN ";
            var items = Provider.GetCompletions(BuildRequest(sql, isAfterFromKeyword: false, isAfterJoinKeyword: true));
            CollectionAssert.Contains(items.Select(i => i.DisplayText).ToList(), "#temp");
        }

        [TestMethod]
        public void DotContext_ReturnsEmpty() {
            string sql = "CREATE TABLE #temp (Id INT); SELECT * FROM #temp.";
            var items = Provider.GetCompletions(BuildRequest(sql, isDotContext: true));
            Assert.AreEqual(0, items.Count);
        }

        [TestMethod]
        public void NotAfterFromOrJoin_ReturnsEmpty() {
            string sql = "CREATE TABLE #temp (Id INT); SELECT ";
            var items = Provider.GetCompletions(BuildRequest(sql, isAfterFromKeyword: false, isAfterJoinKeyword: false));
            Assert.AreEqual(0, items.Count);
        }

        [TestMethod]
        public void AfterFromKeyword_ReturnsTableVariableNames() {
            string sql = "DECLARE @t TABLE (Id INT); SELECT * FROM ";
            var items = Provider.GetCompletions(BuildRequest(sql));
            CollectionAssert.Contains(items.Select(i => i.DisplayText).ToList(), "@t");
        }

        [TestMethod]
        public void NoTempTablesDefined_ReturnsEmpty() {
            string sql = "SELECT * FROM ";
            var items = Provider.GetCompletions(BuildRequest(sql));
            Assert.AreEqual(0, items.Count);
        }
    }
}
