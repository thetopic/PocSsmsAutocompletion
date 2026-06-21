using Microsoft.SqlServer.Management.SqlParser.Parser;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SsmsAutocompletion.Tests.Helpers;

namespace SsmsAutocompletion.Tests {

    [TestClass]
    public class SqlContextDetectorTests {

        private static readonly SsmsSqlParser      Parser   = new SsmsSqlParser();
        private static readonly SqlContextDetector Detector = new SqlContextDetector();

        private static ParseResult Parse(string sql) => Parser.Parse(sql);

        private static (int line, int col) Pos(string sql, int position) =>
            Parser.GetLineColumn(sql, position);

        // ══════════════════════════════════════════════════════════════════════
        // IsDotContext
        // ══════════════════════════════════════════════════════════════════════

        [TestMethod]
        public void IsDotContext_AfterDot_True() {
            // "t." — caret at position 2 (right after dot, nothing typed yet)
            string text = "t.";
            Assert.IsTrue(Detector.IsDotContext(SnapshotHelper.Make(text), text.Length));
        }

        [TestMethod]
        public void IsDotContext_AfterDotWithPartialWord_True() {
            // "table.col" — caret at end
            string text = "table.col";
            Assert.IsTrue(Detector.IsDotContext(SnapshotHelper.Make(text), text.Length));
        }

        [TestMethod]
        public void IsDotContext_NoQualifier_False() {
            string text = "SELECT ";
            Assert.IsFalse(Detector.IsDotContext(SnapshotHelper.Make(text), text.Length));
        }

        [TestMethod]
        public void IsDotContext_SpaceBeforeWord_False() {
            // "FROM table" — caret at end, no dot
            string text = "FROM table";
            Assert.IsFalse(Detector.IsDotContext(SnapshotHelper.Make(text), text.Length));
        }

        [TestMethod]
        public void IsDotContext_EmptyText_False() {
            Assert.IsFalse(Detector.IsDotContext(SnapshotHelper.Make(""), 0));
        }

        // ══════════════════════════════════════════════════════════════════════
        // GetQualifier
        // ══════════════════════════════════════════════════════════════════════

        [TestMethod]
        public void GetQualifier_AfterDot_ReturnsQualifier() {
            string text = "table.col";
            Assert.AreEqual("table", Detector.GetQualifier(SnapshotHelper.Make(text), text.Length));
        }

        [TestMethod]
        public void GetQualifier_NoDot_ReturnsNull() {
            string text = "table";
            Assert.IsNull(Detector.GetQualifier(SnapshotHelper.Make(text), text.Length));
        }

        [TestMethod]
        public void GetQualifier_EmptyAfterDot() {
            // "alias." — nothing typed after dot yet, caret at 6
            string text = "alias.";
            Assert.AreEqual("alias", Detector.GetQualifier(SnapshotHelper.Make(text), text.Length));
        }

        [TestMethod]
        public void GetQualifier_SchemaQualified() {
            // "dbo.Orders.col" → qualifier is "Orders"
            string text = "dbo.Orders.col";
            Assert.AreEqual("Orders", Detector.GetQualifier(SnapshotHelper.Make(text), text.Length));
        }

        // ══════════════════════════════════════════════════════════════════════
        // GetCurrentWord
        // ══════════════════════════════════════════════════════════════════════

        [TestMethod]
        public void GetCurrentWord_FullWord() {
            string text = "SELECT";
            Assert.AreEqual("SELECT", Detector.GetCurrentWord(SnapshotHelper.Make(text), text.Length));
        }

        [TestMethod]
        public void GetCurrentWord_PartialWord() {
            string text = "SEL";
            Assert.AreEqual("SEL", Detector.GetCurrentWord(SnapshotHelper.Make(text), text.Length));
        }

        [TestMethod]
        public void GetCurrentWord_AfterSpace() {
            // "FROM t" — caret at end, current word is "t"
            string text = "FROM t";
            Assert.AreEqual("t", Detector.GetCurrentWord(SnapshotHelper.Make(text), text.Length));
        }

        [TestMethod]
        public void GetCurrentWord_AtSpace_ReturnsEmpty() {
            // "FROM " — caret right after space (position 5), no word started
            string text = "FROM ";
            Assert.AreEqual("", Detector.GetCurrentWord(SnapshotHelper.Make(text), text.Length));
        }

