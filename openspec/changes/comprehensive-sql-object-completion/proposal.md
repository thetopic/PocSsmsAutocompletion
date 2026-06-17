hea	## Why

The extension already handles the most common completion contexts (FROM/table, dot/column, JOIN/ON, EXEC/procedure, UDF, keywords) but several core DML editing positions — INSERT column lists, UPDATE SET clauses, and unqualified column references in SELECT/WHERE — produce no custom suggestions, relying entirely on the SSMS native resolver which degrades or disappears when the metadata cache is cold.

## What Changes

- **INSERT column list completion**: when the cursor is inside `INSERT INTO table (`, suggest columns of the target table.
- **UPDATE SET column completion**: when the cursor is after `UPDATE table SET ` or after a comma in the SET list, suggest column names from the target table.
- **Unqualified column completion**: in SELECT and WHERE clauses, when the user types a bare word with no preceding dot, suggest columns from all tables referenced in the current query's FROM clause (drawn from the local metadata cache, not the DLL resolver).
- Add three new context flags to `CompletionRequest` — `IsInsertColumnList`, `IsUpdateSetClause`, `InsertUpdateTargetTable` — computed by `CompletionRequestBuilder` from the SQL AST.
- Add `IsInSelectList` and `IsInWhereClause` flags (boolean) for guarding the unqualified-column provider, reusing the existing `IsWhereContext` flag for WHERE and adding a new `IsInSelectList` for SELECT position.

**Strategy rule** (applies to this change and the whole extension):
> Use `Resolver.FindCompletions()` (SSMS DLL) as the primary completions path. Add a custom provider only for object types or editing positions the DLL does not cover — notably: FK join snippets, alias suggestion, CTE extraction, parameter hints, and now INSERT/UPDATE column lists and unqualified column access.

## Capabilities

### New Capabilities

- `insert-column-completion`: Suggests column names inside an INSERT INTO column list; excludes columns already listed; ordered by ordinal position.
- `update-set-completion`: Suggests `columnName = ` items after UPDATE ... SET and between commas in the SET clause; excludes columns already set in the same statement.
- `unqualified-column-completion`: Suggests columns from all FROM-clause tables when typing in SELECT or WHERE without a table qualifier (no dot).

### Modified Capabilities

(none — existing provider behaviours are unchanged)

## Impact

- New files: `Completion/InsertColumnCompletionProvider.cs`, `Completion/UpdateSetCompletionProvider.cs`, `Completion/UnqualifiedColumnCompletionProvider.cs`, plus corresponding test files.
- Modified files: `Completion/CompletionRequest.cs` (new flags), `Completion/CompletionRequestBuilder.cs` (populate new flags via AST), `Parsing/SqlContextDetector.cs` (helpers for INSERT/UPDATE target table detection), `Editor/VsTextViewCreationSqlListener.cs` (register three new providers), both `.csproj` files.
- Depends on: `IDatabaseMetadata.GetColumns()` (already implemented), `AliasExtractor` (already available), SSMS parser AST (`SqlInsertStatement`, `SqlUpdateStatement`, `SqlSelectStatement`) for structural detection.
- No changes to the existing provider chain order except inserting new providers before `KeywordCompletionProvider`.
