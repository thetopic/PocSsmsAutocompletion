using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SsmsAutocompletion.Tests {

    [TestClass]
    public class StoredProcedureParameterCompletionProviderTests {

        // ── Helpers ────────────────────────────────────────────────────────────

        private static ProcedureInfo Proc(string schema, string name, params ParameterInfo[] parms) =>
            new ProcedureInfo(schema, name,
                new ReadOnlyCollection<ParameterInfo>(new List<ParameterInfo>(parms)));

        private static ParameterInfo Param(string name, string type = "int",
            bool isOutput = false, bool hasDefault = false) =>
            new ParameterInfo(name, type, isOutput, hasDefault);

        private static IDatabaseMetadata MetaWith(params ProcedureInfo[] procs) {
            var mock = new Mock<IDatabaseMetadata>();
            mock.Setup(m => m.GetProcedures(It.IsAny<ConnectionKey>()))
                .Returns(new List<ProcedureInfo>(procs).AsReadOnly() as IReadOnlyList<ProcedureInfo>);
            return mock.Object;
        }

        private static CompletionRequest Make(
            bool isInsideProcedureCall = true,
            string procedureName = "dbo.MyProc",
            IReadOnlyList<string> alreadyProvided = null,
            ConnectionKey key = null) =>
            new CompletionRequest(
                sql: "EXEC dbo.MyProc ", caretPosition: 17, line: 1, column: 18,
                connectionKey: key ?? new ConnectionKey("S|DB"),
                parseResult: null, metadataProvider: null,
                isDotContext: false, qualifier: null,
                isAfterFromKeyword: false, isJoinOnContext: false, isAfterJoinKeyword: false,
                isWhereContext: false, isAfterTableInFromJoin: false,
                tableNameBeforeCursor: null, snapshot: null,
                isAfterExecKeyword: false,
                isInsideProcedureCall: isInsideProcedureCall,
                procedureNameBeforeCursor: procedureName,
                alreadyProvidedParameters: alreadyProvided ?? System.Array.Empty<string>());

        // ── Context gating ─────────────────────────────────────────────────────

        [TestMethod]
        public void NotInsideProcedureCall_ReturnsEmpty() {
            var p = new StoredProcedureParameterCompletionProvider(
                MetaWith(Proc("dbo", "MyProc", Param("@id"))));
            Assert.AreEqual(0, p.GetCompletions(Make(isInsideProcedureCall: false)).Count);
        }

        [TestMethod]
        public void EmptyConnectionKey_ReturnsEmpty() {
            var p = new StoredProcedureParameterCompletionProvider(
                MetaWith(Proc("dbo", "MyProc", Param("@id"))));
            Assert.AreEqual(0, p.GetCompletions(Make(key: new ConnectionKey(""))).Count);
        }

        [TestMethod]
        public void ProcNotInCache_ReturnsEmpty() {
            var p = new StoredProcedureParameterCompletionProvider(MetaWith(/* empty */));
            Assert.AreEqual(0, p.GetCompletions(Make()).Count);
        }

        // ── All parameters shown on empty call ────────────────────────────────

        [TestMethod]
        public void EmptyCall_ShowsAllParams() {
            var p = new StoredProcedureParameterCompletionProvider(
                MetaWith(Proc("dbo", "MyProc",
                    Param("@id"), Param("@name", "nvarchar"))));
            var result = p.GetCompletions(Make());
            Assert.AreEqual(2, result.Count);
        }

        [TestMethod]
        public void Items_HaveParameterKind() {
            var p = new StoredProcedureParameterCompletionProvider(
                MetaWith(Proc("dbo", "MyProc", Param("@id"))));
            var item = p.GetCompletions(Make()).Single();
            Assert.AreEqual(CompletionItemKind.Parameter, item.Kind);
        }

        [TestMethod]
        public void Items_InsertTextEndsWithEquals() {
            var p = new StoredProcedureParameterCompletionProvider(
                MetaWith(Proc("dbo", "MyProc", Param("@id"))));
            var item = p.GetCompletions(Make()).Single();
            StringAssert.Contains(item.InsertText, "=");
            Assert.AreEqual("@id = ", item.InsertText);
        }

        // ── Already-provided params are excluded ──────────────────────────────

        [TestMethod]
        public void AlreadyProvided_Excluded() {
            var p = new StoredProcedureParameterCompletionProvider(
                MetaWith(Proc("dbo", "MyProc",
                    Param("@id"), Param("@name", "nvarchar"))));
            var req = Make(alreadyProvided: new[] { "@id" });
            var result = p.GetCompletions(req);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("@name", result[0].DisplayText);
        }

        [TestMethod]
        public void AlreadyProvidedCaseInsensitive_Excluded() {
            var p = new StoredProcedureParameterCompletionProvider(
                MetaWith(Proc("dbo", "MyProc", Param("@ID"))));
            var req = Make(alreadyProvided: new[] { "@id" });
            Assert.AreEqual(0, p.GetCompletions(req).Count);
        }

        // ── Schema-qualified proc lookup ──────────────────────────────────────

        [TestMethod]
        public void ProcLookup_SchemaQualified() {
            var p = new StoredProcedureParameterCompletionProvider(
                MetaWith(Proc("sales", "GetOrders", Param("@from", "date"))));
            var req = Make(procedureName: "sales.GetOrders");
            var result = p.GetCompletions(req);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("@from", result[0].DisplayText);
        }
    }
}