        // ══════════════════════════════════════════════════════════════════════
        // GetWordBefore
        // ══════════════════════════════════════════════════════════════════════

        [TestMethod]
        public void GetWordBefore_SingleSpace() {
            string text = "FROM ";
            Assert.AreEqual("FROM", Detector.GetWordBefore(SnapshotHelper.Make(text), text.Length));
        }

        [TestMethod]
        public void GetWordBefore_MultipleSpaces() {
            string text = "FROM  ";
            Assert.AreEqual("FROM", Detector.GetWordBefore(SnapshotHelper.Make(text), text.Length));
        }

        [TestMethod]
        public void GetWordBefore_TwoWords() {
            // "SELECT FROM " → word before cursor is "FROM"
            string text = "SELECT FROM ";
            Assert.AreEqual("FROM", Detector.GetWordBefore(SnapshotHelper.Make(text), text.Length));
        }

        // ══════════════════════════════════════════════════════════════════════
        // IsAfterKeyword  (uses real TokenManager)
        // ══════════════════════════════════════════════════════════════════════

        [TestMethod]
        public void IsAfterKeyword_FROM_True() {
            string sql = "SELECT * FROM ";
            var result = Parse(sql);
            var (line, col) = Pos(sql, sql.Length);
            Assert.IsTrue(Detector.IsAfterKeyword(result, line, col, "FROM"));
        }

        [TestMethod]
        public void IsAfterKeyword_WHERE_True() {
            string sql = "SELECT * FROM Orders WHERE ";
            var result = Parse(sql);
            var (line, col) = Pos(sql, sql.Length);
            Assert.IsTrue(Detector.IsAfterKeyword(result, line, col, "WHERE"));
        }

        [TestMethod]
        public void IsAfterKeyword_JOIN_True() {
            string sql = "SELECT * FROM Orders o INNER JOIN ";
            var result = Parse(sql);
            var (line, col) = Pos(sql, sql.Length);
            Assert.IsTrue(Detector.IsAfterKeyword(result, line, col, "JOIN"));
        }

        [TestMethod]
        public void IsAfterKeyword_WrongKeyword_False() {
            string sql = "SELECT * FROM ";
            var result = Parse(sql);
            var (line, col) = Pos(sql, sql.Length);
            Assert.IsFalse(Detector.IsAfterKeyword(result, line, col, "WHERE"));
        }

        [TestMethod]
        public void IsAfterKeyword_NullParseResult_False() =>
            Assert.IsFalse(Detector.IsAfterKeyword(null, 1, 1, "FROM"));

        // ══════════════════════════════════════════════════════════════════════
        // IsInsideWhereClause  (uses real TokenManager)
        // ══════════════════════════════════════════════════════════════════════

        [TestMethod]
        public void IsInsideWhereClause_True() {
            string sql = "SELECT * FROM Orders WHERE Id = ";
            var result = Parse(sql);
            var (line, col) = Pos(sql, sql.Length);
            Assert.IsTrue(Detector.IsInsideWhereClause(result, line, col));
        }

        [TestMethod]
        public void IsInsideWhereClause_BeforeWhere_False() {
            string sql = "SELECT * FROM Orders ";
            var result = Parse(sql);
            var (line, col) = Pos(sql, sql.Length);
            Assert.IsFalse(Detector.IsInsideWhereClause(result, line, col));
        }

        [TestMethod]
        public void IsInsideWhereClause_InFromClause_False() {
            string sql = "SELECT * FROM ";
            var result = Parse(sql);
            var (line, col) = Pos(sql, sql.Length);
            Assert.IsFalse(Detector.IsInsideWhereClause(result, line, col));
        }

        [TestMethod]
        public void IsInsideWhereClause_NullParseResult_False() =>
            Assert.IsFalse(Detector.IsInsideWhereClause(null, 1, 1));

        // ══════════════════════════════════════════════════════════════════════
        // IsInsideGroupByClause  (uses real TokenManager)
        // ══════════════════════════════════════════════════════════════════════

        [TestMethod]
        public void IsInsideGroupByClause_True() {
            string sql = "SELECT a, COUNT(*) FROM Orders GROUP BY ";
            var result = Parse(sql);
            var (line, col) = Pos(sql, sql.Length);
            Assert.IsTrue(Detector.IsInsideGroupByClause(result, line, col));
        }

