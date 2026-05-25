using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SsmsAutocompletion.Tests {

    [TestClass]
    public class NativeCompletionProviderTests {

        private static readonly NativeCompletionProvider Provider = new NativeCompletionProvider();
        private static readonly SsmsSqlParser Parser = new SsmsSqlParser();

        private static CompletionRequest Make(bool isAfterFromKeyword = false, string sql = "SELECT") {
            var parseResult = Parser.Parse(sql);
            var (line, col) = Parser.GetLineColumn(sql, sql.Length);
            return new CompletionRequest(
                sql: sql, caretPosition: sql.Length, line: line, column: col,
                connectionKey: null, parseResult: parseResult, metadataProvider: null,
                isDotContext: false, qualifier: null,
                isAfterFromKeyword: isAfterFromKeyword,
                isJoinOnContext: false, isAfterJoinKeyword: false,
                isWhereContext: false, isAfterTableInFromJoin: false,
                tableNameBeforeCursor: null, snapshot: null);
        }

        [TestMethod]
        public void NullParseResult_ReturnsEmpty() {
            var req = new CompletionRequest(
                sql: "", caretPosition: 0, line: 1, column: 1,
                connectionKey: null, parseResult: null, metadataProvider: null,
                isDotContext: false, qualifier: null,
                isAfterFromKeyword: false,
                isJoinOnContext: false, isAfterJoinKeyword: false,
                isWhereContext: false, isAfterTableInFromJoin: false,
                tableNameBeforeCursor: null, snapshot: null);
            Assert.AreEqual(0, Provider.GetCompletions(req).Count);
        }

        [TestMethod]
        public void AfterFromContext_ReturnsEmpty() {
            // After FROM the native resolver must be suppressed — only tables should appear.
            var req = Make(isAfterFromKeyword: true, sql: "SELECT * FROM ");
            Assert.AreEqual(0, Provider.GetCompletions(req).Count);
        }
    }
}
