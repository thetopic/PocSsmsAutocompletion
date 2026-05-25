using System;
using System.Collections.Generic;

namespace SsmsAutocompletion {

    internal sealed class FunctionCompletionProvider : ICompletionProvider {

        // Each entry: (name, description, hasArguments)
        // Insert text adds '(' for functions with arguments so the user types directly inside.
        private static readonly IReadOnlyList<(string Name, string Description)> Functions =
            new List<(string, string)> {
                // ── Null / conditional ────────────────────────────────────────────
                ("COALESCE",           "Retourne le premier argument non NULL"),
                ("NULLIF",             "Retourne NULL si les deux arguments sont égaux"),
                ("ISNULL",             "Remplace NULL par une valeur de substitution"),
                ("IIF",                "IF inline : IIF(condition, vrai, faux)"),
                ("CHOOSE",             "Retourne l'élément à l'index donné"),
                // ── Type conversion ───────────────────────────────────────────────
                ("CAST",               "Convertit une expression vers un type : CAST(x AS type)"),
                ("CONVERT",            "Convertit avec style optionnel : CONVERT(type, x, style)"),
                ("TRY_CAST",           "CAST sans exception sur échec (retourne NULL)"),
                ("TRY_CONVERT",        "CONVERT sans exception sur échec (retourne NULL)"),
                ("TRY_PARSE",          "Parse une chaîne vers un type (retourne NULL si échec)"),
                ("PARSE",              "Parse une chaîne vers un type (lève une erreur si échec)"),
                // ── String ────────────────────────────────────────────────────────
                ("TRIM",               "Supprime espaces (ou caractères) en début et fin"),
                ("LTRIM",              "Supprime espaces à gauche"),
                ("RTRIM",              "Supprime espaces à droite"),
                ("LEN",                "Longueur de la chaîne (sans espaces de fin)"),
                ("DATALENGTH",         "Longueur en octets"),
                ("LEFT",               "N caractères depuis la gauche"),
                ("RIGHT",              "N caractères depuis la droite"),
                ("SUBSTRING",          "Sous-chaîne : SUBSTRING(str, start, length)"),
                ("CHARINDEX",          "Position d'une sous-chaîne : CHARINDEX(search, str)"),
                ("PATINDEX",           "Position d'un pattern LIKE : PATINDEX('%pat%', str)"),
                ("REPLACE",            "Remplace toutes les occurrences"),
                ("STUFF",              "Insère une chaîne à une position"),
                ("REPLICATE",          "Répète une chaîne N fois"),
                ("REVERSE",            "Inverse une chaîne"),
                ("UPPER",              "Convertit en majuscules"),
                ("LOWER",              "Convertit en minuscules"),
                ("CONCAT",             "Concatène des chaînes (NULL ignoré)"),
                ("CONCAT_WS",          "Concatène avec séparateur"),
                ("STRING_AGG",         "Agrège des chaînes avec séparateur"),
                ("FORMAT",             "Formate une valeur avec un format .NET"),
                ("SPACE",              "Retourne N espaces"),
                ("STR",                "Convertit un nombre en chaîne"),
                ("CHAR",               "Retourne le caractère ASCII du code donné"),
                ("UNICODE",            "Retourne le code Unicode du premier caractère"),
                ("SOUNDEX",            "Code phonétique d'une chaîne"),
                ("DIFFERENCE",         "Différence phonétique entre deux chaînes"),
                ("TRANSLATE",          "Remplace des caractères individuels"),
                ("STRING_ESCAPE",      "Échappe les caractères spéciaux JSON/XML"),
                // ── Numeric / math ────────────────────────────────────────────────
                ("ABS",                "Valeur absolue"),
                ("CEILING",            "Arrondi à l'entier supérieur"),
                ("FLOOR",              "Arrondi à l'entier inférieur"),
                ("ROUND",              "Arrondi au nombre de décimales donné"),
                ("SQRT",               "Racine carrée"),
                ("SQUARE",             "Carré"),
                ("POWER",              "Puissance : POWER(base, exp)"),
                ("LOG",                "Logarithme naturel (ou en base N)"),
                ("LOG10",              "Logarithme en base 10"),
                ("EXP",                "Exponentielle"),
                ("SIGN",               "Signe : -1, 0 ou 1"),
                ("RAND",               "Nombre aléatoire entre 0 et 1"),
                ("PI",                 "Valeur de π"),
                ("SIN",                "Sinus"),
                ("COS",                "Cosinus"),
                ("TAN",                "Tangente"),
                ("ASIN",               "Arc sinus"),
                ("ACOS",               "Arc cosinus"),
                ("ATAN",               "Arc tangente"),
                ("ATN2",               "Arc tangente de y/x"),
                ("DEGREES",            "Convertit radians en degrés"),
                ("RADIANS",            "Convertit degrés en radians"),
                ("COT",                "Cotangente"),
                // ── Date / time ───────────────────────────────────────────────────
                ("GETDATE",            "Date et heure courantes (datetime)"),
                ("GETUTCDATE",         "Date et heure UTC courantes"),
                ("SYSDATETIME",        "Date et heure courantes (datetime2, haute précision)"),
                ("SYSUTCDATETIME",     "Date et heure UTC (datetime2, haute précision)"),
                ("DATEADD",            "Ajoute un intervalle : DATEADD(part, n, date)"),
                ("DATEDIFF",           "Différence entre deux dates : DATEDIFF(part, d1, d2)"),
                ("DATEDIFF_BIG",       "DATEDIFF en bigint"),
                ("DATEPART",           "Extrait une partie de date (entier)"),
                ("DATENAME",           "Extrait une partie de date (chaîne)"),
                ("YEAR",               "Extrait l'année"),
                ("MONTH",              "Extrait le mois"),
                ("DAY",                "Extrait le jour"),
                ("EOMONTH",            "Dernier jour du mois"),
                ("DATEFROMPARTS",      "Crée une date à partir de parties"),
                ("DATETIME2FROMPARTS", "Crée un datetime2 à partir de parties"),
                ("DATETIMEFROMPARTS",  "Crée un datetime à partir de parties"),
                ("TIMEFROMPARTS",      "Crée un time à partir de parties"),
                ("ISDATE",             "Vérifie si une chaîne est une date valide"),
                ("SWITCHOFFSET",       "Change le fuseau d'un datetimeoffset"),
                ("TODATETIMEOFFSET",   "Ajoute un offset à un datetime2"),
                // ── Aggregate ─────────────────────────────────────────────────────
                ("COUNT",              "Nombre de lignes"),
                ("COUNT_BIG",          "Nombre de lignes (bigint)"),
                ("SUM",                "Somme"),
                ("AVG",                "Moyenne"),
                ("MIN",                "Valeur minimale"),
                ("MAX",                "Valeur maximale"),
                ("STDEV",              "Écart-type d'un échantillon"),
                ("STDEVP",             "Écart-type de la population"),
                ("VAR",                "Variance d'un échantillon"),
                ("VARP",               "Variance de la population"),
                // ── Window ───────────────────────────────────────────────────────
                ("ROW_NUMBER",         "Numéro de ligne dans la partition"),
                ("RANK",               "Rang avec saut en cas d'égalité"),
                ("DENSE_RANK",         "Rang sans saut"),
                ("NTILE",              "Divise en N groupes"),
                ("LAG",                "Valeur de la ligne précédente"),
                ("LEAD",               "Valeur de la ligne suivante"),
                ("FIRST_VALUE",        "Première valeur de la fenêtre"),
                ("LAST_VALUE",         "Dernière valeur de la fenêtre"),
                ("CUME_DIST",          "Distribution cumulative"),
                ("PERCENT_RANK",       "Rang relatif en pourcentage"),
                // ── JSON ─────────────────────────────────────────────────────────
                ("JSON_VALUE",         "Extrait une valeur scalaire d'un JSON"),
                ("JSON_QUERY",         "Extrait un objet/tableau d'un JSON"),
                ("JSON_MODIFY",        "Modifie une valeur dans un JSON"),
                ("ISJSON",             "Vérifie si une chaîne est un JSON valide"),
                // ── System ───────────────────────────────────────────────────────
                ("NEWID",              "Génère un nouveau GUID"),
                ("NEWSEQUENTIALID",    "Génère un GUID séquentiel (défaut de colonne uniquement)"),
                ("SCOPE_IDENTITY",     "Dernière valeur d'identité insérée dans la portée"),
                ("IDENT_CURRENT",      "Dernière valeur d'identité d'une table donnée"),
                ("OBJECT_ID",          "ID de l'objet par son nom"),
                ("OBJECT_NAME",        "Nom de l'objet par son ID"),
                ("DB_NAME",            "Nom de la base de données courante"),
                ("DB_ID",              "ID de la base de données"),
                ("USER_NAME",          "Nom de l'utilisateur courant"),
                ("HOST_NAME",          "Nom du poste client"),
                ("APP_NAME",           "Nom de l'application cliente"),
                ("ERROR_MESSAGE",      "Message de l'erreur dans un bloc CATCH"),
                ("ERROR_NUMBER",       "Numéro de l'erreur dans un bloc CATCH"),
                ("ERROR_SEVERITY",     "Sévérité de l'erreur dans un bloc CATCH"),
                ("ERROR_STATE",        "État de l'erreur dans un bloc CATCH"),
                ("ERROR_LINE",         "Ligne de l'erreur dans un bloc CATCH"),
                ("XACT_STATE",         "État de la transaction courante"),
            }.AsReadOnly();

        private static readonly IReadOnlyList<CompletionItem> CachedItems = BuildItems();

        public IReadOnlyList<CompletionItem> GetCompletions(CompletionRequest request) {
            if (request.IsDotContext)       return Array.Empty<CompletionItem>();
            if (request.IsAfterFromKeyword) return Array.Empty<CompletionItem>();
            return CachedItems;
        }

        private static IReadOnlyList<CompletionItem> BuildItems() {
            var items = new List<CompletionItem>(Functions.Count);
            foreach (var (name, description) in Functions)
                items.Add(new CompletionItem(name, name + "(", description, CompletionItemKind.Function));
            return items.AsReadOnly();
        }
    }
}
