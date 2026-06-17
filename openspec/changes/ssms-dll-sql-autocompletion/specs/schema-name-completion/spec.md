## ADDED Requirements

### Requirement: Suggest schema names as a dot-context fallback
The system SHALL suggest schema names when `IsDotContext` is true and the qualifier before the dot does not match any known table, view, CTE name, or alias in the current query.

#### Scenario: Unknown qualifier triggers schema suggestions
- **WHEN** the user has typed `hr.` (cursor after the dot) and `hr` is a schema name in the database but not a table or alias
- **THEN** the popup shows tables/views under the `hr` schema (handled by `ColumnCompletionProvider` or `TableCompletionProvider`) AND the existing providers handle it; if no match, `SchemaCompletionProvider` returns nothing — the responsibility of `SchemaCompletionProvider` is only for the bare-prefix case (before the dot)

#### Scenario: Bare unrecognised identifier before dot shows schema list
- **WHEN** the user has typed `h` (not followed by a dot yet, but `IsDotContext` is false) and then types `.`
- **THEN** if `h` resolves to a known schema, `ColumnCompletionProvider` shows that schema's objects; if `h` is not a known schema, the `SchemaCompletionProvider` activates at `IsPartialSchemaContext` (see flag below) to show matching schema names

#### Scenario: Known table qualifier does not trigger schema suggestions
- **WHEN** the user types `dbo.` and `dbo` matches a known schema with objects
- **THEN** `SchemaCompletionProvider` returns no items (existing providers handle the dot context)

#### Scenario: Schema list is empty on miss
- **WHEN** `GetSchemas(key)` returns an empty list (cache miss or permission denied)
- **THEN** the provider returns no items silently

### Requirement: Schema names loaded and cached via `GetSchemas`
The system SHALL expose `IReadOnlyList<string> GetSchemas(ConnectionKey key)` on `IDatabaseMetadata` and load schema names from `sys.schemas` in `SystemCatalogMetadataLoader`.

#### Scenario: Schema names returned on cache hit
- **WHEN** the cache has been warmed for a given connection
- **THEN** `GetSchemas(key)` returns the list of schema names for that database without issuing a new SQL query

#### Scenario: Empty list returned on cache miss
- **WHEN** `GetSchemas(key)` is called before the cache has been warmed
- **THEN** it returns `Array.Empty<string>()` (consistent with other cache methods)

#### Scenario: Query failure returns empty list
- **WHEN** the SQL query against `sys.schemas` throws an exception (e.g. permission denied)
- **THEN** `GetSchemas(key)` returns `Array.Empty<string>()` and the exception is swallowed
