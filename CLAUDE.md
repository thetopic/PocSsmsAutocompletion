# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Git Workflow

**Never commit autonomously — always ask first.**

Before creating any git commit, ask the user for confirmation. Do not commit even if the user says "save the changes" or "it's done" — only commit when explicitly told to do so with a clear instruction like "commit" or "commit the changes".

## Project Overview

This is a **Visual Studio Extension (VSIX)** that adds SQL IntelliSense/autocomplete to **SQL Server Management Studio (SSMS) v22**. It's a proof-of-concept written in C# targeting .NET Framework 4.8.1.

## Build & Run

Open `SsmsAutocompletion.slnx` in Visual Studio 2022. Build with standard VS build commands (`Ctrl+Shift+B`).

**Debug launch**: Configured to start SSMS with an isolated experimental instance (`/rootsuffix Exp`). Post-build copies the VSIX to:
```
C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Extensions\AutoCompletion
```

There is no CLI build/test/lint toolchain — this project is developed entirely through Visual Studio.

## Architecture

The extension uses MEF (Managed Extensibility Framework) and the Visual Studio Editor SDK to hook into SSMS's SQL editor.

**Data flow when the user types:**
```
Keystroke → SqlCommandFilter.Exec()
  → HandleTypeChar / HandleSpaceCharacter / HandleDotCharacter
  → Task.Run(() => Trigger(...))
      → CompletionEngine.GetCompletions(snapshot, sql, caretPosition, connectionKey)
          → CompletionRequestBuilder.Build(...)      // builds CompletionRequest with all context flags
          → each ICompletionProvider.GetCompletions(request)   // providers filter on request flags
      → AutoCompletePopup.Show(items, wordSpan)      // shown on UI thread via Dispatcher.InvokeAsync
  → RETURN/TAB → AutoCompletePopup.Commit()          // inserts InsertText into buffer
```

**Key classes:**

| Class | File | Role |
|-------|------|------|
| `SsmsAutocompletionPackage` | `SsmsAutocompletionPackage.cs` | `AsyncPackage` MEF entry point |
| `VsTextViewCreationSqlListener` | `Editor/VsTextViewCreationSqlListener.cs` | Hooks view creation; wires up all providers, builds `SqlCommandFilter` |
| `SqlCommandFilter` | `Editor/SqlCommandFilter.cs` | `IOleCommandTarget`; intercepts keystrokes; decides when to trigger/dismiss popup; calls `RefreshConnectionKey()` |
| `AutoCompletePopup` | `UI/AutoCompletePopup.cs` | WPF popup; `Show`, `Dismiss`, `Filter`, `Commit`, `MoveUp/Down` |
| `CompletionEngine` | `Completion/CompletionEngine.cs` | Calls all providers, merges results |
| `CompletionRequestBuilder` | `Completion/CompletionRequestBuilder.cs` | Computes all `CompletionRequest` flags from snapshot + parse result |
| `SqlContextDetector` | `Parsing/SqlContextDetector.cs` | Implements `IContextDetector`; text-based and token-based helpers |
| `DatabaseMetadataCache` | `Cache/DatabaseMetadataCache.cs` | Singleton; 10-min TTL; thread-safe; wraps `IMetadataLoader` |

**Content type**: `"SQL"` — the extension only activates on SQL editor windows.

---

## CompletionRequest Flags

`CompletionRequestBuilder.Build()` computes these flags and passes them to every provider:

| Flag | Type | Detection method | Meaning |
|------|------|-----------------|---------|
| `IsDotContext` | bool | text-based (`GetQualifier`) | Cursor follows a `.` (schema.table or alias.column) |
| `Qualifier` | string? | text-based | The word before the dot when `IsDotContext` is true |
| `IsAfterFromKeyword` | bool | **text-based** (`GetWordBefore`) | Word before current token is `FROM` |
| `IsAfterExecKeyword` | bool | **text-based** (`GetWordBefore`) | Word before current token is `EXEC` or `EXECUTE` |
| `IsAfterJoinKeyword` | bool | token-based (`IsAfterKeyword`) | Word before current token is `JOIN` |
| `IsJoinOnContext` | bool | token-based (`IsAfterKeyword`) | Cursor is after `ON` keyword |
| `IsWhereContext` | bool | token-based (`IsInsideWhereClause`) | Cursor is inside a WHERE clause |
| `IsAfterTableInFromJoin` | bool | token-based (`DetectAliasContext`) | Cursor follows a table name (alias assignment context) |
| `TableNameBeforeCursor` | string? | token-based (`DetectAliasContext`) | The table name when `IsAfterTableInFromJoin` is true |
| `ConnectionKey` | `ConnectionKey` | from `SqlCommandFilter` | Server + database identity for cache lookup |
| `MetadataProvider` | `IMetadataProvider`? | from cache | SMO metadata provider (null if cache not warmed) |
| `ParseResult` | `ParseResult` | `SsmsSqlParser.Parse(sql)` | Full SSMS parse tree for the current SQL |
| `Snapshot` | `ITextSnapshot` | from `SqlCommandFilter` | Current editor text snapshot |

