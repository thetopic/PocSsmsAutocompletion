using System;
using System.Collections.Generic;

namespace SsmsAutocompletion {

    internal sealed class StoredProcedureParameterCompletionProvider : ICompletionProvider {
        private readonly IDatabaseMetadata _databaseMetadata;

        public StoredProcedureParameterCompletionProvider(IDatabaseMetadata databaseMetadata) {
            _databaseMetadata = databaseMetadata;
        }

        public IReadOnlyList<CompletionItem> GetCompletions(CompletionRequest request) {
            if (!request.IsInsideProcedureCall)                                    return Array.Empty<CompletionItem>();
            if (string.IsNullOrEmpty(request.ProcedureNameBeforeCursor))           return Array.Empty<CompletionItem>();
            if (request.ConnectionKey == null || request.ConnectionKey.IsEmpty)    return Array.Empty<CompletionItem>();

            var procedures   = _databaseMetadata.GetProcedures(request.ConnectionKey);
            ProcedureInfo proc = FindProcedure(procedures, request.ProcedureNameBeforeCursor);
            if (proc == null) return Array.Empty<CompletionItem>();

            var already = new HashSet<string>(request.AlreadyProvidedParameters, StringComparer.OrdinalIgnoreCase);
            var items   = new List<CompletionItem>();
            foreach (var param in proc.Parameters) {
                if (already.Contains(param.Name.ToLowerInvariant())) continue;
                string display = param.Name;
                string insert  = param.Name + " = ";
                string desc    = param.DataType + (param.IsOutput ? " OUTPUT" : "") + (param.HasDefault ? " (optional)" : "");
                items.Add(new CompletionItem(display, insert, desc, CompletionItemKind.Parameter));
            }
            return items.AsReadOnly();
        }

        private static ProcedureInfo FindProcedure(
            IReadOnlyList<ProcedureInfo> procedures, string procName) {
            // Try exact match including schema (e.g. "dbo.MyProc")
            foreach (var proc in procedures) {
                if (string.Equals(proc.ToString(), procName, StringComparison.OrdinalIgnoreCase))
                    return proc;
            }
            // Try matching just the procedure name (no schema in procName)
            int dot = procName.IndexOf('.');
            if (dot < 0) {
                foreach (var proc in procedures) {
                    if (string.Equals(proc.ProcedureName, procName, StringComparison.OrdinalIgnoreCase))
                        return proc;
                }
            } else {
                string schema = procName.Substring(0, dot);
                string name   = procName.Substring(dot + 1);
                foreach (var proc in procedures) {
                    if (string.Equals(proc.Schema, schema, StringComparison.OrdinalIgnoreCase)
                     && string.Equals(proc.ProcedureName, name, StringComparison.OrdinalIgnoreCase))
                        return proc;
                }
            }
            return null;
        }
    }
}
