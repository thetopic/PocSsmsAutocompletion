## Why

`FkJoinCompletionProvider` and `SimilarColumnJoinCompletionProvider` both fire when `IsJoinOnContext` or `IsWhereContext` is true, and can independently propose ON-condition items for the same pair of tables. `CompletionEngine.Deduplicate` only removes items with an identical `DisplayText`, so:

- A condition proposed with swapped operands (`b.CustomerID = a.CustomerID` vs `a.CustomerID = b.CustomerID`) shows up as two separate items in the popup.
- `SimilarColumnJoinCompletionProvider` re-proposes single-column conditions for column pairs that are already covered (often as part of a composite key) by an FK relation that `FkJoinCompletionProvider` already suggested, adding near-duplicate noise.
- The relative order of FK-derived suggestions (schema-verified, reliable) versus name-similarity suggestions (heuristic) depends entirely on provider registration order in `BuildProviders()`, with no explicit signal that FK-based conditions should rank above heuristic ones.

## What Changes

- `CompletionEngine`: for `CompletionItemKind.Join` items, compute a canonical comparison key (operands of each `AND`-joined `alias.Column = alias.Column` clause sorted, clauses sorted) and use it — in addition to `DisplayText` — for deduplication, so operand-order variants of the same condition collapse into a single item.
- `SimilarColumnJoinCompletionProvider`: before proposing `aliasA.colA = aliasB.colB`, compute the set of column pairs already covered by FK relations between `tableA` and `tableB` (same `GetForeignKeys` lookups `FkJoinCompletionProvider` already performs) and skip any column pair that is part of one of those FK relations.
- `CompletionItem`: add an `int Rank` property (default `0`, optional constructor parameter so existing call sites are unaffected). `SimilarColumnJoinCompletionProvider` sets `Rank = 1` on the items it still proposes after the exclusion above.
- `CompletionEngine.GetCompletions`: after deduplication, perform a stable sort by `Rank` so rank-0 items (everything else, including FK-based join suggestions) keep their existing relative order, and rank-1 items (remaining similarity-based suggestions) move after them.

## Capabilities

### New Capabilities

- `join-suggestion-ranking`: defines the dedup/ranking contract applied by `CompletionEngine` to `CompletionItemKind.Join` completion items — canonical-form dedup across providers, and rank-based ordering that places name-similarity suggestions after FK-based and other suggestions.

### Modified Capabilities

- `similar-column-join-completion`: no longer proposes column pairs that are already covered by an FK relation between the two tables, and its remaining items are ranked after other completions.

## Impact

- Modified files: `Completion/CompletionEngine.cs` (canonical dedup key, stable rank sort), `Completion/SimilarColumnJoinCompletionProvider.cs` (FK-pair exclusion, set `Rank = 1`), `Models/CompletionItem.cs` (new `Rank` property).
- New test files: `SsmsAutocompletion.Tests/CompletionEngineTests.cs`, `SsmsAutocompletion.Tests/SimilarColumnJoinCompletionProviderTests.cs`, plus both `.csproj` entries.
- No new `CompletionRequest` flags, no provider registration changes, no breaking changes — `Rank` defaults to `0` so all unmodified providers are unaffected.