        [TestMethod]
        public void IsInsideGroupByClause_BeforeGroupBy_False() {
            string sql = "SELECT a FROM Orders ";
            var result = Parse(sql);
            var (line, col) = Pos(sql, sql.Length);
            Assert.IsFalse(Detector.IsInsideGroupByClause(result, line, col));
        }

        [TestMethod]
        public void IsInsideGroupByClause_AfterHaving_False() {
            string sql = "SELECT a FROM Orders GROUP BY a HAVING COUNT(*) > ";
            var result = Parse(sql);
            var (line, col) = Pos(sql, sql.Length);
            Assert.IsFalse(Detector.IsInsideGroupByClause(result, line, col));
        }

        [TestMethod]
        public void IsInsideGroupByClause_InWhereClause_False() {
            string sql = "SELECT a FROM Orders WHERE ";
            var result = Parse(sql);
            var (line, col) = Pos(sql, sql.Length);
            Assert.IsFalse(Detector.IsInsideGroupByClause(result, line, col));
        }

        [TestMethod]
        public void IsInsideGroupByClause_NullParseResult_False() =>
            Assert.IsFalse(Detector.IsInsideGroupByClause(null, 1, 1));

        // ══════════════════════════════════════════════════════════════════════
        // IsInsideHavingClause  (uses real TokenManager)
        // ══════════════════════════════════════════════════════════════════════

        [TestMethod]
        public void IsInsideHavingClause_True() {
            string sql = "SELECT a, COUNT(*) FROM Orders GROUP BY a HAVING ";
            var result = Parse(sql);
            var (line, col) = Pos(sql, sql.Length);
            Assert.IsTrue(Detector.IsInsideHavingClause(result, line, col));
        }

        [TestMethod]
        public void IsInsideHavingClause_InGroupBy_False() {
            string sql = "SELECT a, COUNT(*) FROM Orders GROUP BY ";
            var result = Parse(sql);
            var (line, col) = Pos(sql, sql.Length);
            Assert.IsFalse(Detector.IsInsideHavingClause(result, line, col));
        }

        [TestMethod]
        public void IsInsideHavingClause_AfterOrderBy_False() {
            string sql = "SELECT a FROM Orders GROUP BY a HAVING COUNT(*) > 1 ORDER BY ";
            var result = Parse(sql);
            var (line, col) = Pos(sql, sql.Length);
            Assert.IsFalse(Detector.IsInsideHavingClause(result, line, col));
        }

        [TestMethod]
        public void IsInsideHavingClause_NullParseResult_False() =>
            Assert.IsFalse(Detector.IsInsideHavingClause(null, 1, 1));

        // ══════════════════════════════════════════════════════════════════════
        // IsInsideOrderByClause  (uses real TokenManager)
        // ══════════════════════════════════════════════════════════════════════

        [TestMethod]
        public void IsInsideOrderByClause_True() {
            string sql = "SELECT a FROM Orders ORDER BY ";
            var result = Parse(sql);
            var (line, col) = Pos(sql, sql.Length);
            Assert.IsTrue(Detector.IsInsideOrderByClause(result, line, col));
        }

        [TestMethod]
        public void IsInsideOrderByClause_BeforeOrderBy_False() {
            string sql = "SELECT a FROM Orders ";
            var result = Parse(sql);
            var (line, col) = Pos(sql, sql.Length);
            Assert.IsFalse(Detector.IsInsideOrderByClause(result, line, col));
        }

        [TestMethod]
        public void IsInsideOrderByClause_AfterUnion_False() {
            string sql = "SELECT a FROM Orders ORDER BY a UNION SELECT a FROM Other ";
            var result = Parse(sql);
            var (line, col) = Pos(sql, sql.Length);
            Assert.IsFalse(Detector.IsInsideOrderByClause(result, line, col));
        }

        [TestMethod]
        public void IsInsideOrderByClause_NullParseResult_False() =>
            Assert.IsFalse(Detector.IsInsideOrderByClause(null, 1, 1));

        // ══════════════════════════════════════════════════════════════════════
        // DetectAliasContext  (uses real TokenManager)
        // ══════════════════════════════════════════════════════════════════════