**Detection rule — text-based vs token-based:**
- Use **text-based** (`GetWordBefore` on `ITextSnapshot`) for keywords typed directly before the current word (FROM, EXEC, EXECUTE). More reliable in SSMS runtime.
- Use **token-based** (`TokenManager.FindToken` + `GetPreviousSignificantTokenIndex`) for structural context (ON, JOIN, WHERE, alias detection) where the keyword may not be immediately adjacent.

---

## Completion Providers

Registered in order in `VsTextViewCreationSqlListener.BuildProviders()`. Each returns `Array.Empty` when its guard conditions are not met.

| Provider | File | Activates when | Returns |
|----------|------|---------------|---------|
| `NativeCompletionProvider` | `Completion/` | always (delegates to SSMS native) | SSMS built-in items |
| `FkJoinTableCompletionProvider` | `Completion/` | `IsDotContext` + FK exists on qualifier | FK-related table suggestions |
| `InlineJoinCompletionProvider` | `Completion/` | `IsAfterFromKeyword`, alias context | JOIN … ON … snippet |
| `FkJoinCompletionProvider` | `Completion/` | `IsJoinOnContext` | FK column pairs for ON clause |
| `SimilarColumnJoinCompletionProvider` | `Completion/` | `IsJoinOnContext` | Same-name columns across joined tables |
| `AliasCompletionProvider` | `Completion/` | `IsAfterTableInFromJoin` | Suggested alias for the table just typed |
| `ColumnCompletionProvider` | `Completion/` | `IsDotContext` | Columns of the qualified table/view/alias |
| `CteCompletionProvider` | `Completion/` | `IsAfterFromKeyword` | CTE names defined in the current query |
| `CteColumnCompletionProvider` | `Completion/` | `IsDotContext` + qualifier is a CTE | CTE columns |
| `TableCompletionProvider` | `Completion/` | `IsAfterFromKeyword`, not dot | Tables and views (kind = Table or View) |
| `StoredProcedureCompletionProvider` | `Completion/` | `IsAfterExecKeyword`, not dot | Stored procedure names |
| `UserDefinedFunctionCompletionProvider` | `Completion/` | not dot, not exec | Scalar UDFs (non-FROM) or TVFs (FROM) |
| `FunctionCompletionProvider` | `Completion/` | not dot, not FROM, not exec | Built-in SQL functions |
| `KeywordCompletionProvider` | `Completion/` | not dot, not FROM, not exec | SQL keywords |

**Space-trigger words** (auto-open popup when user types a space after these): `FROM`, `ON`, `WHERE`, `AND`, `OR`, `JOIN`, `EXEC`, `EXECUTE` — see `SqlCommandFilter.IsContextTriggerWord()`.

**Auto-open after commit**: when a Table or View item is committed, the popup is immediately re-triggered (for alias or JOIN suggestions).

---

## Cache & Models

### IDatabaseMetadata (interface) / DatabaseMetadataCache (singleton)

```
Warm(key, serverConnection)              → background Task.Run → IMetadataLoader.Load()
GetMetadataProvider(key)                 → IMetadataProvider (SMO, for NativeCompletionProvider)
GetTables(key)                           → IReadOnlyList<TableInfo>  (tables + views)
GetColumns(key, schema, tableName)       → IReadOnlyList<ColumnInfo>
GetForeignKeys(key, schema, tableName)   → IReadOnlyList<ForeignKeyInfo>
GetProcedures(key)                       → IReadOnlyList<ProcedureInfo>
GetUserDefinedFunctions(key)             → IReadOnlyList<UserFunctionInfo>
Invalidate(key)                          → evicts cache entry
```

TTL: 10 minutes. All methods return `Array.Empty` on miss (never null).

### IMetadataLoader strategy

| Loader | Default? | Source |
|--------|----------|--------|
| `SystemCatalogMetadataLoader` | yes | `sys.tables`, `sys.views`, `sys.procedures`, `sys.objects` — fast SQL queries |
| `SmoMetadataLoader` | no | SMO object tree — complete but slower |

