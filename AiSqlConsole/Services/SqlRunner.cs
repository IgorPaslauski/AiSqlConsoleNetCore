using Npgsql;

namespace AiSqlConsole.Services;

public static class SqlRunner
{
    public static async Task<(bool ok, Exception? error)> ExecuteAndPrintAsync(NpgsqlConnection conn, string sql)
    {
        try
        {
            await using var cmd = new NpgsqlCommand(sql, conn) { CommandTimeout = 120 };
            await using var rd = await cmd.ExecuteReaderAsync();

            var schema = rd.GetColumnSchema();
            Console.WriteLine($"\nExecutando:\n{sql}\n");

            int rows = 0;
            while (await rd.ReadAsync())
            {
                rows++;
                for (int i = 0; i < schema.Count; i++)
                {
                    var name = schema[i].ColumnName ?? $"c{i}";
                    var val = await rd.IsDBNullAsync(i) ? "NULL" : rd.GetValue(i);
                    Console.Write(i == 0 ? $"{name}={val}" : $" | {name}={val}");
                }

                Console.WriteLine();
            }

            Console.WriteLine($"\n{rows} linha(s).");
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex);
        }
    }
}