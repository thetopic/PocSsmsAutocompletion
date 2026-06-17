## Why

The extension already uses `Resolver.FindCompletions()` from the SSMS SqlParser DLL for native completions, but leaves a large portion of the DLL's intellisense surface untapped. Specifically, stored procedure parameter completion and sub-query-level context detection are absent, making the editing experience drop off sharply once the user types past a procedure name or enters a nested SELECT.

## What Changes

- Add a `StoredProcedureParameterCompletionProvider` that suggests named parameters (`@paramName =`) after `EXEC proc_name` using the cached `ProcedureInfo.Parameters` already loaded by `SystemCatalogMetadataLoader`.
- Add a `SchemaCompletionProvider` that suggests schema names (e.g. `dbo.`, `hr.`) when the user types a bare identifier before a dot and no qualifier is recognised.
- Extend `CompletionRequestBuilder` to detect the **inside-procedure-call context** (cursor is between the procedure name and the closing `;` / next statement) and expose it as a new `IsInsideProcedureCall` / `ProcedureNameBeforeCursor` flag pair.
- Extend `SqlContextDetector` with a text-based `GetProcedureName()` helper that scans backwards from the cursor to the nearest `EXEC`/`EXECUTE` token, returning the procedure name.
- Extend `SystemCatalogMetadataLoader` (and cache interface) with a `GetSchemas(key)` method backed by `sys.schemas`.

## Capabilities

### New Capabilities

- `stored-procedure-parameter-completion`: Suggests `@paramName =` items inside an EXEC call, ordered by parameter position; already-provided parameters are excluded.
- `schema-name-completion`: Suggests schema names when typing the prefix before a `.` and no existing qualifier matches a table/alias.

### Modified Capabilities

- `stored-procedure-completion`: Existing EXEC-triggered completion; no requirement change, only used as reference context for the new parameter provider.

## Impact

- New files: `Completion/StoredProcedureParameterCompletionProvider.cs`, `Completion/SchemaCompletionProvider.cs`, `Parsing/IProcedureCallDetector.cs` (interface), plus test files for each.
- Modified files: `Completion/CompletionRequestBuilder.cs` (new flags), `Parsing/SqlContextDetector.cs` (new helper), `Cache/IDatabaseMetadata.cs` + `Cache/DatabaseMetadataCache.cs` + `Cache/SystemCatalogMetadataLoader.cs` (new `GetSchemas`), `Editor/VsTextViewCreationSqlListener.cs` (register new providers), both `.csproj` files.
- No breaking changes. Existing providers are unchanged in behaviour.
- Requires SSMS 22 runtime DLLs: `Microsoft.SqlServer.Management.SqlParser.dll` (already referenced).