        [TestMethod]
        public void DetectAliasContext_AfterTableInFrom_True() {
            string sql = "SELECT * FROM Orders ";
            var result = Parse(sql);
            var (line, col) = Pos(sql, sql.Length);
            var (isAlias, tableName) = Detector.DetectAliasContext(result, line, col);
            Assert.IsTrue(isAlias);
            Assert.AreEqual("Orders", tableName);
        }

        [TestMethod]
        public void DetectAliasContext_AfterTableInJoin_True() {
            string sql = "SELECT * FROM Customers c JOIN Orders ";
            var result = Parse(sql);
            var (line, col) = Pos(sql, sql.Length);
            var (isAlias, tableName) = Detector.DetectAliasContext(result, line, col);
            Assert.IsTrue(isAlias);
            Assert.AreEqual("Orders", tableName);
        }

        [TestMethod]
        public void DetectAliasContext_AfterAs_True() {
            string sql = "SELECT * FROM Orders AS ";
            var result = Parse(sql);
            var (line, col) = Pos(sql, sql.Length);
            var (isAlias, tableName) = Detector.DetectAliasContext(result, line, col);
            Assert.IsTrue(isAlias);
        }

        [TestMethod]
        public void DetectAliasContext_InWhereClause_False() {
            string sql = "SELECT * FROM Orders o WHERE o.Id = ";
            var result = Parse(sql);
            var (line, col) = Pos(sql, sql.Length);
            var (isAlias, _) = Detector.DetectAliasContext(result, line, col);
            Assert.IsFalse(isAlias);
        }

        [TestMethod]
        public void DetectAliasContext_NullParseResult_False() {
            var (isAlias, _) = Detector.DetectAliasContext(null, 1, 1);
            Assert.IsFalse(isAlias);
        }

        // ══════════════════════════════════════════════════════════════════════
        // IsAfterExecKeyword  (uses real TokenManager)
        // ══════════════════════════════════════════════════════════════════════

        [TestMethod]
        public void IsAfterExecKeyword_AfterExec_True() {
            string sql = "EXEC ";
            var result = Parse(sql);
            var (line, col) = Pos(sql, sql.Length);
            Assert.IsTrue(Detector.IsAfterExecKeyword(result, line, col));
        }

        [TestMethod]
        public void IsAfterExecKeyword_AfterExecute_True() {
            string sql = "EXECUTE ";
            var result = Parse(sql);
            var (line, col) = Pos(sql, sql.Length);
            Assert.IsTrue(Detector.IsAfterExecKeyword(result, line, col));
        }

        [TestMethod]
        public void IsAfterExecKeyword_PartialProcName_True() {
            string sql = "EXEC GetCust";
            var result = Parse(sql);
            var (line, col) = Pos(sql, sql.Length);
            Assert.IsTrue(Detector.IsAfterExecKeyword(result, line, col));
        }

        [TestMethod]
        public void IsAfterExecKeyword_AfterSelect_False() {
            string sql = "SELECT ";
            var result = Parse(sql);
            var (line, col) = Pos(sql, sql.Length);
            Assert.IsFalse(Detector.IsAfterExecKeyword(result, line, col));
        }

        [TestMethod]
        public void IsAfterExecKeyword_AfterFrom_False() {
            string sql = "SELECT * FROM ";
            var result = Parse(sql);
            var (line, col) = Pos(sql, sql.Length);
            Assert.IsFalse(Detector.IsAfterExecKeyword(result, line, col));
        }

        [TestMethod]
        public void IsAfterExecKeyword_NullParseResult_False() =>
            Assert.IsFalse(Detector.IsAfterExecKeyword(null, 1, 1));

        // ══════════════════════════════════════════════════════════════════════
        // GetLineColumn  (via snapshot)
        // ══════════════════════════════════════════════════════════════════════

        [TestMethod]
        public void GetLineColumn_SingleLine_Position0() {
            var snap = SnapshotHelper.Make("SELECT");
            var (line, col) = Detector.GetLineColumn(snap, 0);
            Assert.AreEqual(1, line);
            Assert.AreEqual(1, col);
        }

        [TestMethod]
        public void GetLineColumn_MultiLine_SecondLine() {
            string text = "SELECT\nFROM t";
            var snap = SnapshotHelper.Make(text);
            // position 7 = start of "FROM t"
            var (line, col) = Detector.GetLineColumn(snap, 7);
            Assert.AreEqual(2, line);
            Assert.AreEqual(1, col);
        }
    }
}
