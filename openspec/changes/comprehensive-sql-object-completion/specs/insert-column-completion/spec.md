## ADDED Requirements

### Requirement: Suggest columns inside INSERT INTO column list
The system SHALL suggest column names of the target table when the cursor is inside the explicit column list of an INSERT INTO statement.

#### Scenario: Empty column list triggers all column suggestions
- **WHEN** the user has typed `INSERT INTO dbo.Customers (` and the cursor is after the `(`
- **THEN** the popup shows all columns of `dbo.Customers` ordered by ordinal position

#### Scenario: Partial column name filters the suggestion list
- **WHEN** the user types `INSERT INTO dbo.Orders (Cust` inside the column list
- **THEN** only columns whose names start with `Cust` are shown

#### Scenario: Already-listed columns are excluded
- **WHEN** the user has typed `INSERT INTO dbo.Orders (CustomerID, ` and the cursor is after the comma
- **THEN** `CustomerID` does NOT appear in the suggestion list

#### Scenario: Target table not in cache returns no suggestions
- **WHEN** the metadata cache is cold or the target table is not found
- **THEN** the provider returns no items (popup is not shown for this provider)

#### Scenario: Cursor outside INSERT column list returns no suggestions
- **WHEN** the cursor is on a SELECT statement or in the VALUES clause of the same INSERT
- **THEN** the provider returns no items

### Requirement: `IsInsertColumnList` and `InsertUpdateTargetTable` flags on CompletionRequest
The system SHALL compute `IsInsertColumnList` (bool) and `InsertUpdateTargetTable` (string?) in `CompletionRequestBuilder` using the SQL AST (`SqlInsertStatement`).

#### Scenario: Flag is true when cursor is between column-list parentheses
- **WHEN** the SQL is `INSERT INTO dbo.T (|)` (| = cursor position)
- **THEN** `IsInsertColumnList` is `true` and `InsertUpdateTargetTable` is `"dbo.T"`

#### Scenario: Flag is false in VALUES clause
- **WHEN** the SQL is `INSERT INTO dbo.T (col1) VALUES (|)`
- **THEN** `IsInsertColumnList` is `false`

#### Scenario: Schema prefix is preserved in target table name
- **WHEN** the SQL is `INSERT INTO hr.Employees (`
- **THEN** `InsertUpdateTargetTable` is `"hr.Employees"` (schema included)