Switch via `DatabaseMetadataCache.Loader = new SmoMetadataLoader()`.

### Key models

```csharp
// TableInfo — represents a table OR a view
enum SqlObjectType { Table, View }
TableInfo { Schema, TableName, ObjectType }   // ToString() omits "dbo." prefix

// ProcedureInfo
ProcedureInfo { Schema, ProcedureName, IReadOnlyList<ParameterInfo> Parameters }

// UserFunctionInfo
enum UserFunctionType { Scalar, TableValued, InlineTableValued }
UserFunctionInfo { Schema, FunctionName, FunctionType, Parameters, IsTableValued /*bool*/ }

// ParameterInfo
ParameterInfo { Name, DataType, IsOutput, HasDefault }

// CompletionItem
enum CompletionItemKind { Keyword, Table, View, StoredProcedure, UserDefinedFunction,
                          Column, Join, Alias, Cte, Function }
CompletionItem { DisplayText, InsertText, Description, Kind }
```

`sys.objects` type codes used in SQL queries: `'U'`=table, `'V'`=view, `'FN'`=scalar fn, `'TF'`=table-valued fn, `'IF'`=inline TVF, `'P'`=stored procedure.

---

## Test Project

`SsmsAutocompletion.Tests/` uses **file linking**, not a project reference, because the main project is an old-style VSIX `.csproj` incompatible with SDK-style references.

**Consequence**: every new `.cs` file added to the main project must also be added to **both** `.csproj` files:
1. `SsmsAutocompletion.csproj` — `<Compile Include="Path\File.cs" />`
2. `SsmsAutocompletion.Tests/SsmsAutocompletion.Tests.csproj` — `<Compile Include="..\Path\File.cs" Link="_src\Path\File.cs" />`

**C# version**: main project uses C# 7.3 (no switch expressions, no null-coalescing assignment `??=`, no records). Test project uses C# 9.

---

## Development Rules

**Write tests first — always.**

Before implementing any new behaviour, write a failing test in `SsmsAutocompletion.Tests` that captures the expected result. Only then write the production code to make it pass. This applies to new features, bug fixes, and refactors alike.

**Always use the SSMS SQL parser — never regex, never hand-written extraction.**

The SSMS parser (`Microsoft.SqlServer.Management.SqlParser`) exposes two complementary APIs. Choose the right one for the task:

### 1. SqlCodeDom AST — for structural SQL analysis

`ParseResult.Script` returns a typed `SqlScript` AST from the `Microsoft.SqlServer.Management.SqlParser.SqlCodeDom` namespace. Use it whenever you need to extract structural information from a query: CTEs, SELECT lists, FROM clauses, etc.

```csharp
// Example: enumerate all CTEs in a query
foreach (SqlBatch batch in parseResult.Script.Batches)
    foreach (SqlStatement stmt in batch.Statements) {
        var withClause = (stmt as SqlSelectStatement)?.QueryWithClause;
        if (withClause == null) continue;
        foreach (SqlCommonTableExpression cte in withClause.CommonTableExpressions) {
            string name         = cte.Name.Value;
            var    explicitCols = cte.ColumnList;       // SqlIdentifierCollection (may be empty)
            var    queryExpr    = cte.QueryExpression;  // SqlQueryExpression (body)
        }
    }
```

Key types: `SqlScript`, `SqlBatch`, `SqlSelectStatement`, `SqlQueryWithClause`, `SqlCommonTableExpression`, `SqlQuerySpecification`, `SqlSelectClause`, `SqlSelectScalarExpression`, `SqlColumnRefExpression`, `SqlIdentifier`.

Do **not** write custom parsing loops, regex, or token-walking to extract information the AST already provides.

### 2. TokenManager — for cursor-position context detection

`ParseResult.Script.TokenManager` provides token-level access needed to answer "what is the user typing right now":

- `tokenManager.FindToken(line, column)` — find the token at the cursor position
- `tokenManager.GetPreviousSignificantTokenIndex(index)` — walk backwards through significant tokens
- `tokenManager.GetText(index)` — read token text

Use this for positional context (e.g. "is the cursor after a JOIN keyword?"), not for extracting query structure. The existing `IContextDetector.IsAfterKeyword()` is the reference implementation.

### Decision rule

| Need | Use |
|------|-----|
| Extract query structure (CTEs, columns, aliases…) | SqlCodeDom AST |
| Detect keyword immediately before cursor (FROM, EXEC…) | text-based `GetWordBefore` |
| Detect structural position (ON, JOIN, WHERE…) | TokenManager |
| Anything else | Check AST first |
