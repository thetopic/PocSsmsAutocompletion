using System;
using System.Collections.Generic;
using System.Linq;

namespace SsmsAutocompletion {

    /// <summary>
    /// Suggère les colonnes d'une CTE quand l'utilisateur tape  cteName.  ou  alias.
    /// où l'alias référence une CTE définie dans le WITH courant.
    /// </summary>
    internal sealed class CteColumnCompletionProvider : ICompletionProvider {
        private readonly ICteExtractor       _cteExtractor;
        private readonly ICteColumnExtractor _cteColumnExtractor;
        private readonly IAliasExtractor     _aliasExtractor;

        public CteColumnCompletionProvider(
            ICteExtractor       cteExtractor,
            ICteColumnExtractor cteColumnExtractor,
            IAliasExtractor     aliasExtractor) {
            _cteExtractor       = cteExtractor;
            _cteColumnExtractor = cteColumnExtractor;
            _aliasExtractor     = aliasExtractor;
        }

        public IReadOnlyList<CompletionItem> GetCompletions(CompletionRequest request) {
            if (!request.IsDotContext)            return Array.Empty<CompletionItem>();
            if (string.IsNullOrEmpty(request.Qualifier)) return Array.Empty<CompletionItem>();

            string cteName = ResolveCteNameFromQualifier(request);
            if (cteName == null) return Array.Empty<CompletionItem>();

            var columns = _cteColumnExtractor.ExtractColumns(request.Sql, cteName);
            if (columns.Count == 0) return Array.Empty<CompletionItem>();

            var items = new List<CompletionItem>(columns.Count);
            foreach (string col in columns)
                items.Add(new CompletionItem(col, col, $"Colonne CTE ({cteName})", CompletionItemKind.Column));
            return items.AsReadOnly();
        }

        /// <summary>
        /// Résout le qualifier en nom de CTE :
        ///   - le qualifier est directement un nom de CTE (ex: MaCte.col)
        ///   - le qualifier est un alias qui pointe vers une CTE (ex: mc.col  où mc = MaCte)
        /// </summary>
        private string ResolveCteNameFromQualifier(CompletionRequest request) {
            var cteNames = _cteExtractor.Extract(request.Sql);
            if (cteNames.Count == 0) return null;

            // Correspondance directe
            string direct = cteNames.FirstOrDefault(
                n => string.Equals(n, request.Qualifier, StringComparison.OrdinalIgnoreCase));
            if (direct != null) return direct;

            // Via alias :  FROM MaCte mc  →  qualifier = "mc"
            var aliasMap = request.ParseResult != null
                ? _aliasExtractor.Extract(request.ParseResult)
                : _aliasExtractor.Extract(request.Sql);

            if (aliasMap.TryGetValue(request.Qualifier.ToLowerInvariant(), out var tableInfo)) {
                string viaAlias = cteNames.FirstOrDefault(
                    n => string.Equals(n, tableInfo.TableName, StringComparison.OrdinalIgnoreCase));
                if (viaAlias != null) return viaAlias;
            }

            return null;
        }
    }
}
