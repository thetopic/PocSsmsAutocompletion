using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SsmsAutocompletion.Tests {

    [TestClass]
    public class StoredProcedureCompletionProviderTests {

        // ── Helpers ────────────────────────────────────────────────────────────

        private static ProcedureInfo Proc(
            string schema, string name, params ParameterInfo[] parameters) =>
            new ProcedureInfo(schema, name,
                new ReadOnlyCollection<ParameterInfo>(new List<ParameterInfo>(parameters)));

        private static ParameterInfo Param(
            string name, string type, bool isOutput = false, bool hasDefault = false) =>
            new ParameterInfo(name, type, isOutput, hasDefault);

        private static IDatabaseMetadata MetaWith(params ProcedureInfo[] procs) {
            var mock = new Mock<IDatabaseMetadata>();
            mock.Setup(m => m.GetProcedures(It.IsAny<ConnectionKey>()))
                .Returns(new List<ProcedureInfo>(procs).AsReadOnly() as IReadOnlyList<ProcedureInfo>);
            return mock.Object;
        }

        private static CompletionRequest Make(
            bool isAfterExecKeyword  = true,
            bool isDotContext        = false,
            ConnectionKey connectionKey = null) =>
            new CompletionRequest(
                sql: "EXEC ", caretPosition: 5, line: 1, column: 6,
                connectionKey: connectionKey ?? new ConnectionKey("Server|DB"),
                parseResult: null, metadataProvider: null,
                isDotContext: isDotContext, qualifier: null,
                isAfterFromKeyword: false,
                isJoinOnContext: false, isAfterJoinKeyword: false,
                isWhereContext: false, isAfterTableInFromJoin: false,
                tableNameBeforeCursor: null, snapshot: null,
                isAfterExecKeyword: isAfterExecKeyword);

        // ── Context gating ─────────────────────────────────────────────────────

        [TestMethod]
        public void NotAfterExec_ReturnsEmpty() {
            var p = new StoredProcedureCompletionProvider(
                MetaWith(Proc("dbo", "GetCustomers")));
            Assert.AreEqual(0, p.GetCompletions(Make(isAfterExecKeyword: false)).Count);
        }

        [TestMethod]
        public void DotContext_ReturnsEmpty() {
            var p = new StoredProcedureCompletionProvider(
                MetaWith(Proc("dbo", "GetCustomers")));
            Assert.AreEqual(0, p.GetCompletions(Make(isDotContext: true)).Count);
        }

        [TestMethod]
        public void EmptyConnectionKey_ReturnsEmpty() {
            var p = new StoredProcedureCompletionProvider(
                MetaWith(Proc("dbo", "GetCustomers")));
            Assert.AreEqual(0, p.GetCompletions(Make(connectionKey: new ConnectionKey(""))).Count);
        }

        // ── Item shape ─────────────────────────────────────────────────────────

        [TestMethod]
        public void ReturnsProcedure_KindIsStoredProcedure() {
            var p = new StoredProcedureCompletionProvider(
                MetaWith(Proc("dbo", "GetCustomers")));
            var item = p.GetCompletions(Make()).Single();
            Assert.AreEqual(CompletionItemKind.StoredProcedure, item.Kind);
        }

        [TestMethod]
        public void DboSchema_DisplayTextOmitsSchema() {
            var p = new StoredProcedureCompletionProvider(
                MetaWith(Proc("dbo", "GetCustomers")));
            var item = p.GetCompletions(Make()).Single();
            Assert.AreEqual("GetCustomers", item.DisplayText);
        }

        [TestMethod]
        public void NonDboSchema_DisplayTextIncludesSchema() {
            var p = new StoredProcedureCompletionProvider(
                MetaWith(Proc("sales", "GetCustomers")));
            var item = p.GetCompletions(Make()).Single();
            Assert.AreEqual("sales.GetCustomers", item.DisplayText);
        }

        [TestMethod]
        public void InsertText_EndsWithSpace() {
            var p = new StoredProcedureCompletionProvider(
                MetaWith(Proc("dbo", "GetCustomers")));
            var item = p.GetCompletions(Make()).Single();
            Assert.IsTrue(item.InsertText.EndsWith(" "),
                $"Expected insert text ending with space, got: '{item.InsertText}'");
        }

        // ── Description / parameter list ───────────────────────────────────────

        [TestMethod]
        public void NoParameters_DescriptionIndicatesNone() {
            var p = new StoredProcedureCompletionProvider(
                MetaWith(Proc("dbo", "GetAll")));
            var item = p.GetCompletions(Make()).Single();
            Assert.IsFalse(string.IsNullOrWhiteSpace(item.Description),
                "Description should not be empty even with no parameters");
        }

        [TestMethod]
        public void WithParameters_DescriptionContainsParamName() {
            var p = new StoredProcedureCompletionProvider(
                MetaWith(Proc("dbo", "GetCustomer", Param("@id", "int"))));
            var item = p.GetCompletions(Make()).Single();
            StringAssert.Contains(item.Description, "@id");
        }

        [TestMethod]
        public void WithParameters_DescriptionContainsTypeName() {
            var p = new StoredProcedureCompletionProvider(
                MetaWith(Proc("dbo", "GetCustomer", Param("@id", "int"))));
            var item = p.GetCompletions(Make()).Single();
            StringAssert.Contains(item.Description, "int");
        }

        [TestMethod]
        public void OutputParameter_DescriptionContainsOutputMarker() {
            var p = new StoredProcedureCompletionProvider(
                MetaWith(Proc("dbo", "Upsert",
                    Param("@id", "int", isOutput: true))));
            var item = p.GetCompletions(Make()).Single();
            StringAssert.Contains(item.Description, "OUTPUT");
        }

        [TestMethod]
        public void MultipleParameters_DescriptionContainsAll() {
            var p = new StoredProcedureCompletionProvider(
                MetaWith(Proc("dbo", "Search",
                    Param("@name",      "nvarchar"),
                    Param("@pageSize",  "int"),
                    Param("@total",     "int", isOutput: true))));
            var item = p.GetCompletions(Make()).Single();
            StringAssert.Contains(item.Description, "@name");
            StringAssert.Contains(item.Description, "@pageSize");
            StringAssert.Contains(item.Description, "@total");
        }

        // ── Multiple procedures ────────────────────────────────────────────────

        [TestMethod]
        public void MultipleProcedures_AllReturned() {
            var p = new StoredProcedureCompletionProvider(
                MetaWith(
                    Proc("dbo", "GetCustomers"),
                    Proc("dbo", "GetOrders"),
                    Proc("dbo", "DeleteCustomer")));
            Assert.AreEqual(3, p.GetCompletions(Make()).Count);
        }
    }
}
