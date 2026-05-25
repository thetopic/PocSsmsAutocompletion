using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SsmsAutocompletion.Tests {

    [TestClass]
    public class FunctionCompletionProviderTests {

        private static readonly FunctionCompletionProvider Provider = new FunctionCompletionProvider();

        private static CompletionRequest Make(bool isDotContext = false, bool isAfterFromKeyword = false) =>
            new CompletionRequest(
                sql: "SELECT", caretPosition: 0, line: 1, column: 1,
                connectionKey: null, parseResult: null, metadataProvider: null,
                isDotContext: isDotContext, qualifier: null,
                isAfterFromKeyword: isAfterFromKeyword,
                isJoinOnContext: false, isAfterJoinKeyword: false,
                isWhereContext: false, isAfterTableInFromJoin: false,
                tableNameBeforeCursor: null, snapshot: null);

        [TestMethod]
        public void NormalContext_ReturnsFunctions() {
            Assert.IsTrue(Provider.GetCompletions(Make()).Count > 0);
        }

        [TestMethod]
        public void DotContext_ReturnsEmpty() {
            Assert.AreEqual(0, Provider.GetCompletions(Make(isDotContext: true)).Count);
        }

        [TestMethod]
        public void AfterFromContext_ReturnsEmpty() {
            // Functions like COALESCE, CAST must not appear in a FROM clause.
            Assert.AreEqual(0, Provider.GetCompletions(Make(isAfterFromKeyword: true)).Count);
        }
    }
}
