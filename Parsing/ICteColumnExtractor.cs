using System.Collections.Generic;

namespace SsmsAutocompletion {

    internal interface ICteColumnExtractor {
        /// <summary>
        /// Extrait les noms de colonnes exposées par une CTE donnée.
        /// Gère les colonnes explicites (WITH cte (col1, col2) AS …)
        /// et implicites (déduites du SELECT de la CTE).
        /// </summary>
        IReadOnlyList<string> ExtractColumns(string sql, string cteName);
    }
}
