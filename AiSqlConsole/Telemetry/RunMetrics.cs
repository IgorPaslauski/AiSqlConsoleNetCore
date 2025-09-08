namespace AiSqlConsole.Telemetry;

public sealed class RunMetrics
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string SessionId { get; init; } = Guid.NewGuid().ToString("N");
    public string Question { get; set; } = "";
    public string Model { get; set; } = "";
    public int Attempt { get; set; } = 1;

    public bool Success { get; set; }
    public int Rows { get; set; } = 0;
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }

    public long DurationMsTotal { get; set; }
    public long DurationMsLlm { get; set; }
    public long DurationMsSql { get; set; }

    public string Sql { get; set; } = "";
    public string TablesUsed { get; set; } = "";
    public string ColumnsUsed { get; set; } = "";
    public int CatalogTables { get; set; } = 0;
    public int CatalogColumns { get; set; } = 0;

    public int SystemPromptChars { get; set; } = 0;
    public int UserPromptChars { get; set; } = 0;

    public static RunMetrics Start(string question, string model, int attempt) => new() { Question = question, Model = model, Attempt = attempt };

    public static string CsvHeader => string.Join(",",
        "timestamp_utc", "session_id", "question", "model", "attempt",
        "success", "rows", "error_code", "error_message",
        "dur_ms_total", "dur_ms_llm", "dur_ms_sql",
        "sql", "tables_used", "columns_used", "catalog_tables", "catalog_columns",
        "system_prompt_chars", "user_prompt_chars"
    );

    public string ToCsvLine()
    {
        static string Q(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "\"\"";
            var esc = s.Replace("\"", "\"\"");
            return $"\"{esc}\"";
        }

        return string.Join(",",
            Q(Timestamp.ToString("yyyy-MM-dd HH:mm:ss")),
            Q(SessionId),
            Q(Question),
            Q(Model),
            Attempt.ToString(),
            Success ? "1" : "0",
            Rows.ToString(),
            Q(ErrorCode ?? ""),
            Q(ErrorMessage ?? ""),
            DurationMsTotal.ToString(),
            DurationMsLlm.ToString(),
            DurationMsSql.ToString(),
            Q(Sql),
            Q(TablesUsed),
            Q(ColumnsUsed),
            CatalogTables.ToString(),
            CatalogColumns.ToString(),
            SystemPromptChars.ToString(),
            UserPromptChars.ToString()
        );
    }
}