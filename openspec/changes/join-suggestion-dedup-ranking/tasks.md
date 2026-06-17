## 1. `CompletionItem` — add `Rank`

- [x] 1.1 Add `public int Rank { get; }` to `Models/CompletionItem.cs`, as an optional constructor parameter defaulting to `0`

## 2. `CompletionEngine` — canonical dedup for `Join` items

- [x] 2.1 Write failing tests in new `SsmsAutocompletion.Tests/CompletionEngineTests.cs`:
  - two `Join` items whose `DisplayText` differ only by operand order (`a.X = b.X` vs `b.X = a.X`) dedupe to one, keeping the first-seen item
  - two `Join` items with genuinely different conditions are both kept
  - a multi-clause AND condition dedupes against the same condition with clauses reordered
  - non-`Join` items continue to dedupe by exact `DisplayText` only (no canonicalization)
  - a `Join` item whose `DisplayText` doesn't match the `"X = Y[ AND ...]"` shape falls back to raw-text dedup without throwing
- [x] 2.2 Implement a private static canonical-key helper in `CompletionEngine`: split on `" AND "`, split each clause on `" = "`, sort the two operands, sort the clause list, rejoin — with raw-`DisplayText` fallback on shape mismatch
- [x] 2.3 Update `Deduplicate` to use the canonical key for `CompletionItemKind.Join` items and `DisplayText` for all other kinds

## 3. `CompletionEngine` — rank-based stable ordering

- [x] 3.1 Write failing tests in `SsmsAutocompletion.Tests/CompletionEngineTests.cs`:
  - a `Rank = 1` item is moved after all `Rank = 0` items even if a provider returned it first
  - relative order among `Rank = 0` items is unchanged (stability)
  - relative order among multiple `Rank = 1` items is unchanged (stability)
- [x] 3.2 In `GetCompletions`, after `Deduplicate` and before `FilterByPrefix`, apply a stable sort by `Rank`

## 4. `SimilarColumnJoinCompletionProvider` — exclude FK-covered pairs, set `Rank = 1`

- [x] 4.1 Write failing tests in new `SsmsAutocompletion.Tests/SimilarColumnJoinCompletionProviderTests.cs`:
  - a column pair that is part of an FK relation between the two tables (tableA owns the FK) is excluded
  - a column pair that is part of an FK relation in the reverse direction (tableB owns the FK) is excluded
  - a column pair covered by one clause of a composite FK is excluded
  - a similarly-named column pair with NO FK relation between the tables is still suggested, with `Rank == 1`
  - existing behavior (suggestions when `aliasMap.Count < 2` returns empty, `IsDotContext` returns empty) is preserved
- [x] 4.2 Implement an FK-pair-set builder mirroring `FkJoinCompletionProvider.AddFkConditionsForPair` (both directions), returning the set of `(columnNameA, columnNameB)` pairs already covered by FK relations between `tableA` and `tableB`
- [x] 4.3 In `AddConditionsForPair`, skip `(columnA.ColumnName, columnB.ColumnName)` pairs present in that set (either order)
- [x] 4.4 Set `Rank: 1` on the `CompletionItem`s this provider still returns

## 5. Project files

- [x] 5.1 Add `CompletionEngineTests.cs` and `SimilarColumnJoinCompletionProviderTests.cs` to `SsmsAutocompletion.Tests/SsmsAutocompletion.Tests.csproj`

## 6. Integration smoke test

- [ ] 6.1 Build and deploy to the experimental SSMS instance (`Ctrl+Shift+B`, launch via `/rootsuffix Exp`)
- [ ] 6.2 With `Orders o JOIN Customers c ON `, verify only one `o.CustomerID = c.CustomerID`-style item appears (no reversed-operand duplicate)
- [ ] 6.3 With a composite FK between two joined tables, verify no extra single-column "Colonnes similaires" duplicates appear for the FK columns
- [ ] 6.4 Verify remaining name-similarity-only suggestions still appear, after FK-based and other suggestions in the popup
