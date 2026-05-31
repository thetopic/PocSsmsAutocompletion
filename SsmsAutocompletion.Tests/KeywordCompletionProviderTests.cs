using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace SsmsAutocompletion.Tests {

    [TestClass]
    public class KeywordCompletionProviderTests {

        private static readonly KeywordCompletionProvider Provider = new KeywordCompletionProvider();

        private static CompletionRequest Make(
            bool isDotContext       = false,
            bool isAfterFromKeyword = false,
            bool isAfterExecKeyword = false) =>
            new CompletionRequest(
                sql: "SELECT", caretPosition: 0, line: 1, column: 1,
                connectionKey: null, parseResult: null, metadataProvider: null,
                isDotContext: isDotContext, qualifier: null,
                isAfterFromKeyword: isAfterFromKeyword,
                isJoinOnContext: false, isAfterJoinKeyword: false,
                isWhereContext: false, isAfterTableInFromJoin: false,
                tableNameBeforeCursor: null, snapshot: null,
                isAfterExecKeyword: isAfterExecKeyword);

        // ── Context gating ─────────────────────────────────────────────────────

        [TestMethod]
        public void NoDotContext_ReturnsKeywords() {
            var items = Provider.GetCompletions(Make(isDotContext: false));
            Assert.IsTrue(items.Count > 0);
        }

        [TestMethod]
        public void DotContext_ReturnsEmpty() {
            var items = Provider.GetCompletions(Make(isDotContext: true));
            Assert.AreEqual(0, items.Count);
        }

        [TestMethod]
        public void AfterFromContext_ReturnsEmpty() {
            Assert.AreEqual(0, Provider.GetCompletions(Make(isAfterFromKeyword: true)).Count);
        }

        [TestMethod]
        public void AfterExecContext_ReturnsEmpty() {
            Assert.AreEqual(0, Provider.GetCompletions(Make(isAfterExecKeyword: true)).Count);
        }

        [TestMethod]
        public void NotAfterFromContext_ReturnsKeywords() {
            Assert.IsTrue(Provider.GetCompletions(Make(isAfterFromKeyword: false)).Count > 0);
        }

        // ── Keyword coverage ───────────────────────────────────────────────────

        [TestMethod]
        public void Contains_SELECT() =>
            Assert.IsTrue(Provider.GetCompletions(Make()).Any(i => i.DisplayText == "SELECT"));

        [TestMethod]
        public void Contains_FROM() =>
            Assert.IsTrue(Provider.GetCompletions(Make()).Any(i => i.DisplayText == "FROM"));

        [TestMethod]
        public void Contains_WHERE() =>
            Assert.IsTrue(Provider.GetCompletions(Make()).Any(i => i.DisplayText == "WHERE"));

        [TestMethod]
        public void Contains_JOIN() =>
            Assert.IsTrue(Provider.GetCompletions(Make()).Any(i => i.DisplayText == "JOIN"));

        [TestMethod]
        public void Contains_WITH() =>
            Assert.IsTrue(Provider.GetCompletions(Make()).Any(i => i.DisplayText == "WITH"));

        // ── Item shape ─────────────────────────────────────────────────────────

        [TestMethod]
        public void AllItems_KindIsKeyword() {
            var items = Provider.GetCompletions(Make());
            Assert.IsTrue(items.All(i => i.Kind == CompletionItemKind.Keyword));
        }

        [TestMethod]
        public void InsertText_EndsWithSpace() {
            // Convention: insert text adds trailing space so next token can follow
            var items = Provider.GetCompletions(Make());
            Assert.IsTrue(items.All(i => i.InsertText.EndsWith(" ")),
                "Expected all keyword insert texts to end with a space");
        }

        [TestMethod]
        public void DisplayText_EqualsKeyword() {
            var select = Provider.GetCompletions(Make()).First(i => i.DisplayText == "SELECT");
            Assert.AreEqual("SELECT ", select.InsertText);
        }
    }
}
