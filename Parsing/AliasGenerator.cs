using System.Collections.Generic;
using System.Text;

namespace SsmsAutocompletion {

    internal static class AliasGenerator {

        /// <summary>
        /// Generates an alias from a table name using uppercase letters and
        /// letters immediately after underscores, then disambiguates with a suffix.
        /// Examples:
        ///   CustomerOrder    → co
        ///   order_details    → od
        ///   SalesOrderHeader → soh
        ///   Customer_Order   → co
        /// </summary>
        public static string Generate(string tableName, ISet<string> existingAliases) {
            tableName = StripSchemaAndBrackets(tableName);
            string baseAlias = BuildBaseAlias(tableName);
            if (!existingAliases.Contains(baseAlias)) return baseAlias;
            return FindAvailableAlias(baseAlias, existingAliases);
        }

        private static string StripSchemaAndBrackets(string tableName) {
            int dotIndex = tableName.LastIndexOf('.');
            if (dotIndex >= 0) tableName = tableName.Substring(dotIndex + 1);
            return tableName.Trim('[', ']');
        }

        private static string BuildBaseAlias(string tableName) {
            var result              = new StringBuilder();
            bool isFirstChar        = true;
            bool previousWasUnderscore = false;

            foreach (char character in tableName) {
                if (character == '_') {
                    previousWasUnderscore = true;
                    continue;
                }

                bool shouldInclude = isFirstChar
                                  || previousWasUnderscore
                                  || char.IsUpper(character);

                if (shouldInclude)
                    result.Append(char.ToLowerInvariant(character));

                isFirstChar           = false;
                previousWasUnderscore = false;
            }

            if (result.Length > 0) return result.ToString();
            return tableName.Length > 0
                ? char.ToLowerInvariant(tableName[0]).ToString()
                : "t";
        }

        private static string FindAvailableAlias(string baseAlias, ISet<string> existingAliases) {
            for (int suffix = 2; suffix < 100; suffix++) {
                string candidate = baseAlias + suffix;
                if (!existingAliases.Contains(candidate)) return candidate;
            }
            return baseAlias;
        }
    }
}
