## Context

The extension has two tiers of completion:
1. **Native**: delegates to `Resolver.FindCompletions()` from `Microsoft.SqlServer.Management.SqlParser.Intellisense` — covers general identifiers.
2. **Custom**: provider chain in `CompletionEngine` — covers JOIN suggestions, CTEs, aliases, keywords, stored procedures, UDFs, etc.

The custom chain is driven by flags on `CompletionRequest` computed by `CompletionRequestBuilder`. Adding new completion scenarios always involves: (a) a new context flag, (b) a new provider, and (c) registration in `VsTextViewCreationSqlListener.BuildProviders()`.

Two gaps are being closed: **parameter name completion inside EXEC calls** and **schema prefix completion before a dot**.

## Goals / Non-Goals

**Goals:**
- Suggest `@paramName =` items in the correct positional order when the cursor is inside an EXEC call.
- Suppress already-provided parameters from the suggestion list.
- Suggest schema names (e.g., `dbo`, `hr`) when the user types a bare word before a `.` and no table/alias matches.
- Load schemas on demand from `sys.schemas` and cache them under the existing `DatabaseMetadataCache` TTL.

**Non-Goals:**
- Parameter value completion (no inference of allowed values for a given parameter type).
- Overloaded procedure resolution.
- Schema-qualified column completion (already handled by `ColumnCompletionProvider` via `IsDotContext`).

## Decisions

### D1 — Context detection: text-based `GetProcedureName()` on `ITextSnapshot`

**Decision**: Detect the inside-EXEC context with a text-based backward scan, identical in style to the existing `GetWordBefore()` used for `IsAfterExecKeyword`.

**Rationale**: Token-based detection proved less reliable in SSMS's live editor (same reason `IsAfterExecKeyword` was switched to text-based in commit ca5cab1). The scan walks backwards from the cursor: skip whitespace, collect parameter tokens already typed, then find the EXEC keyword and procedure name between it and the cursor.

**Alternative considered**: Use `TokenManager.FindToken` to walk backwards to the EXEC token. Rejected because SSMS's tokenizer re-parses incrementally and can produce stale token positions mid-typing.

### D2 — Already-provided parameter exclusion: set subtraction at build time

**Decision**: `GetProcedureName()` (or a companion helper) also collects the set of `@param` names already present in the call. `StoredProcedureParameterCompletionProvider` subtracts this set before returning items.

**Rationale**: Prevents duplicating already-used named parameters, which is a SQL error. Simple string-set comparison is sufficient since parameter names are case-insensitive identifiers.

### D3 — Schema list: new `GetSchemas(key)` on `IDatabaseMetadata`

**Decision**: Add `IReadOnlyList<string> GetSchemas(ConnectionKey key)` to the cache interface, backed by `SELECT name FROM sys.schemas WHERE schema_id < 16384 ORDER BY name` in `SystemCatalogMetadataLoader`.

**Rationale**: Schemas are rarely more than a dozen rows and don't change during a session. Adding them to the existing 10-minute TTL cache means zero extra round-trips after the first warm.

**Alternative considered**: Return schemas inline from `GetTables()` by inspecting distinct Schema values. Rejected because that only returns schemas that have at least one table, missing empty schemas the user might be targeting.

### D4 — Schema completion guard: only when qualifier is unresolved

**Decision**: `SchemaCompletionProvider` activates only when `IsDotContext` is true and the `Qualifier` does not match any known table, view, CTE name, or alias. It returns early (empty) otherwise, avoiding interference with `ColumnCompletionProvider` and `FkJoinTableCompletionProvider`.

**Rationale**: Schema completion should be a fallback, not a competitor. The guard preserves the existing provider priority order.

### D5 — `IsInsideProcedureCall` flag: new `CompletionRequest` field

**Decision**: Add `bool IsInsideProcedureCall` and `string? ProcedureNameBeforeCursor` to `CompletionRequest`. `CompletionRequestBuilder` computes them via a new `SqlContextDetector.GetProcedureCallContext()` method.

**Rationale**: Follows the established pattern (e.g., `IsAfterExecKeyword` / `IsAfterTableInFromJoin`). Keeps providers thin — they guard on a flag, they don't do their own scanning.

## Risks / Trade-offs

- **Text scan false positives** → If the user writes `/* EXEC */ EXEC realProc @a = 1,` the backward scan may misread the comment. Mitigation: the scan stops at the first unbalanced `)` or `;` going backward, limiting scope to the current statement.
- **`sys.schemas` permission** → On locked-down SQL Server instances the user account may lack `VIEW DEFINITION`. Mitigation: wrap the query in a try/catch inside `SystemCatalogMetadataLoader`; return `Array.Empty` on failure (same pattern as existing loaders).
- **Provider ordering** → `StoredProcedureParameterCompletionProvider` must be registered *before* `KeywordCompletionProvider` (so `@`-prefixed items appear at the top), but *after* `StoredProcedureCompletionProvider` (to avoid activating when not inside a call). Registration order in `BuildProviders()` enforces this.

## Open Questions

- Should `StoredProcedureParameterCompletionProvider` also trigger for `sp_executesql` and other system procedures? (Current plan: yes, if cached — but system procedures may not be in `sys.procedures`; may require a `GetSystemProcedures` extension.)
- Should schemas be shown with a trailing `.` already appended in `InsertText`? (Leaning yes, matching the pattern used for table names.)
