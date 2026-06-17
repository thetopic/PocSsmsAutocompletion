using Microsoft.SqlServer.Management.SqlParser.Parser;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SsmsAutocompletion.Tests {

    [TestClass]
    public class SchemaCompletionProviderTests {

        // ── Helpers ────────────────────────────────────────────────────────────

        private static IDatabaseMetadata MetaWith(
            string[] schemas = null,
            TableInfo[] tables = null) {
            var mock = new Mock<IDatabaseMetadata>();
            mock.Setup(m => m.GetSchemas(It.IsAny<ConnectionKey>()))
                .Returns(new ReadOnlyCollection<string>(
                    new List<string>(schemas ?? System.Array.Empty<string>()))
                    as IReadOnlyList<string>);
            mock.Setup(m => m.GetTables(It.IsAny<ConnectionKey>()))
                .Returns(new ReadOnlyCollection<TableInfo>(
                    new List<TableInfo>(tables ?? System.Array.Empty<TableInfo>()))
                    as IReadOnlyList<TableInfo>);
            return mock.Object;
        }

        private static CompletionRequest Make(
            bool isDotContext = true,
            string qualifier = "hr",
            ConnectionKey key = null,
            ParseResult parseResult = null) =>
            new CompletionRequest(
                sql: qualifier + ".", caretPosition: qualifier.Length + 1,
                line: 1, column: qualifier.Length + 2,
                connectionKey: key ?? new ConnectionKey("S|DB"),
                parseResult: parseResult, metadataProvider: null,
                isDotContext: isDotContext, qualifier: isDotContext ? qualifier : null,
                isAfterFromKeyword: false, isJoinOnContext: false, isAfterJoinKeyword: false,
                isWhereContext: false, isAfterTableInFromJoin: false,
                tableNameBeforeCursor: null, snapshot: null);

        private static SchemaCompletionProvider MakeProvider(
            string[] schemas = null, TableInfo[] tables = null) =>
            new SchemaCompletionProvider(
                MetaWith(schemas, tables),
                new AliasExtractor(),
                new CteExtractor());

        // ── Context gating ─────────────────────────────────────────────────────

        [TestMethod]
        public void NoDotContext_ReturnsEmpty() {
            var p = MakeProvider(schemas: new[] { "hr" });
            Assert.AreEqual(0, p.GetCompletions(Make(isDotContext: false)).Count);
        }

        [TestMethod]
        public void EmptyConnectionKey_ReturnsEmpty() {
            var p = MakeProvider(schemas: new[] { "hr" });
            Assert.AreEqual(0, p.GetCompletions(Make(key: new ConnectionKey(""))).Count);
        }

        // ── Unrecognised qualifier triggers schema list ────────────────────────

        [TestMethod]
        public void UnrecognisedQualifier_ReturnsSchemas() {
            var p = MakeProvider(schemas: new[] { "hr", "sales" });
            // qualifier="unknown" is not a known table/view/alias/CTE
            var result = p.GetCompletions(Make(qualifier: "unknown"));
            Assert.AreEqual(2, result.Count);
        }

        [TestMethod]
        public void SchemaItems_HaveSchemaKind() {
            var p = MakeProvider(schemas: new[] { "hr" });
            var item = p.GetCompletions(Make(qualifier: "unknown")).Single();
            Assert.AreEqual(CompletionItemKind.Schema, item.Kind);
        }

        // ── Known table qualifier returns empty ────────────────────────────────

        [TestMethod]
        public void KnownTableQualifier_ReturnsEmpty() {
            // qualifier matches a table name → ColumnCompletionProvider handles it
            var p = MakeProvider(
                schemas: new[] { "dbo" },
                tables: new[] { new TableInfo("dbo", "Orders") });
            var result = p.GetCompletions(Make(qualifier: "Orders"));
            Assert.AreEqual(0, result.Count);
        }

        // ── Empty schemas on cache miss ────────────────────────────────────────

        [TestMethod]
        public void CacheMiss_ReturnsEmpty() {
            var p = MakeProvider(schemas: System.Array.Empty<string>());
            Assert.AreEqual(0, p.GetCompletions(Make(qualifier: "unknown")).Count);
        }
    }
}
