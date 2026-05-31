using System;
using System.Collections.Generic;
using System.Text;

namespace SsmsAutocompletion {

    internal sealed class UserDefinedFunctionCompletionProvider : ICompletionProvider {
        private readonly IDatabaseMetadata _databaseMetadata;

        public UserDefinedFunctionCompletionProvider(IDatabaseMetadata databaseMetadata) {
            _databaseMetadata = databaseMetadata;
        }

        public IReadOnlyList<CompletionItem> GetCompletions(CompletionRequest request) {
            if (request.IsDotContext)    return Array.Empty<CompletionItem>();
            if (request.IsAfterExecKeyword) return Array.Empty<CompletionItem>();
            if (request.ConnectionKey == null || request.ConnectionKey.IsEmpty)
                return Array.Empty<CompletionItem>();

            var functions = _databaseMetadata.GetUserDefinedFunctions(request.ConnectionKey);
            var items     = new List<CompletionItem>(functions.Count);
            foreach (var fn in functions) {
                if (request.IsAfterFromKeyword && !fn.IsTableValued) continue;
                if (!request.IsAfterFromKeyword && fn.IsTableValued)  continue;
                var display     = fn.ToString();
                var description = BuildDescription(fn);
                items.Add(new CompletionItem(display, display + "(", description,
                    CompletionItemKind.UserDefinedFunction));
            }
            return items.AsReadOnly();
        }

        private static string BuildDescription(UserFunctionInfo fn) {
            if (fn.Parameters.Count == 0)
                return fn.IsTableValued ? "Table-Valued Function" : "Scalar Function";
            var sb = new StringBuilder();
            for (int i = 0; i < fn.Parameters.Count; i++) {
                if (i > 0) sb.Append(", ");
                var p = fn.Parameters[i];
                sb.Append(p.Name).Append(' ').Append(p.DataType);
            }
            return sb.ToString();
        }
    }
}
