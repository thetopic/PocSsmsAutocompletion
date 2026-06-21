using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Collections.Generic;
using System.Linq;

namespace SsmsAutocompletion.Tests {

    [TestClass]
    public class OrderByColumnCompletionProviderTests {

        private static CompletionRequest Request(string sql, bool isOrderByContext) {
            var parseResult = new SsmsSqlParser().Parse(sql);
            var (line, col)  = new SsmsSqlParser().GetLineColumn(sql, sql.Length);
            return new CompletionRequest(
                sql, sql.Length, line, col, null, parseResult, null,
                isDotContext: false, qualifier: null,
                isAfterFromKeyword: false, isJoinOnContext: false, isAfterJoinKeyword: false,
                isWhereContext: false, isAfterTableInFromJoin: false, tableNameBeforeCursor: null,
                snapshot: null, isAfterExecKeyword: false,
                isInsideProcedureCall: false, procedureNameBeforeCursor: null,
                alreadyProvidedParameters: null,
                isInsertColumnList: false, isUpdateSetClause: false,
                insertUpdateTargetTable: null, isInSelectList: false,
                isGroupByContext: false, isHavingContext: false,
                isOrderByContext: isOrderByContext);
        }

        [TestMethod]
        public void OrderByContext_ReturnsTableColumns() {
            var columns  = new List<CompletionItem> { new CompletionItem("Id", "Id", null, CompletionItemKind.Column) };
            var resolver = new Mock<IScopedColumnResolver>();
            resolver.Setup(r => r.GetVisibleColumns(It.IsAny<CompletionRequest>())).Returns(columns);
            var provider = new OrderByColumnCompletionProvider(resolver.Object, new SelectListAliasExtractor());

            var items = provider.GetCompletions(Request("SELECT Id FROM Orders ORDER BY ", isOrderByContext: true));
            CollectionAssert.Contains(items.Select(i => i.DisplayText).ToList(), "Id");
        }

        [TestMethod]
        public void OrderByContext_ReturnsSelectListAlias() {
            var resolver = new Mock<IScopedColumnResolver>();
            resolver.Setup(r => r.GetVisibleColumns(It.IsAny<CompletionRequest>())).Returns(new List<CompletionItem>());
            var provider = new OrderByColumnCompletionProvider(resolver.Object, new SelectListAliasExtractor());

            var items = provider.GetCompletions(Request("SELECT x AS foo FROM Orders ORDER BY ", isOrderByContext: true));
            CollectionAssert.Contains(items.Select(i => i.DisplayText).ToList(), "foo");
        }

        [TestMethod]
        public void OrderByContext_DedupsAliasAndColumn() {
            var columns  = new List<CompletionItem> { new CompletionItem("foo", "foo", null, CompletionItemKind.Column) };
            var resolver = new Mock<IScopedColumnResolver>();
            resolver.Setup(r => r.GetVisibleColumns(It.IsAny<CompletionRequest>())).Returns(columns);
            var provider = new OrderByColumnCompletionProvider(resolver.Object, new SelectListAliasExtractor());

            var items = provider.GetCompletions(Request("SELECT x AS foo FROM Orders ORDER BY ", isOrderByContext: true));
            Assert.AreEqual(1, items.Count(i => i.DisplayText == "foo"));
        }

        [TestMethod]
        public void NotOrderByContext_ReturnsEmpty() {
            var resolver = new Mock<IScopedColumnResolver>();
            resolver.Setup(r => r.GetVisibleColumns(It.IsAny<CompletionRequest>()))
                .Returns(new List<CompletionItem> { new CompletionItem("Id", "Id", null, CompletionItemKind.Column) });
            var provider = new OrderByColumnCompletionProvider(resolver.Object, new SelectListAliasExtractor());

            var items = provider.GetCompletions(Request("SELECT Id FROM Orders ", isOrderByContext: false));
            Assert.AreEqual(0, items.Count);
        }
    }
}
