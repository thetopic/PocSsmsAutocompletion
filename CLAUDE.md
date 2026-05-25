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
  → triggers SqlCompletionSource.AugmentCompletionSession()
  → returns keyword list
  → RETURN/TAB commits the selected suggestion
```

**Key classes:**

- `SsmsAutocompletionPackage` — `AsyncPackage` entry point; auto-loads in both solution and no-solution contexts.
- `SqlCommandFilter` (`IOleCommandTarget`) — intercepts keystrokes; triggers autocomplete on letter/digit/dot, commits on RETURN/TAB, dismisses on other keys.
- `SqlCompletionSource` / `SqlCompletionSourceProvider` — MEF-exported `ICompletionSource`; currently returns a hard-coded list (SELECT, FROM, WHERE).
- `VsTextViewCreationSqlListener` / `SqlCompletionController` — hooks text view creation to attach the command filter.
- `Test` class — stub helpers for SMO-based database metadata (uses reflection to pull connection info from SSMS UI); partially implemented.

**Content type**: `"SQL"` — the extension only activates on SQL editor windows.

**Current limitations**: Suggestions are hard-coded keywords; the `Test` class contains the scaffolding for real database-driven completions (tables, columns) but it is not yet wired up.

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
            string name          = cte.Name.Value;               // CTE name
            var    explicitCols  = cte.ColumnList;               // SqlIdentifierCollection (may be empty)
            var    queryExpr     = cte.QueryExpression;          // SqlQueryExpression (body)
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
| Detect what the user is typing at the cursor | TokenManager |
| Anything else | Neither — check the AST first |
