## ADDED Requirements

### Requirement: Suggest column names after UPDATE ... SET
The system SHALL suggest column names of the target table when the cursor is after the SET keyword or between comma-separated assignments in an UPDATE statement.

#### Scenario: First assignment after SET
- **WHEN** the user has typed `UPDATE dbo.Orders SET ` (cursor after the space)
- **THEN** the popup shows all columns of `dbo.Orders`, each with insert text `columnName = `

#### Scenario: Additional assignment after a comma
- **WHEN** the user has typed `UPDATE dbo.Orders SET Status = 'A', ` (cursor after the comma)
- **THEN** all columns of `dbo.Orders` except `Status` are shown

#### Scenario: Already-assigned columns are excluded
- **WHEN** `CustomerId` and `Status` have already been assigned in the SET clause before the cursor
- **THEN** neither `CustomerId` nor `Status` appears in the suggestion list

#### Scenario: Aliased UPDATE target is resolved correctly
- **WHEN** the SQL is `UPDATE o SET o.Status` and `o` is an alias for `dbo.Orders` in the FROM clause
- **THEN** the provider resolves `o` to `dbo.Orders` via `AliasExtractor` and suggests its columns

#### Scenario: Target table not in cache returns no suggestions
- **WHEN** the metadata cache has not been warmed or the table is unknown
- **THEN** the provider returns no items

#### Scenario: Cursor outside UPDATE SET clause returns no suggestions
- **WHEN** the cursor is on a SELECT or INSERT statement
- **THEN** the provider returns no items

### Requirement: `IsUpdateSetClause` flag on CompletionRequest
The system SHALL compute `IsUpdateSetClause` (bool) and `InsertUpdateTargetTable` (string?) in `CompletionRequestBuilder` using the SQL AST (`SqlUpdateStatement`).

#### Scenario: Flag is true inside the SET clause
- **WHEN** the SQL is `UPDATE dbo.T SET |` (| = cursor)
- **THEN** `IsUpdateSetClause` is `true` and `InsertUpdateTargetTable` is `"dbo.T"`

#### Scenario: Flag is false in the WHERE clause of the same UPDATE
- **WHEN** the SQL is `UPDATE dbo.T SET col = 1 WHERE |`
- **THEN** `IsUpdateSetClause` is `false`
