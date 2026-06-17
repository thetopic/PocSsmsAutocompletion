## MODIFIED Requirements

### Requirement: Suggest JOIN/WHERE conditions from similarly-named columns

The system SHALL suggest `alias.Column = alias.Column` items for pairs of columns with identical or similar names across the tables referenced in the query, when the cursor is in a JOIN `ON` clause or a `WHERE` clause and at least two tables are referenced. The system SHALL NOT suggest a column pair that is already part of a foreign-key relation between the two tables, and SHALL mark its suggestions with `Rank = 1` so they are ordered after foreign-key-based and other suggestions.

#### Scenario: Identically-named columns with no FK relation are suggested

- **WHEN** two joined tables both have a column named `Status`, and there is no FK relation between the two tables on that column
- **THEN** `aliasA.Status = aliasB.Status` is suggested with `Rank == 1`

#### Scenario: Column pair covered by an FK relation is excluded

- **WHEN** `tableA.CustomerID` is a foreign key referencing `tableB.CustomerID`
- **THEN** this provider does NOT suggest `aliasA.CustomerID = aliasB.CustomerID` (it is left to the FK-based provider)

#### Scenario: Column pair covered by a composite FK relation is excluded

- **WHEN** a composite FK relates `tableA.(Col1, Col2)` to `tableB.(Col1, Col2)`
- **THEN** this provider does NOT suggest `aliasA.Col1 = aliasB.Col1` nor `aliasA.Col2 = aliasB.Col2`

#### Scenario: FK relation in the reverse direction is also excluded

- **WHEN** `tableB.OrderID` is a foreign key referencing `tableA.OrderID`
- **THEN** this provider does NOT suggest `aliasA.OrderID = aliasB.OrderID`

#### Scenario: Provider does not fire outside JOIN ON / WHERE, or with fewer than two tables

- **WHEN** `IsDotContext` is true, or neither `IsJoinOnContext` nor `IsWhereContext` is true, or fewer than two tables are referenced
- **THEN** the provider returns no items (unchanged from prior behavior)
