## ADDED Requirements

### Requirement: Suggest named parameters inside EXEC calls
The system SHALL suggest named parameter items (`@paramName =`) when the cursor is positioned inside an EXEC/EXECUTE call and the invoked procedure is found in the metadata cache.

#### Scenario: First parameter inside a bare EXEC call
- **WHEN** the user has typed `EXEC dbo.MyProc ` (cursor after the space, no parameters yet)
- **THEN** the popup shows one item per parameter of `dbo.MyProc`, each displayed as `@paramName` with insert text `@paramName = `

#### Scenario: Additional parameter after a comma
- **WHEN** the user has typed `EXEC dbo.MyProc @p1 = 1, ` (cursor after the comma and space)
- **THEN** the popup shows items for all parameters of `dbo.MyProc` except `@p1`, preserving positional order

#### Scenario: Already-provided parameters are excluded
- **WHEN** a named parameter `@id` has already been typed in the call
- **THEN** `@id` does NOT appear in the suggestion list

#### Scenario: Procedure not in cache
- **WHEN** the metadata cache has not been warmed or the procedure name is not found
- **THEN** the provider returns no items (popup is not shown for this provider)

#### Scenario: Cursor outside any EXEC call
- **WHEN** the cursor is on a plain SELECT statement with no enclosing EXEC
- **THEN** the provider returns no items

### Requirement: Context flag `IsInsideProcedureCall` exposed on CompletionRequest
The system SHALL compute `IsInsideProcedureCall` (bool) and `ProcedureNameBeforeCursor` (string?) in `CompletionRequestBuilder` using a text-based backward scan from the cursor.

#### Scenario: Flag is true inside EXEC call
- **WHEN** the SQL text at the cursor is `EXEC dbo.MyProc @p1 = 1, |` (| = cursor)
- **THEN** `IsInsideProcedureCall` is `true` and `ProcedureNameBeforeCursor` is `"dbo.MyProc"`

#### Scenario: Flag is false after EXEC keyword but before procedure name
- **WHEN** the SQL text at the cursor is `EXEC |` (cursor immediately after EXEC)
- **THEN** `IsInsideProcedureCall` is `false` (the procedure name has not been typed yet)

#### Scenario: Flag is false outside any EXEC statement
- **WHEN** the SQL text at the cursor is `SELECT | FROM dbo.T`
- **THEN** `IsInsideProcedureCall` is `false` and `ProcedureNameBeforeCursor` is `null`
