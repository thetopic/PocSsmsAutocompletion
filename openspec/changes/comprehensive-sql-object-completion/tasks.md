## 1. CompletionRequest — New Context Flags

- [x] 1.1 Add `bool IsInsertColumnList`, `bool IsUpdateSetClause`, `string? InsertUpdateTargetTable`, and `bool IsInSelectList` to `Completion/CompletionRequest.cs`
- [x] 1.2 Write failing tests in `SsmsAutocompletion.Tests/CompletionRequestBuilderTests.cs` for each new flag: INSERT column list true/false, UPDATE SET true/false, INSERT target table extraction, SELECT list flag true/false

## 2. Context Detection — INSERT and UPDATE via AST

- [x] 2.1 Write failing tests in `SsmsAutocompletion.Tests/SqlContextDetectorTests.cs` for `DetectInsertContext()`: cursor inside column list → true + table name; cursor in VALUES → false
- [x] 2.2 Implement `SqlContextDetector.DetectInsertContext(ParseResult, line, column)` — walk token stream, check if cursor falls within the column-list token range, return `(bool, string? targetTable)`
- [x] 2.3 Write failing tests for `DetectUpdateSetContext()`: cursor in SET → true + table name; cursor in WHERE → false; aliased target resolved via alias map
- [x] 2.4 Implement `SqlContextDetector.DetectUpdateSetContext(ParseResult, line, column)` — walk token stream, check if cursor is between SET keyword and WHERE/end, return `(bool, string? targetTable)`
- [x] 2.5 Populate `IsInsertColumnList`, `IsUpdateSetClause`, `InsertUpdateTargetTable` in `Completion/CompletionRequestBuilder.cs`

## 3. Context Detection — SELECT List Flag

- [x] 3.1 Write failing tests for `IsInSelectList`: nearest keyword is SELECT → true; nearest is FROM/WHERE/SET → false; nested subquery SELECT → true
- [x] 3.2 Implement text-based `GetNearestClauseKeyword(ITextSnapshot, caretPosition)` in `Parsing/SqlContextDetector.cs` — backward scan returning the first clause-level keyword found
- [x] 3.3 Populate `IsInSelectList` in `CompletionRequestBuilder` using the new helper

## 4. INSERT Column Completion Provider

- [x] 4.1 Write failing tests in `SsmsAutocompletion.Tests/InsertColumnCompletionProviderTests.cs`: all columns shown on empty list; already-listed columns excluded; cache miss returns empty; non-INSERT context returns empty
- [x] 4.2 Create `Completion/InsertColumnCompletionProvider.cs` — guard on `IsInsertColumnList`; parse already-listed columns from SQL text (split on commas inside the column list); call `GetColumns(key, schema, table)`; subtract already-listed; return `CompletionItem` list with `Kind = Column`
- [x] 4.3 Register `InsertColumnCompletionProvider` in `Editor/VsTextViewCreationSqlListener.cs` before `KeywordCompletionProvider`
- [x] 4.4 Add both `.csproj` entries for the new provider file (main project + test project)

## 5. UPDATE SET Completion Provider

- [x] 5.1 Write failing tests in `SsmsAutocompletion.Tests/UpdateSetCompletionProviderTests.cs`: columns shown after SET; already-assigned columns excluded; aliased target resolved; cache miss returns empty; non-UPDATE context returns empty
- [x] 5.2 Create `Completion/UpdateSetCompletionProvider.cs` — guard on `IsUpdateSetClause`; collect already-assigned columns by scanning SET clause text up to cursor; resolve alias via `AliasExtractor` if needed; call `GetColumns`; subtract already-assigned; return items with `InsertText = "columnName = "`
- [x] 5.3 Register `UpdateSetCompletionProvider` in `Editor/VsTextViewCreationSqlListener.cs` before `KeywordCompletionProvider`
- [x] 5.4 Add both `.csproj` entries for the new provider file

## 6. Unqualified Column Completion Provider

- [x] 6.1 Write failing tests in `SsmsAutocompletion.Tests/UnqualifiedColumnCompletionProviderTests.cs`: returns columns when `IsInSelectList` and `!IsDotContext`; returns columns when `IsWhereContext` and `!IsDotContext`; returns empty when `IsDotContext`; merges columns from multiple FROM tables; cache miss for a table contributes nothing (other tables still returned)
- [x] 6.2 Create `Completion/UnqualifiedColumnCompletionProvider.cs` — guard on `!IsDotContext && (IsInSelectList || IsWhereContext)`; collect all aliases from `AliasExtractor`; for each alias resolve to table and call `GetColumns`; merge and return `CompletionItem` list with `Kind = Column`
- [x] 6.3 Register `UnqualifiedColumnCompletionProvider` in `Editor/VsTextViewCreationSqlListener.cs` after `ColumnCompletionProvider`, before `TableCompletionProvider`
- [x] 6.4 Add both `.csproj` entries for the new provider file

## 7. Integration Smoke Test

- [ ] 7.1 Build and deploy to the experimental SSMS instance (`Ctrl+Shift+B` then launch via `/rootsuffix Exp`)
- [ ] 7.2 Verify `INSERT INTO dbo.SomeTable (` opens popup with column names
- [ ] 7.3 Verify `UPDATE dbo.SomeTable SET ` opens popup with `col = ` items
- [ ] 7.4 Verify `SELECT Fir` from a table with `FirstName` shows `FirstName` in the popup (no dot typed)
- [ ] 7.5 Verify all existing completion paths (FROM + table, dot + column, EXEC + proc, JOIN) are unaffected
