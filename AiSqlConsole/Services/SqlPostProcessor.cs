using System.Text.RegularExpressions;

namespace AiSqlConsole.Services;

public static class SqlPostProcessor
{
    public static string FixCommonSyntaxIssues(string sql, int limit)
    {
        if (string.IsNullOrWhiteSpace(sql)) return sql;

        // remove fences
        sql = Regex.Replace(sql, @"^\s*```(?:sql)?\s*([\s\S]*?)\s*```\s*$", "$1", RegexOptions.IgnoreCase);

        // remove ; final
        sql = Regex.Replace(sql, @";\s*$", "", RegexOptions.Multiline);

        // vírgula indevida antes de SELECT após CTEs
        sql = Regex.Replace(sql, @"\)\s*,\s*SELECT", ") SELECT", RegexOptions.IgnoreCase);
        sql = Regex.Replace(sql, @"\)\s*,\s*\n\s*SELECT", ") SELECT", RegexOptions.IgnoreCase);

        // WITH vazio → SELECT
        sql = Regex.Replace(sql, @"^\s*WITH\s*,\s*SELECT", "SELECT", RegexOptions.IgnoreCase);

        // garantir SELECT/WITH
        var s = sql.TrimStart();
        if (!(s.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) || s.StartsWith("WITH", StringComparison.OrdinalIgnoreCase)))
            throw new Exception("Apenas SELECT/CTE são permitidos.");

        // garantir LIMIT simples
        if (!Regex.IsMatch(sql, @"\bLIMIT\b", RegexOptions.IgnoreCase))
            sql += $" LIMIT {limit}";

        return sql.Trim();
    }
}