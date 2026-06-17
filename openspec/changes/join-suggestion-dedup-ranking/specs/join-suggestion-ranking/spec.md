## ADDED Requirements

### Requirement: Canonical deduplication of join-condition suggestions

The system SHALL deduplicate `CompletionItemKind.Join` completion items using a canonical form of their `DisplayText` — operands within each `alias.Column = alias.Column` clause sorted, and `AND`-joined clauses sorted — in addition to exact-text matching used for other completion kinds.

#### Scenario: Operand-swapped duplicate is collapsed

- **WHEN** one provider proposes `o.CustomerID = c.CustomerID` and another proposes `c.CustomerID = o.CustomerID` for the same join context
- **THEN** only one of the two items is shown to the user

#### Scenario: Reordered AND clauses are collapsed

- **WHEN** one provider proposes `a.Col1 = b.Col1 AND a.Col2 = b.Col2` and another proposes `b.Col2 = a.Col2 AND b.Col1 = a.Col1`
- **THEN** only one of the two items is shown to the user

#### Scenario: Genuinely different conditions are kept

- **WHEN** two `Join` items have conditions that do not canonicalize to the same key (different columns or different tables)
- **THEN** both items are shown

#### Scenario: Non-join items are unaffected

- **WHEN** two non-`Join` items (e.g. `Table` or `Column` kind) have different `DisplayText`
- **THEN** both are kept, using the existing exact-`DisplayText` deduplication (no canonicalization applied)

#### Scenario: Unrecognized join-condition shape falls back to exact-text dedup

- **WHEN** a `Join` item's `DisplayText` does not match the `"alias.Column = alias.Column[ AND ...]"` shape
- **THEN** that item is deduplicated by its raw `DisplayText` and no error occurs

### Requirement: Rank-based ordering of completion items

The system SHALL support an `int Rank` property on `CompletionItem` (default `0`) and, after deduplication, SHALL apply a stable sort by `Rank` so that items with a higher `Rank` value are moved after items with a lower `Rank` value, without changing the relative order of items that share the same `Rank`.

#### Scenario: Higher-rank item moves after lower-rank items

- **WHEN** a provider that runs early returns an item with `Rank = 1`, and later providers return items with `Rank = 0`
- **THEN** the `Rank = 1` item appears after all `Rank = 0` items in the final list

#### Scenario: Stability within the same rank

- **WHEN** multiple items share the same `Rank` value
- **THEN** their relative order from `CollectAllItems`/`Deduplicate` is preserved
