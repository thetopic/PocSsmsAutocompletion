using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Linq;

namespace SsmsAutocompletion.Tests {

    [TestClass]
    public class CteCompletionProviderTests {

        private static CompletionRequest Request(bool isDotContext, bool isAfterWithKeyword) =>
            new CompletionRequest(
                "", 0, 1, 1, null, null, null,
                isDotContext: isDotContext, qualifier: null,
                isAfterFromKeyword: false, isJoinOnContext: false, isAfterJoinKeyword: false,
                isWhereContext: false, isAfterTableInFromJoin: false, tableNameBeforeCursor: null,
                snapshot: null, isAfterExecKeyword: false,
                isInsideProcedureCall: false, procedureNameBeforeCursor: null,
                alreadyProvidedParameters: null,
                isInsertColumnList: false, isUpdateSetClause: false,
                insertUpdateTargetTable: null, isInSelectList: false,
                isGroupByContext: false, isHavingContext: false, isOrderByContext: false,
                isAfterWithKeyword: isAfterWithKeyword);

        [TestMethod]
        public void ExistingCtes_ReturnsTheirNames() {
            var cteExtractor = new Mock<ICteExtractor>();
            cteExtractor.Setup(e => e.Extract(null)).Returns(new[] { "myCte" });
            var provider = new CteCompletionProvider(cteExtractor.Object);

            var items = provider.GetCompletions(Request(isDotContext: false, isAfterWithKeyword: false));
            CollectionAssert.Contains(items.Select(i => i.DisplayText).ToList(), "myCte");
        }

        [TestMethod]
        public void AfterWithKeyword_NoCtesYet_ReturnsSkeleton() {
            var cteExtractor = new Mock<ICteExtractor>();
            cteExtractor.Setup(e => e.Extract(null)).Returns(System.Array.Empty<string>());
            var provider = new CteCompletionProvider(cteExtractor.Object);

            var items = provider.GetCompletions(Request(isDotContext: false, isAfterWithKeyword: true));
            Assert.AreEqual(1, items.Count);
            StringAssert.Contains(items[0].InsertText, "AS (");
            StringAssert.Contains(items[0].InsertText, "SELECT * FROM");
        }

        [TestMethod]
        public void AfterWithKeyword_CtesExist_NoSkeletonDuplication() {
            var cteExtractor = new Mock<ICteExtractor>();
            cteExtractor.Setup(e => e.Extract(null)).Returns(new[] { "existing" });
            var provider = new CteCompletionProvider(cteExtractor.Object);

            var items = provider.GetCompletions(Request(isDotContext: false, isAfterWithKeyword: true));
            Assert.AreEqual(1, items.Count);
            Assert.AreEqual("existing", items[0].DisplayText);
        }

        [TestMethod]
        public void NotAfterWithKeyword_NoCtes_ReturnsEmpty() {
            var cteExtractor = new Mock<ICteExtractor>();
            cteExtractor.Setup(e => e.Extract(null)).Returns(System.Array.Empty<string>());
            var provider = new CteCompletionProvider(cteExtractor.Object);

            var items = provider.GetCompletions(Request(isDotContext: false, isAfterWithKeyword: false));
            Assert.AreEqual(0, items.Count);
        }

        [TestMethod]
        public void DotContext_ReturnsEmpty() {
            var cteExtractor = new Mock<ICteExtractor>();
            var provider = new CteCompletionProvider(cteExtractor.Object);

            var items = provider.GetCompletions(Request(isDotContext: true, isAfterWithKeyword: true));
            Assert.AreEqual(0, items.Count);
        }
    }
}
