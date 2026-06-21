using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Collections.Generic;
using System.Linq;

namespace SsmsAutocompletion.Tests {

    [TestClass]
    public class HavingColumnCompletionProviderTests {

        private static CompletionRequest Request(bool isHavingContext) =>
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
                isGroupByContext: false, isHavingContext: isHavingContext);

        [TestMethod]
        public void HavingContext_ReturnsScopedColumns() {
            var columns  = new List<CompletionItem> { new CompletionItem("Id", "Id", null, CompletionItemKind.Column) };
            var resolver = new Mock<IScopedColumnResolver>();
            resolver.Setup(r => r.GetVisibleColumns(It.IsAny<CompletionRequest>())).Returns(columns);
            var provider = new HavingColumnCompletionProvider(resolver.Object);

            var items = provider.GetCompletions(Request(isHavingContext: true));
            CollectionAssert.AreEqual(new[] { "Id" }, items.Select(i => i.DisplayText).ToList());
        }

        [TestMethod]
        public void NotHavingContext_ReturnsEmpty() {
            var resolver = new Mock<IScopedColumnResolver>();
            resolver.Setup(r => r.GetVisibleColumns(It.IsAny<CompletionRequest>()))
                .Returns(new List<CompletionItem> { new CompletionItem("Id", "Id", null, CompletionItemKind.Column) });
            var provider = new HavingColumnCompletionProvider(resolver.Object);

            var items = provider.GetCompletions(Request(isHavingContext: false));
            Assert.AreEqual(0, items.Count);
        }
    }
}
