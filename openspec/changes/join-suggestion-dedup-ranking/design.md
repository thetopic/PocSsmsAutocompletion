## Context

`CompletionEngine.GetCompletions` runs every registered provider, concatenates their results (`CollectAllItems`), removes exact-`DisplayText` duplicates (`Deduplicate`), then filters by the current prefix.

Two providers can both produce `CompletionItemKind.Join` items for the same table pair when the cursor is in a JOIN `ON` clause or a `WHERE` clause:

- `FkJoinCompletionProvider` — for every pair of tables in the query, emits one item per real FK relation: `aliasFk.Col1 = aliasPk.Col1[ AND aliasFk.Col2 = aliasPk.Col2 ...]`, description `"Relation FK/PK"`.
- `SimilarColumnJoinCompletionProvider` — for every pair of tables, emits one item per pair of identically/similarly named columns: `aliasA.Col = aliasB.Col`, description `"Colonnes similaires"`.

Registration order (`BuildProviders()`) currently places `FkJoinCompletionProvider` before `SimilarColumnJoinCompletionProvider`, so when both emit the *exact same* `DisplayText`, the FK-derived item wins today's `Deduplicate`. But this only works when:
1. The operand order matches (`aliasA.Col = aliasB.Col` on both sides, not reversed), and
2. The FK relation is single-column (a composite FK produces one multi-clause AND item that won't string-match the per-column items from the similarity provider).

## Goals / Non-Goals

**Goals:**
- Collapse semantically identical `Join`-kind suggestions regardless of operand order.
- Stop `SimilarColumnJoinCompletionProvider` from proposing column pairs that duplicate an FK relation (including composite FKs) between the same two tables.
- Ensure FK-based (and all other) suggestions are never pushed below name-similarity suggestions in the popup, independent of provider registration order.

**Non-Goals:**
- Multi-hop / junction-table join suggestions (separate concern).
- Self-join support.
- Refactoring the duplicated FK-traversal code shared by `FkJoinTableCompletionProvider` / `InlineJoinCompletionProvider` (separate concern; not required to implement this change, though `SimilarColumnJoinCompletionProvider`'s new FK-pair lookup will resemble `FkJoinCompletionProvider.AddFkConditionsForPair`).
- Changing the textual format of any existing suggestion's `DisplayText`/`InsertText`.

## Decisions

### D1 — Canonical dedup key for `Join`-kind items

**Decision**: In `CompletionEngine.Deduplicate`, for items where `Kind == CompletionItemKind.Join`, compute a canonical key from `DisplayText`:
1. Split on `" AND "` to get clauses.
2. Split each clause on `" = "` into two operands; sort the two operands ordinally and rejoin with `" = "`.
3. Sort the resulting clause list ordinally and rejoin with `" AND "`.

Use this canonical key (instead of `DisplayText`) as the dedup key for `Join` items. Non-`Join` items continue to use `DisplayText` as today. If a `Join` item's `DisplayText` doesn't match the expected `"X = Y[ AND X = Y ...]"` shape (split doesn't produce exactly two operands per clause), fall back to using the raw `DisplayText` as the key — never throw.

**Rationale**: Both providers produce conditions in the `alias.Column = alias.Column` shape; only the left/right placement and AND-clause ordering can differ between an FK-derived multi-column condition and independently-ordered alias iteration in the similarity provider. Canonicalizing both sides collapses true duplicates while the **first-seen** item (by provider order, FK first) is the one whose original `DisplayText` is kept and shown to the user — preserving today's "FK wins the visible text" behavior.

**Alternative considered**: Parse `DisplayText` back into structured `(alias, column)` pairs via a shared model instead of string manipulation. Rejected — would require both providers to emit a structured representation alongside `DisplayText`, a larger change than needed; the string shape is simple and stable (both providers already build it via the same `"{alias}.{col} = {alias}.{col}"` pattern joined with `" AND "`).

### D2 — `SimilarColumnJoinCompletionProvider` excludes FK-covered column pairs

**Decision**: For each table pair `(tableA, tableB)`, before the existing column×column similarity loop, build a set of `(columnNameA, columnNameB)` pairs (case-insensitive) that are already related via an FK between `tableA` and `tableB` — checking both directions (`tableA` owns the FK referencing `tableB`, and vice versa), mirroring `FkJoinCompletionProvider.AddFkConditionsForPair`. While iterating `columnA × columnB`, skip a pair if `(columnA.ColumnName, columnB.ColumnName)` (in either order) is in that set.

**Rationale**: D1's dedup only collapses items with matching canonical text. A composite FK produces a *single* multi-clause item (`a.Col1 = b.Col1 AND a.Col2 = b.Col2`), which has no canonical-text overlap with the *separate* single-clause items the similarity provider would otherwise propose for `Col1` and `Col2`. Excluding at the source removes this noise for both single- and multi-column FKs, and avoids the wasted work of generating items that would just be thrown away.

**Alternative considered**: Rely solely on D1. Rejected for the composite-FK case explained above — D1 alone cannot detect that overlap.

### D3 — Rank-based ordering via stable sort

**Decision**: Add `public int Rank { get; }` to `CompletionItem`, as an optional constructor parameter defaulting to `0` (all existing call sites unchanged). After `Deduplicate`, `CompletionEngine.GetCompletions` performs a stable ordering by `Rank` (e.g. `items.OrderBy(i => i.Rank)`, which LINQ guarantees is stable) before `FilterByPrefix`. `SimilarColumnJoinCompletionProvider` sets `Rank = 1` on the items it still emits after D2's exclusion; every other provider's items remain at the default `Rank = 0`.

**Rationale**: A stable sort by `Rank` preserves the existing relative order of the (overwhelming majority) rank-0 items exactly as today, while guaranteeing that the residual heuristic similarity suggestions always sort after everything else — including FK-based join suggestions, tables, columns, etc. — regardless of future changes to provider registration order.

**Alternative considered**: Reorder `BuildProviders()` so `SimilarColumnJoinCompletionProvider` is registered last. Rejected — registration order also determines which item's `DisplayText` "wins" D1's dedup; conflating the two concerns (dedup priority vs. display rank) makes both harder to reason about. A dedicated `Rank` field documents ordering intent explicitly.

## Risks / Trade-offs

- **Canonical-key parsing assumes the `"alias.Column = alias.Column[ AND ...]"` shape.** If a future provider emits a `Join`-kind item in a different shape (e.g. involving `BETWEEN`, function calls, or `OR`), the parser must fall back to raw `DisplayText` rather than throwing or mis-deduping — covered by the fallback in D1.
- **D2 adds extra `GetForeignKeys` calls** per table pair in `SimilarColumnJoinCompletionProvider`. Mitigated: `GetForeignKeys` reads from the existing 10-minute TTL `DatabaseMetadataCache`, so this is an in-memory lookup, not a new SQL round trip.
- **`Rank` is a raw `int`** rather than an enum, for extensibility (future tiers without a breaking enum change). This is a minor discoverability trade-off, mitigated by documenting the two values in use (`0` = normal, `1` = heuristic/lower priority).

## Open Questions

- None — scope is limited to dedup + ranking for `Join`-kind items, per the agreed focus area.
