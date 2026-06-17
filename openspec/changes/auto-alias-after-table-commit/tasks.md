## 1. `SqlCommandFilter` — inject alias dependencies and auto-insert alias

- [x] 1.1 Add `ISqlParser _sqlParser` and `IAliasExtractor _aliasExtractor` private fields to `Editor/SqlCommandFilter.cs`
- [x] 1.2 Add `ISqlParser sqlParser` and `IAliasExtractor aliasExtractor` parameters to the `SqlCommandFilter` constructor; assign the fields
- [x] 1.3 Replace the `OnItemCommitted` body:
  - Change the guard from `committed?.Description == "Table" || "View"` to `committed?.Kind == CompletionItemKind.Table || committed?.Kind == CompletionItemKind.View`
  - Parse the post-commit snapshot: `var pr = _sqlParser.Parse(snapshot.GetText())`
  - Extract existing aliases: `new HashSet<string>(_aliasExtractor.Extract(pr).Keys, StringComparer.OrdinalIgnoreCase)`
  - Compute alias: `AliasGenerator.Generate(committed.DisplayText, existingAliases)`
  - Insert: `_textView.TextBuffer.Insert(caretPos, alias + " ")`
  - Move caret: `_textView.Caret.MoveTo(new SnapshotPoint(_textView.TextBuffer.CurrentSnapshot, caretPos + alias.Length + 1))`
  - Remove the `TriggerCompletionFromExplicitCommand()` call

## 2. `VsTextViewCreationSqlListener` — pass new dependencies

- [x] 2.1 In `VsTextViewCreationSqlListener.cs`, update the `SqlCommandFilter` constructor call to pass `SqlParser` and `AliasExtractor`

## 3. Integration Smoke Test

- [ ] 3.1 Build and deploy to the experimental SSMS instance (`Ctrl+Shift+B` then launch via `/rootsuffix Exp`)
- [ ] 3.2 Type `SELECT * FROM `, select a table from the popup with ENTER → verify `TableName alias ` appears with no second popup
- [ ] 3.3 Type `SELECT * FROM Orders o JOIN `, select another table → verify alias avoids `o`
- [ ] 3.4 Manually type `SELECT * FROM Orders ` (space typed, no popup commit), press Ctrl+Space → verify alias popup still appears
- [ ] 3.5 Commit a keyword, column, or stored procedure → verify no alias is inserted
