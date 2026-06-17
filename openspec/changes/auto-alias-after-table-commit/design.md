## Context

`SqlCommandFilter` intercepts all editor keystrokes. When RETURN or TAB is pressed and the popup is visible, it calls `AutoCompletePopup.Commit()`, which replaces the word span with `selectedItem.InsertText` and fires the `Committed` event. `OnItemCommitted` then re-triggers the completion engine so the popup re-opens for alias or JOIN suggestions.

`AliasCompletionProvider` is the only provider that activates for the alias step: it fires when `IsAfterTableInFromJoin` is true, generates one alias via `AliasGenerator.Generate`, and returns it as the sole popup item.

## Goals / Non-Goals

**Goals:**
- Insert the alias immediately after the table name, without a second popup, when a Table or View item is committed from the popup.
- Use the same alias generation logic (`AliasGenerator.Generate`) already in use.
- Respect existing aliases already present in the SQL so the generated alias is collision-free.

**Non-Goals:**
- Auto-alias for CTE items or subquery aliases.
- Allowing the user to choose among multiple alias candidates — the generated alias is inserted without confirmation.
- Removing `AliasCompletionProvider` (it stays as a fallback for manual triggers).

## Decisions

### D1 — Direct buffer insert instead of popup re-trigger

**Decision**: In `OnItemCommitted`, when `committed.Kind` is `Table` or `View`, insert `alias + " "` at the current caret position using `_textView.TextBuffer.Insert`, then move the caret forward by `alias.Length + 1`.

**Rationale**: Re-triggering the popup forces a second keypress. A direct insert is a single atomic action, consistent with how other editors (e.g. VS IntelliSense for `using` imports) auto-complete secondary tokens.

**Alternative considered**: Keep the popup but auto-commit it immediately (zero-delay `Commit()` call in `OnItemCommitted`). Rejected because it causes a visible popup flash and relies on timing between the UI thread and the background trigger task.

### D2 — Alias extraction: inject `ISqlParser` + `IAliasExtractor` into `SqlCommandFilter`

**Decision**: Add two constructor parameters to `SqlCommandFilter`: `ISqlParser sqlParser` and `IAliasExtractor aliasExtractor`. In `OnItemCommitted`, parse the post-commit snapshot synchronously and call `aliasExtractor.Extract(parseResult).Keys` to build the existing-alias set before calling `AliasGenerator.Generate`.

**Rationale**: `SqlCommandFilter` already accesses the text buffer synchronously on the UI thread in `OnItemCommitted`. The parse is fast (same parser used by `CompletionRequestBuilder`). Both `ISqlParser` and `IAliasExtractor` are already instantiated in `VsTextViewCreationSqlListener` and can be passed at construction time.

**Alternative considered**: Expose a method on `IContextDetector` that returns existing aliases directly from `ITextSnapshot`. Rejected because it adds a new contract to an interface that is primarily about cursor-position context, not document-wide extraction.

### D3 — Kind check, not Description check

**Decision**: Change the guard in `OnItemCommitted` from `committed?.Description == "Table"` / `"View"` to `committed.Kind == CompletionItemKind.Table` / `CompletionItemKind.View`.

**Rationale**: `Description` is a display string; `Kind` is a typed enum. The current Description check is fragile — it would silently break if `TableCompletionProvider` ever changes its description text. `Kind` is the canonical discriminator.

### D4 — No re-trigger after alias insertion

**Decision**: After inserting the alias, do not call `TriggerCompletionFromExplicitCommand()`.

**Rationale**: After `FROM Orders o`, the cursor sits after `o `. Neither the alias provider nor any other provider would fire usefully at that position (the JOIN snippet providers require an explicit `ON` keyword to be typed). The user continues typing naturally; Ctrl+Space is always available.

## Risks / Trade-offs

- **Unwanted alias** — A user who does not want an alias (e.g. `SELECT * FROM Orders WHERE ...` with no alias) will have an alias inserted and must delete it. This is a UX trade-off inherent in the feature. The alias is short (1–3 chars) and easy to delete.
- **UI thread parse latency** — Parsing is synchronous and happens on the UI thread. For large SQL files this could add a few milliseconds. In practice SSMS SQL editors hold short-to-medium queries; the risk is low.
- **Caret positioning** — `TextBuffer.Insert` does not automatically advance the caret in all VS editor versions. The implementation must explicitly move the caret via `_textView.Caret.MoveTo(new SnapshotPoint(...))`.
