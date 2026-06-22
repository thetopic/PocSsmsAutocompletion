using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Collections.Generic;
using System.Linq;

namespace SsmsAutocompletion.Tests {

    [TestClass]
    public class WindowColumnCompletionProviderTests {

        private static CompletionRequest Request(bool isWindowContext) =>
            new CompletionRequest(
                "", 0, 1, 1, null, null, null,
                isDotContext: false, qualifier: null,
                isAfterFromKeyword: false, isJoinOnContext: false, isAfterJoinKeyword: false,
                isWhereContext: false, isAfterTableInFromJoin: false, tableNameBeforeCursor: null,
                snapshot: null, isAfterExecKeyword: false,
                isInsideProcedureCall: false, procedureNameBeforeCursor: null,
                alreadyProvidedParameters: null,
                isInsertColumnList: false, isUpdateSetClause: false,
                insertUpdateTargetTable: null, isInSelectList: false,
                isGroupByContext: false, isHavingContext: false, isOrderByContext: false,
                isAfterWithKeyword: false, isWindowContext: isWindowContext);

        [TestMethod]
        public void WindowContext_ReturnsScopedColumns() {
            var columns  = new List<CompletionItem> { new CompletionItem("Id", "Id", null, CompletionItemKind.Column) };
            var resolver = new Mock<IScopedColumnResolver>();
            resolver.Setup(r => r.GetVisibleColumns(It.IsAny<CompletionRequest>())).Returns(columns);
            var provider = new WindowColumnCompletionProvider(resolver.Object);

            var items = provider.GetCompletions(Request(isWindowContext: true));
            CollectionAssert.AreEqual(new[] { "Id" }, items.Select(i => i.DisplayText).ToList());
        }

        [TestMethod]
        public void NotWindowContext_ReturnsEmpty() {
            var resolver = new Mock<IScopedColumnResolver>();
            resolver.Setup(r => r.GetVisibleColumns(It.IsAny<CompletionRequest>()))
                .Returns(new List<CompletionItem> { new CompletionItem("Id", "Id", null, CompletionItemKind.Column) });
            var provider = new WindowColumnCompletionProvider(resolver.Object);

            var items = provider.GetCompletions(Request(isWindowContext: false));
            Assert.AreEqual(0, items.Count);
        }
    }
}
