## Why

When the user picks a table from the FROM/JOIN completion popup, the popup immediately re-opens and displays a single alias suggestion (e.g. `o` for `Orders`). The user must press ENTER or TAB a second time to accept it. This is friction: alias assignment is almost always desired, and the generated alias is deterministic — there is no meaningful choice to present in the popup.

Accepting a table should silently insert the alias in the same gesture, leaving the cursor ready to continue typing.

## What Changes

- `SqlCommandFilter.OnItemCommitted` — replace the re-trigger call with a direct buffer insert of the computed alias.
- `SqlCommandFilter` constructor — add `IAliasExtractor` and `ISqlParser` parameters so the alias can be computed from the current buffer state.
- `Editor/VsTextViewCreationSqlListener.cs` — pass the two new dependencies when constructing `SqlCommandFilter`.

`AliasCompletionProvider` is left unchanged: it still fires when the user manually triggers completion (Ctrl+Space) while the cursor sits directly after a table name.

## Capabilities

### Modified Capabilities

- `alias-suggestion`: Previously required the user to select the alias from a popup after committing a table. Now the alias is auto-inserted at the moment the table is committed, with no extra keypress.

## Impact

- Modified files: `Editor/SqlCommandFilter.cs`, `Editor/VsTextViewCreationSqlListener.cs`.
- No new files, no new provider registrations, no new flags on `CompletionRequest`.
- No breaking changes. `AliasCompletionProvider` and `AliasGenerator` are untouched.
