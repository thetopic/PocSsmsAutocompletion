using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SsmsAutocompletion.Tests {

    [TestClass]
    public class UserDefinedFunctionCompletionProviderTests {

        // ── Helpers ────────────────────────────────────────────────────────────

        private static UserFunctionInfo Fn(
            string schema, string name, UserFunctionType type,
            params ParameterInfo[] parameters) =>
            new UserFunctionInfo(schema, name, type,
                new ReadOnlyCollection<ParameterInfo>(new List<ParameterInfo>(parameters)));

        private static ParameterInfo Param(string name, string type) =>
            new ParameterInfo(name, type, isOutput: false, hasDefault: false);

        private static IDatabaseMetadata MetaWith(params UserFunctionInfo[] functions) {
            var mock = new Mock<IDatabaseMetadata>();
            mock.Setup(m => m.GetUserDefinedFunctions(It.IsAny<ConnectionKey>()))
                .Returns(new List<UserFunctionInfo>(functions).AsReadOnly()
                    as IReadOnlyList<UserFunctionInfo>);
            return mock.Object;
        }

        private static CompletionRequest Make(
            bool isAfterFromKeyword  = false,
            bool isDotContext        = false,
            bool isAfterExecKeyword  = false,
            ConnectionKey connectionKey = null) =>
            new CompletionRequest(
                sql: "SELECT ", caretPosition: 7, line: 1, column: 8,
                connectionKey: connectionKey ?? new ConnectionKey("Server|DB"),
                parseResult: null, metadataProvider: null,
                isDotContext: isDotContext, qualifier: null,
                isAfterFromKeyword: isAfterFromKeyword,
                isJoinOnContext: false, isAfterJoinKeyword: false,
                isWhereContext: false, isAfterTableInFromJoin: false,
                tableNameBeforeCursor: null, snapshot: null,
                isAfterExecKeyword: isAfterExecKeyword);

        // ── Context gating — scalar ────────────────────────────────────────────

        [TestMethod]
        public void Scalar_NormalContext_IsReturned() {
            var p = new UserDefinedFunctionCompletionProvider(
                MetaWith(Fn("dbo", "CalcTax", UserFunctionType.Scalar)));
            Assert.AreEqual(1, p.GetCompletions(Make()).Count);
        }

        [TestMethod]
        public void Scalar_DotContext_ReturnsEmpty() {
            var p = new UserDefinedFunctionCompletionProvider(
                MetaWith(Fn("dbo", "CalcTax", UserFunctionType.Scalar)));
            Assert.AreEqual(0, p.GetCompletions(Make(isDotContext: true)).Count);
        }

        [TestMethod]
        public void Scalar_AfterFrom_ReturnsEmpty() {
            var p = new UserDefinedFunctionCompletionProvider(
                MetaWith(Fn("dbo", "CalcTax", UserFunctionType.Scalar)));
            Assert.AreEqual(0, p.GetCompletions(Make(isAfterFromKeyword: true)).Count);
        }

        [TestMethod]
        public void Scalar_AfterExec_ReturnsEmpty() {
            var p = new UserDefinedFunctionCompletionProvider(
                MetaWith(Fn("dbo", "CalcTax", UserFunctionType.Scalar)));
            Assert.AreEqual(0, p.GetCompletions(Make(isAfterExecKeyword: true)).Count);
        }

        [TestMethod]
        public void Scalar_EmptyConnectionKey_ReturnsEmpty() {
            var p = new UserDefinedFunctionCompletionProvider(
                MetaWith(Fn("dbo", "CalcTax", UserFunctionType.Scalar)));
            Assert.AreEqual(0, p.GetCompletions(Make(connectionKey: new ConnectionKey(""))).Count);
        }

        // ── Context gating — TVF ──────────────────────────────────────────────

        [TestMethod]
        public void Tvf_AfterFrom_IsReturned() {
            var p = new UserDefinedFunctionCompletionProvider(
                MetaWith(Fn("dbo", "GetOrders", UserFunctionType.TableValued)));
            Assert.AreEqual(1, p.GetCompletions(Make(isAfterFromKeyword: true)).Count);
        }

        [TestMethod]
        public void InlineTvf_AfterFrom_IsReturned() {
            var p = new UserDefinedFunctionCompletionProvider(
                MetaWith(Fn("dbo", "GetOrders", UserFunctionType.InlineTableValued)));
            Assert.AreEqual(1, p.GetCompletions(Make(isAfterFromKeyword: true)).Count);
        }

        [TestMethod]
        public void Tvf_NormalContext_ReturnsEmpty() {
            var p = new UserDefinedFunctionCompletionProvider(
                MetaWith(Fn("dbo", "GetOrders", UserFunctionType.TableValued)));
            Assert.AreEqual(0, p.GetCompletions(Make(isAfterFromKeyword: false)).Count);
        }

        [TestMethod]
        public void MixedTypes_AfterFrom_OnlyTvfsReturned() {
            var p = new UserDefinedFunctionCompletionProvider(MetaWith(
                Fn("dbo", "CalcTax",   UserFunctionType.Scalar),
                Fn("dbo", "GetOrders", UserFunctionType.TableValued)));
            var items = p.GetCompletions(Make(isAfterFromKeyword: true));
            Assert.AreEqual(1, items.Count);
            Assert.AreEqual("GetOrders", items[0].DisplayText);
        }

        [TestMethod]
        public void MixedTypes_NormalContext_OnlyScalarsReturned() {
            var p = new UserDefinedFunctionCompletionProvider(MetaWith(
                Fn("dbo", "CalcTax",   UserFunctionType.Scalar),
                Fn("dbo", "GetOrders", UserFunctionType.TableValued)));
            var items = p.GetCompletions(Make(isAfterFromKeyword: false));
            Assert.AreEqual(1, items.Count);
            Assert.AreEqual("CalcTax", items[0].DisplayText);
        }

        // ── Item shape ─────────────────────────────────────────────────────────

        [TestMethod]
        public void Kind_IsUserDefinedFunction() {
            var p = new UserDefinedFunctionCompletionProvider(
                MetaWith(Fn("dbo", "CalcTax", UserFunctionType.Scalar)));
            Assert.AreEqual(CompletionItemKind.UserDefinedFunction, p.GetCompletions(Make()).Single().Kind);
        }

        [TestMethod]
        public void InsertText_EndsWithOpenParen() {
            var p = new UserDefinedFunctionCompletionProvider(
                MetaWith(Fn("dbo", "CalcTax", UserFunctionType.Scalar)));
            var item = p.GetCompletions(Make()).Single();
            Assert.IsTrue(item.InsertText.EndsWith("("),
                $"Expected insert text ending with '(', got: '{item.InsertText}'");
        }

        [TestMethod]
        public void DboSchema_DisplayTextOmitsSchema() {
            var p = new UserDefinedFunctionCompletionProvider(
                MetaWith(Fn("dbo", "CalcTax", UserFunctionType.Scalar)));
            Assert.AreEqual("CalcTax", p.GetCompletions(Make()).Single().DisplayText);
        }

        [TestMethod]
        public void NonDboSchema_DisplayTextIncludesSchema() {
            var p = new UserDefinedFunctionCompletionProvider(
                MetaWith(Fn("finance", "CalcTax", UserFunctionType.Scalar)));
            Assert.AreEqual("finance.CalcTax", p.GetCompletions(Make()).Single().DisplayText);
        }

        // ── Description ────────────────────────────────────────────────────────

        [TestMethod]
        public void WithParameters_DescriptionContainsParamName() {
            var p = new UserDefinedFunctionCompletionProvider(
                MetaWith(Fn("dbo", "CalcTax", UserFunctionType.Scalar,
                    Param("@amount", "decimal"))));
            StringAssert.Contains(p.GetCompletions(Make()).Single().Description, "@amount");
        }

        [TestMethod]
        public void WithParameters_DescriptionContainsTypeName() {
            var p = new UserDefinedFunctionCompletionProvider(
                MetaWith(Fn("dbo", "CalcTax", UserFunctionType.Scalar,
                    Param("@amount", "decimal"))));
            StringAssert.Contains(p.GetCompletions(Make()).Single().Description, "decimal");
        }

        [TestMethod]
        public void NoParameters_DescriptionIsNotEmpty() {
            var p = new UserDefinedFunctionCompletionProvider(
                MetaWith(Fn("dbo", "GetDate", UserFunctionType.Scalar)));
            Assert.IsFalse(string.IsNullOrWhiteSpace(p.GetCompletions(Make()).Single().Description));
        }
    }
}
