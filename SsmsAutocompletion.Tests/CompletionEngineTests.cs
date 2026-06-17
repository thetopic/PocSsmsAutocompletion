using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SsmsAutocompletion.Tests {

    [TestClass]
    public class CompletionEngineTests {

        // ══════════════════════════════════════════════════════════════════════
        // CanonicalJoinKey — tests via the internal static helper
        // ══════════════════════════════════════════════════════════════════════

        [TestMethod]
        public void CanonicalJoinKey_SwappedOperands_SameKey() {
            string a = CompletionEngine.CanonicalJoinKey("o.CustomerID = c.CustomerID");
            string b = CompletionEngine.CanonicalJoinKey("c.CustomerID = o.CustomerID");
            Assert.AreEqual(a, b);
        }

        [TestMethod]
        public void CanonicalJoinKey_ReorderedAndClauses_SameKey() {
            string a = CompletionEngine.CanonicalJoinKey("a.Col1 = b.Col1 AND a.Col2 = b.Col2");
            string b = CompletionEngine.CanonicalJoinKey("b.Col2 = a.Col2 AND b.Col1 = a.Col1");
            Assert.AreEqual(a, b);
        }

        [TestMethod]
        public void CanonicalJoinKey_DifferentColumns_DifferentKeys() {
            string a = CompletionEngine.CanonicalJoinKey("o.OrderID = c.OrderID");
            string b = CompletionEngine.CanonicalJoinKey("o.CustomerID = c.CustomerID");
            Assert.AreNotEqual(a, b);
        }

        [TestMethod]
        public void CanonicalJoinKey_UnrecognisedShape_ReturnsSameText() {
            string raw = "some weird text";
            Assert.AreEqual(raw, CompletionEngine.CanonicalJoinKey(raw));
        }

        // ══════════════════════════════════════════════════════════════════════
        // Deduplication — Join items use canonical key, others use exact text
        // ══════════════════════════════════════════════════════════════════════

        private static CompletionItem Join(string display) =>
            new CompletionItem(display, display, "Join", CompletionItemKind.Join);

        private static CompletionItem Table(string display) =>
            new CompletionItem(display, display, "Table", CompletionItemKind.Table);

        private static CompletionItem Column(string display) =>
            new CompletionItem(display, display, "Column", CompletionItemKind.Column);

        private static System.Collections.Generic.IReadOnlyList<CompletionItem>
            RunEngine(params CompletionItem[] items) {
            var provider = new FixedItemsProvider(items);
            var meta     = new Moq.Mock<IDatabaseMetadata>();
            var builder  = new CompletionRequestBuilder(
                new SsmsSqlParser(), new SqlContextDetector(), meta.Object);
            var engine   = new CompletionEngine(
                new[] { provider },
                builder,
                new SqlContextDetector());
            var snap     = SsmsAutocompletion.Tests.Helpers.SnapshotHelper.Make("");
            return engine.GetCompletions(snap, "", 0, null);
        }

        [TestMethod]
        public void JoinDedup_SwappedOperandsDedupedToOne() {
            var result = RunEngine(
                Join("o.CustomerID = c.CustomerID"),
                Join("c.CustomerID = o.CustomerID"));
            Assert.AreEqual(1, result.Count);
        }

        [TestMethod]
        public void JoinDedup_ReorderedAndClausesDedupedToOne() {
            var result = RunEngine(
                Join("a.Col1 = b.Col1 AND a.Col2 = b.Col2"),
                Join("b.Col2 = a.Col2 AND b.Col1 = a.Col1"));
            Assert.AreEqual(1, result.Count);
        }

        [TestMethod]
        public void JoinDedup_GenuinelyDifferentConditions_BothKept() {
            var result = RunEngine(
                Join("o.CustomerID = c.CustomerID"),
                Join("o.OrderID = d.OrderID"));
            Assert.AreEqual(2, result.Count);
        }

        [TestMethod]
        public void JoinDedup_NonJoinItemsUseExactText_BothKept() {
            var result = RunEngine(
                Table("Orders"),
                Column("OrderID"));
            Assert.AreEqual(2, result.Count);
        }

        [TestMethod]
        public void JoinDedup_UnrecognisedShape_FallsBackToRawText() {
            // Two join items with the same unrecognised shape should dedup by raw text
            var result = RunEngine(
                Join("some weird text"),
                Join("some weird text"));
            Assert.AreEqual(1, result.Count);
        }

        // ══════════════════════════════════════════════════════════════════════
        // Rank-based stable ordering
        // ══════════════════════════════════════════════════════════════════════

        private static CompletionItem Ranked(string display, int rank) =>
            new CompletionItem(display, display, "", CompletionItemKind.Keyword, rank);

        [TestMethod]
        public void Rank1MovesAfterRank0() {
            // Provider returns Rank=1 item first, but it should appear after Rank=0 items
            var result = RunEngine(
                Ranked("B", 1),
                Ranked("A", 0));
            Assert.AreEqual("A", result[0].DisplayText);
            Assert.AreEqual("B", result[1].DisplayText);
        }

        [TestMethod]
        public void Rank0ItemsPreserveRelativeOrder() {
            var result = RunEngine(
                Ranked("X", 0),
                Ranked("Y", 0),
                Ranked("Z", 0));
            Assert.AreEqual("X", result[0].DisplayText);
            Assert.AreEqual("Y", result[1].DisplayText);
            Assert.AreEqual("Z", result[2].DisplayText);
        }

        [TestMethod]
        public void Rank1ItemsPreserveRelativeOrder() {
            var result = RunEngine(
                Ranked("R1a", 1),
                Ranked("R0",  0),
                Ranked("R1b", 1));
            Assert.AreEqual("R0",  result[0].DisplayText);
            Assert.AreEqual("R1a", result[1].DisplayText);
            Assert.AreEqual("R1b", result[2].DisplayText);
        }

        // ── Helper ────────────────────────────────────────────────────────────

        private sealed class FixedItemsProvider : ICompletionProvider {
            private readonly CompletionItem[] _items;
            public FixedItemsProvider(CompletionItem[] items) { _items = items; }
            public System.Collections.Generic.IReadOnlyList<CompletionItem> GetCompletions(
                CompletionRequest request) => _items;
        }
    }
}
