using System.Collections.Generic;

namespace SsmsAutocompletion {

    internal interface IScopedColumnResolver {
        IReadOnlyList<CompletionItem> GetVisibleColumns(CompletionRequest request);
    }
}
