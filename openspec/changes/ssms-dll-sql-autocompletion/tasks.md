## 1. Cache — Add `GetSchemas` support

- [x] 1.1 Add `IReadOnlyList<string> GetSchemas(ConnectionKey key)` to `Cache/IDatabaseMetadata.cs`
- [x] 1.2 Add schema loading SQL (`SELECT name FROM sys.schemas WHERE schema_id < 16384`) to `Cache/SystemCatalogMetadataLoader.cs`, wrapped in try/catch returning `Array.Empty` on failure
- [x] 1.3 Wire `GetSchemas` into `Cache/DatabaseMetadataCache.cs` (store and return from the existing TTL cache entry)
- [x] 1.4 Add both `.csproj` entries for any new files (main project + test project)

## 2. Context Detection — `IsInsideProcedureCall` flag

- [x] 2.1 Write failing tests in `SsmsAutocompletion.Tests/SqlContextDetectorTests.cs` for `GetProcedureCallContext()` (flag true/false, procedure name extraction, already-provided parameters set)
- [x] 2.2 Implement `SqlContextDetector.GetProcedureCallContext()` — text-based backward scan that returns `(bool isInside, string? procedureName, IReadOnlyList<string> alreadyProvided)`
- [x] 2.3 Add `bool IsInsideProcedureCall` and `string? ProcedureNameBeforeCursor` and `IReadOnlyList<string> AlreadyProvidedParameters` to `Completion/CompletionRequest.cs`
- [x] 2.4 Populate the new flags in `Completion/CompletionRequestBuilder.cs` by calling `GetProcedureCallContext()`
- [x] 2.5 Write failing tests for `CompletionRequestBuilder` covering the new flags (inside call, before procedure name, outside EXEC)

## 3. Stored Procedure Parameter Completion Provider

- [x] 3.1 Write failing tests in `SsmsAutocompletion.Tests/StoredProcedureParameterCompletionProviderTests.cs` covering: shows all params on empty call, excludes already-provided params, empty on cache miss, empty outside EXEC
- [x] 3.2 Create `Completion/StoredProcedureParameterCompletionProvider.cs` — guard on `IsInsideProcedureCall`, look up `ProcedureNameBeforeCursor` in cache, subtract `AlreadyProvidedParameters`, return `@paramName =` items
- [x] 3.3 Add `CompletionItemKind.Parameter` to `Models/CompletionItem.cs` (or reuse an appropriate existing kind)
- [x] 3.4 Register `StoredProcedureParameterCompletionProvider` in `Editor/VsTextViewCreationSqlListener.cs` (after `StoredProcedureCompletionProvider`, before `KeywordCompletionProvider`)
- [x] 3.5 Add entries for the new provider file to both `.csproj` files

## 4. Schema Name Completion Provider

- [x] 4.1 Write failing tests in `SsmsAutocompletion.Tests/SchemaCompletionProviderTests.cs` covering: unrecognised qualifier triggers schema list, known table qualifier returns empty, empty on cache miss
- [x] 4.2 Create `Completion/SchemaCompletionProvider.cs` — guard on `IsDotContext` + qualifier not matched by tables/views/CTEs/aliases; call `GetSchemas(key)` and filter by prefix match
- [x] 4.3 Register `SchemaCompletionProvider` in `Editor/VsTextViewCreationSqlListener.cs` (after `FkJoinTableCompletionProvider`, before `ColumnCompletionProvider`)
- [x] 4.4 Add entries for the new provider file to both `.csproj` files

## 5. Integration Smoke Test

- [ ] 5.1 Build and deploy to the experimental SSMS instance (`Ctrl+Shift+B` then launch via `/rootsuffix Exp`)
- [ ] 5.2 Verify `EXEC dbo.SomeProc ` opens popup with `@paramName =` items
- [ ] 5.3 Verify typing `hr.` (where `hr` is a valid schema) shows tables under that schema (existing behaviour preserved)
- [ ] 5.4 Verify that unrelated completion paths (FROM + table, dot + column) are unaffected
