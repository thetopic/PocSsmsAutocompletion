## Context

The extension follows a two-tier completion model:
- **Tier 1 — SSMS DLL** (`NativeCompletionProvider`): calls `Resolver.FindCompletions()` from `Microsoft.SqlServer.Management.SqlParser.Intellisense`. Works well for general identifiers in SELECT, WHERE, and JOIN contexts when the metadata provider is initialised.
- **Tier 2 — Custom providers**: each handles one specific editing position that the DLL misses or handles poorly at partial-parse time (FK joins, aliases, CTEs, EXEC parameters).

Three positions currently have no Tier 2 coverage and degrade silently when the resolver is cold: INSERT column lists, UPDATE SET clauses, and bare-word column access in SELECT/WHERE.

This design adds one custom provider per gap, following the exact same pattern as the existing providers.

```
SQL editing position → Who handles it
─────────────────────────────────────────────────────────────────────
SELECT t.col         → ColumnCompletionProvider (dot/qualifier)
SELECT col           → NativeCompletionProvider + UnqualifiedColumnCompletionProvider (NEW)
FROM table           → TableCompletionProvider
FROM t JOIN ...      → InlineJoinCompletionProvider, FkJoinCompletionProvider
WHERE t.col ...      → ColumnCompletionProvider (dot/qualifier)
WHERE col ...        → NativeCompletionProvider + UnqualifiedColumnCompletionProvider (NEW)
INSERT INTO t (col)  → InsertColumnCompletionProvider (NEW)
UPDATE t SET col =   → UpdateSetCompletionProvider (NEW)
EXEC proc            → StoredProcedureCompletionProvider
EXEC proc @p =       → (ssms-dll-sql-autocompletion change)
SELECT func()        → FunctionCompletionProvider, UserDefinedFunctionCompletionProvider
keyword              → KeywordCompletionProvider
```

## Goals / Non-Goals

**Goals:**
- Column completion in INSERT INTO column lists (explicit column names, not values).
- Column completion after UPDATE ... SET and between commas in the SET clause.
- Unqualified column suggestions drawn from all FROM-clause tables in SELECT and WHERE positions.
- All three providers work from the local cache; they degrade gracefully (return empty) when the cache is cold, and let the native resolver take over.

**Non-Goals:**
- INSERT VALUES completion (value type inference is out of scope).
- UPDATE SET value completion.
- Sub-query FROM clause analysis for unqualified column resolution (only top-level FROM tables are considered in v1).
- MERGE statement column completion.

## Decisions

### D1 — AST-based INSERT/UPDATE target detection

**Decision**: Use the SSMS `SqlCodeDom` AST (`ParseResult.Script`) to detect `SqlInsertStatement` and `SqlUpdateStatement` and extract the target table name. Populate `IsInsertColumnList`, `IsUpdateSetClause`, and `InsertUpdateTargetTable` (string?) in `CompletionRequestBuilder`.

**Rationale**: The AST already holds a fully-typed tree. `SqlInsertStatement.Target` gives the table reference; `SqlUpdateStatement.Target` gives it for UPDATE. Both are structurally distinct from SELECT and FROM. Text-based detection would be fragile for multi-line DML.

**Alternative considered**: Token-based backward scan (same pattern as `GetWordBefore`). Rejected — INSERT/UPDATE target tables may be many tokens behind the cursor (aliases, schema prefix, etc.). AST lookup is O(statements) and always correct.

### D2 — `IsInsertColumnList` detection: position between the outer parentheses

**Decision**: A cursor is considered inside the column list when it falls between the opening `(` after the table name and the matching `)`, and the enclosing statement is a `SqlInsertStatement`. Detect this by checking if `ParseResult.Script` contains an `InsertStatement` whose column-list token range contains `(line, column)`.

**Rationale**: The SSMS parser marks the column list as a child of `SqlInsertStatement.Columns`. Using the token range avoids having to reparse or regex-scan the SQL text.

### D3 — Unqualified column provider: fan-out over all FROM tables

**Decision**: `UnqualifiedColumnCompletionProvider` collects all aliases/table names from `AliasExtractor.GetAliases()` (already used by `ColumnCompletionProvider`), then calls `GetColumns(key, schema, tableName)` for each, returning them all merged. It fires only when `!IsDotContext` and (`IsWhereContext` or `IsInSelectList`).

**Rationale**: Reusing `AliasExtractor` means the same FROM-clause resolution logic covers both qualified and unqualified paths without duplication.

**Alternative considered**: Re-expose all columns through `NativeCompletionProvider` by removing its guards. Rejected — the native resolver is opaque and we can't control deduplication or ranking; it also fires for keywords and types we don't want here.

### D4 — `IsInSelectList` flag: text-based keyword scan

**Decision**: `IsInSelectList` is true when the nearest preceding clause-level keyword (scanning backward from the cursor) is `SELECT`, not `FROM`, `WHERE`, `JOIN`, `ON`, `GROUP`, `ORDER`, `HAVING`, or `SET`. Use a text-based scan (same pattern as `IsAfterFromKeyword`) because it's reliable in SSMS's live editor.

**Rationale**: The parser's `SqlSelectClause` range could be used, but SELECT lists can span many lines and the token range check would require walking the full AST. The keyword scan is O(chars scanned) and negligible cost.

### D5 — Already-set column exclusion in UPDATE SET

**Decision**: `UpdateSetCompletionProvider` uses a lightweight text-based scan to collect column names that already appear in the `SET` clause before the cursor, then subtracts that set from the suggestions.

**Rationale**: Prevents offering columns that would create a duplicate SET assignment (a SQL error). Mirrors the parameter-exclusion logic used by `StoredProcedureParameterCompletionProvider` (from the `ssms-dll-sql-autocompletion` change).

## Risks / Trade-offs

- **AST cursor-position correlation** → The SSMS parser gives a `ParseResult` for the full SQL text. When the user is mid-typing, the parse may be incomplete. Mitigation: if the INSERT/UPDATE AST walk fails, fall back to returning empty (same pattern as all existing providers).
- **Unqualified columns are noisy for wide tables** → A table with 50 columns floods the popup. Mitigation: prefix filtering in `CompletionEngine.FilterByPrefix` already cuts this down as the user types.
- **Provider ordering** → `UnqualifiedColumnCompletionProvider` must fire before `KeywordCompletionProvider` (to put columns above keywords) but after `ColumnCompletionProvider` (to avoid double-suggesting qualified paths). Registration order in `BuildProviders()` enforces this.
- **`IsInSelectList` false positives for nested subqueries** → A cursor inside `WHERE x IN (SELECT `)` would be detected as SELECT context. That's acceptable and desirable — columns are valid there.

## Open Questions

- Should `UnqualifiedColumnCompletionProvider` also fire in HAVING clauses? (HAVING is close to WHERE in intent — lean yes, but mark as a follow-up.)
- For INSERT without an explicit column list (`INSERT INTO t VALUES (...)`), should we complete in the VALUES? (Out of scope for now — value type inference is hard.)
