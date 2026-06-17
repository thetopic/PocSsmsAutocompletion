## ADDED Requirements

### Requirement: Suggest columns from all FROM-clause tables without requiring a qualifier
The system SHALL suggest column names drawn from all tables referenced in the current query's FROM clause when the cursor is in a SELECT list or WHERE clause and no table qualifier (dot) precedes the typed word.

#### Scenario: SELECT list with one table, no qualifier
- **WHEN** the user has typed `SELECT Fir` and the FROM clause references `dbo.Customers`
- **THEN** columns of `dbo.Customers` starting with `Fir` are shown (e.g. `FirstName`)

#### Scenario: SELECT list with multiple tables, no qualifier
- **WHEN** the query is `SELECT | FROM dbo.Orders o JOIN dbo.Customers c ON ...` (| = cursor in SELECT list)
- **THEN** columns from BOTH `dbo.Orders` and `dbo.Customers` are shown, merged and deduplicated by display text

#### Scenario: WHERE clause unqualified column
- **WHEN** the cursor is in `WHERE Status = '` and `Status` is a column of the FROM table
- **THEN** `Status` is offered as a column completion item

#### Scenario: Dot context suppresses unqualified provider
- **WHEN** the cursor follows a dot (e.g. `o.`) — `IsDotContext` is true
- **THEN** this provider returns no items (the qualified `ColumnCompletionProvider` handles it)

#### Scenario: Alias is resolved to the underlying table
- **WHEN** the query FROM clause is `dbo.Products p` and `p` is registered in `AliasExtractor`
- **THEN** columns of `dbo.Products` are returned (the alias-to-table mapping is followed)

#### Scenario: Cache miss for a referenced table returns partial results
- **WHEN** the FROM clause references two tables but only one is in the cache
- **THEN** columns of the cached table are shown; the uncached table contributes nothing

#### Scenario: Provider does not fire outside SELECT or WHERE
- **WHEN** the cursor is in a FROM clause, after FROM, or in an EXEC statement
- **THEN** the provider returns no items (`IsInSelectList` and `IsWhereContext` are both false)

### Requirement: `IsInSelectList` flag on CompletionRequest
The system SHALL compute `IsInSelectList` (bool) in `CompletionRequestBuilder` using a text-based backward keyword scan from the cursor position.

#### Scenario: Flag is true when nearest clause keyword is SELECT
- **WHEN** the SQL text before the cursor ends with `SELECT FirstN` (no intervening FROM/WHERE/etc.)
- **THEN** `IsInSelectList` is `true`

#### Scenario: Flag is false when nearest clause keyword is FROM
- **WHEN** the cursor is in `FROM dbo.`
- **THEN** `IsInSelectList` is `false`

#### Scenario: Flag is true inside a subquery SELECT list
- **WHEN** the cursor is in `WHERE id IN (SELECT |` (nested SELECT)
- **THEN** `IsInSelectList` is `true` (unqualified column completion is valid in subqueries)
