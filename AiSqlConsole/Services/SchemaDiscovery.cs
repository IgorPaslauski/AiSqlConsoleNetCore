using System.Text;
using Npgsql;

namespace AiSqlConsole.Services;

public static class SchemaDiscovery
{
    public static async Task<string> BuildSchemaTextAsync(NpgsqlConnection conn, int maxTables, int maxColumns)
    {
        const string sqlCols = @"
SELECT c.table_schema,
       c.table_name,
       c.column_name,
       c.ordinal_position
FROM information_schema.columns c
JOIN information_schema.tables t
  ON t.table_schema = c.table_schema AND t.table_name = c.table_name
WHERE c.table_schema NOT IN ('pg_catalog','information_schema')
  AND t.table_type IN ('BASE TABLE','VIEW')
ORDER BY c.table_schema, c.table_name, c.ordinal_position;";

        var tables = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        await using (var cmd = new NpgsqlCommand(sqlCols, conn))
        await using (var rd = await cmd.ExecuteReaderAsync())
        {
            while (await rd.ReadAsync())
            {
                var schema = rd.GetString(0);
                var table = rd.GetString(1);
                var col = rd.GetString(2);

                var key = $"{schema}.{table}";
                if (!tables.TryGetValue(key, out var cols))
                {
                    cols = new List<string>();
                    tables[key] = cols;
                }

                if (cols.Count < maxColumns) cols.Add(col);
            }
        }

        const string sqlFks = @"
            SELECT tc.table_schema AS child_schema,
                   tc.table_name   AS child_table,
                   kcu.column_name AS child_column,
                   ccu.table_schema AS parent_schema,
                   ccu.table_name   AS parent_table,
                   ccu.column_name  AS parent_column
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
              ON tc.constraint_name = kcu.constraint_name
             AND tc.table_schema   = kcu.table_schema
            JOIN information_schema.constraint_column_usage ccu
              ON ccu.constraint_name = tc.constraint_name
             AND ccu.table_schema   = tc.table_schema
            WHERE tc.constraint_type = 'FOREIGN KEY'
              AND tc.table_schema NOT IN ('pg_catalog','information_schema')
            ORDER BY child_schema, child_table, child_column;";

        var fks = new List<string>();
        await using (var cmd = new NpgsqlCommand(sqlFks, conn))
        await using (var rd = await cmd.ExecuteReaderAsync())
        {
            while (await rd.ReadAsync())
            {
                var childS = rd.GetString(0);
                var childT = rd.GetString(1);
                var childC = rd.GetString(2);
                var parentS = rd.GetString(3);
                var parentT = rd.GetString(4);
                var parentC = rd.GetString(5);
                fks.Add($"{childS}.{childT}({childC}) -> {parentS}.{parentT}({parentC})");
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("Tabelas/colunas:");
        foreach (var kv in tables.OrderBy(k => k.Key).Take(maxTables))
            sb.Append("- ").Append(kv.Key).Append('(').Append(string.Join(", ", kv.Value)).AppendLine(")");

        if (fks.Count > 0)
        {
            sb.AppendLine("Relações (FK):");
            foreach (var fk in fks.Take(500)) sb.AppendLine("- " + fk);
        }

        return sb.ToString();
    }
}