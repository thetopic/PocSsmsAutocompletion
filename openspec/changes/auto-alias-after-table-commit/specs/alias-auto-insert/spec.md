## MODIFIED Requirements

### Requirement: Alias is inserted automatically when a table is committed from the popup

The system SHALL insert the generated alias directly into the buffer — without a second popup — immediately after a Table or View item is committed from the completion popup.

#### Scenario: Table committed in a FROM clause — alias appended automatically
- **GIVEN** the SQL buffer contains `SELECT * FROM ` and the popup is showing table names
- **WHEN** the user selects `Orders` and presses ENTER or TAB
- **THEN** the buffer becomes `SELECT * FROM Orders o ` (alias appended, single trailing space)
- **AND** the caret is positioned after `o `, ready for the user to continue typing
- **AND** no second popup appears

#### Scenario: Alias is collision-free when other aliases already exist
- **GIVEN** the SQL buffer already contains `SELECT * FROM Orders o JOIN ` and the popup is showing table names
- **WHEN** the user selects `OrderItems` and presses ENTER or TAB
- **THEN** `AliasGenerator.Generate("OrderItems", {"o"})` is used to derive the alias
- **AND** the alias inserted avoids `o` (already taken), e.g. `oi`

#### Scenario: View committed — alias appended the same way
- **GIVEN** the popup shows a View item (e.g. `v_ActiveOrders`)
- **WHEN** the user commits it
- **THEN** the buffer receives `v_ActiveOrders vao ` with no second popup

#### Scenario: Non-table item committed — no auto-alias
- **GIVEN** the popup shows a keyword, column, stored procedure, or function item
- **WHEN** the user commits it
- **THEN** behaviour is unchanged: the existing commit logic runs, no alias is inserted

#### Scenario: Alias already present (user typed it manually) — no double alias
- **GIVEN** the SQL buffer is `SELECT * FROM Orders o JOIN Customers ` and the popup shows `Customers`
- **WHEN** the user selects `Customers` from the popup (it was typed partially)
- **THEN** only the alias for `Customers` is inserted; the alias `o` for `Orders` (already present) is NOT re-inserted

### Requirement: `AliasCompletionProvider` remains available for manual triggers

The system SHALL still show the alias suggestion in the popup when the user places the cursor directly after a table name and triggers completion manually (Ctrl+Space / SHOWMEMBERLIST command), without having committed the table from the popup.

#### Scenario: Manual trigger after a table name — alias popup appears
- **GIVEN** the user has typed `SELECT * FROM Orders ` (space typed manually, not via popup commit)
- **WHEN** the user presses Ctrl+Space
- **THEN** the popup shows the alias suggestion `o` as before
