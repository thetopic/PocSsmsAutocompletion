using System;
using System.Collections.Generic;
using System.Text;

namespace SsmsAutocompletion {

    internal sealed class StoredProcedureCompletionProvider : ICompletionProvider {
        private readonly IDatabaseMetadata _databaseMetadata;

        public StoredProcedureCompletionProvider(IDatabaseMetadata databaseMetadata) {
            _databaseMetadata = databaseMetadata;
        }

        public IReadOnlyList<CompletionItem> GetCompletions(CompletionRequest request) {
            if (!request.IsAfterExecKeyword) return Array.Empty<CompletionItem>();
            if (request.IsDotContext)        return Array.Empty<CompletionItem>();
            if (request.ConnectionKey == null || request.ConnectionKey.IsEmpty)
                return Array.Empty<CompletionItem>();

            var procedures = _databaseMetadata.GetProcedures(request.ConnectionKey);
            var items      = new List<CompletionItem>(procedures.Count);
            foreach (var proc in procedures) {
                var display     = proc.ToString();
                var description = BuildDescription(proc);
                items.Add(new CompletionItem(display, display + " ", description, CompletionItemKind.StoredProcedure));
            }
            return items.AsReadOnly();
        }

        private static string BuildDescription(ProcedureInfo proc) {
            if (proc.Parameters.Count == 0) return "Stored Procedure";
            var sb = new StringBuilder();
            for (int i = 0; i < proc.Parameters.Count; i++) {
                if (i > 0) sb.Append(", ");
                var p = proc.Parameters[i];
                sb.Append(p.Name).Append(' ').Append(p.DataType);
                if (p.IsOutput) sb.Append(" OUTPUT");
            }
            return sb.ToString();
        }
    }
}
