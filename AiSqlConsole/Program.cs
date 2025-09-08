using System.Diagnostics;
using System.Text;
using AiSqlConsole.Domain;
using AiSqlConsole.Services;
using AiSqlConsole.Telemetry;
using Npgsql;

namespace AiSqlConsole;

internal class Program
{
    private const int MAX_TABLES = 200;
    private const int MAX_COLUMNS = 60;

    public static async Task Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        Console.WriteLine("Pergunta (ex.: listar usuarios ativos com a empresa):");
        var question = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(question)) return;

        await using var conn = new NpgsqlConnection(AppConfig.CONN_STRING);
        await conn.OpenAsync();

        var schemaText = await SchemaDiscovery.BuildSchemaTextAsync(conn, MAX_TABLES, MAX_COLUMNS);
        var systemPrompt = PromptFactory.BuildSystemPrompt(schemaText, AppConfig.LIMIT);
        var userPrompt = PromptFactory.BuildUserPrompt(question, AppConfig.LIMIT);

        var exampleUser = "Pergunta: 'listar usuarios ativos com cargo e empresa' — Devolva só: '{\"sql\": \"SELECT ...\"}'";
        var exampleSql = $@"
SELECT
  u.id, u.email, u.nome,
  COALESCE(string_agg(DISTINCT c.nome, ', '), '-') AS cargos,
  COALESCE(string_agg(DISTINCT e.razao_social, ', '), '-') AS empresas
FROM public.usuario u
LEFT JOIN public.usuario_cargo   uc ON uc.id_usuario  = u.id
LEFT JOIN public.cargo           c  ON c.id           = uc.id_cargo
LEFT JOIN public.usuario_empresa ue ON ue.id_usuario  = u.id
LEFT JOIN public.empresa         e  ON e.id           = ue.id_empresa
WHERE u.ativo = true
GROUP BY u.id, u.email, u.nome
ORDER BY u.nome
LIMIT {AppConfig.LIMIT};";
        var exampleAssistant = PromptFactory.WrapSqlAsJson(exampleSql);

        var messages = new[]
        {
            new ChatMessage("system", systemPrompt),
            new ChatMessage("user", exampleUser),
            new ChatMessage("assistant", exampleAssistant),
            new ChatMessage("user", userPrompt)
        };

        using var llm = new OllamaChatClient(AppConfig.OLLAMA_BASE, AppConfig.MODEL);
        var logger = new CsvMetricsLogger(Environment.GetEnvironmentVariable("AI_SQL_CSV_PATH"));

        // ===== Tentativa 1 =====
        var m1 = RunMetrics.Start(question, AppConfig.MODEL, attempt: 1);
        m1.SystemPromptChars = systemPrompt.Length;
        m1.UserPromptChars = userPrompt.Length;
        m1.CatalogTables = 0;
        m1.CatalogColumns = 0;

        var swTotal = Stopwatch.StartNew();
        var swLlm = Stopwatch.StartNew();
        var sql1 = await llm.GetSqlFromModelAsync(messages);
        swLlm.Stop();

        m1.DurationMsLlm = swLlm.ElapsedMilliseconds;
        sql1 = SqlPostProcessor.FixCommonSyntaxIssues(sql1, AppConfig.LIMIT);

        ConsolePrinter.SqlAttempt(1, sql1);
        m1.Sql = sql1;

        var swSql = Stopwatch.StartNew();
        var (ok1, err1, rows1) = await SqlRunner.ExecuteAndPrintWithCountAsync(conn, sql1);
        swSql.Stop();

        m1.DurationMsSql = swSql.ElapsedMilliseconds;
        m1.DurationMsTotal = swTotal.ElapsedMilliseconds;
        m1.Success = ok1;
        m1.Rows = rows1;

        if (!ok1 && err1 is Npgsql.PostgresException pg1)
        {
            m1.ErrorCode = pg1.SqlState;
            m1.ErrorMessage = pg1.MessageText;
        }

        logger.Append(m1);

        if (ok1) return;

        // ===== Tentativa 2 (REPAIR) =====
        ConsolePrinter.FirstFail(err1);
        var repairPrompt = PromptFactory.BuildRepairUserPrompt(
            question, sql1, err1?.Message ?? "erro desconhecido", AppConfig.LIMIT
        );

        messages = new[] { new ChatMessage("system", systemPrompt), new ChatMessage("user", repairPrompt) };

        var m2 = RunMetrics.Start(question, AppConfig.MODEL, attempt: 2);
        m2.SystemPromptChars = systemPrompt.Length;
        m2.UserPromptChars = userPrompt.Length;
        m2.CatalogTables = 0;
        m2.CatalogColumns = 0;

        swTotal.Restart();
        swLlm.Restart();
        var sql2 = await llm.GetSqlFromModelAsync(messages);
        swLlm.Stop();

        m2.DurationMsLlm = swLlm.ElapsedMilliseconds;
        sql2 = SqlPostProcessor.FixCommonSyntaxIssues(sql2, AppConfig.LIMIT);

        ConsolePrinter.SqlAttempt(2, sql2);
        m2.Sql = sql2;

        swSql.Restart();
        var (ok2, err2, rows2) = await SqlRunner.ExecuteAndPrintWithCountAsync(conn, sql2);
        swSql.Stop();

        m2.DurationMsSql = swSql.ElapsedMilliseconds;
        m2.DurationMsTotal = swTotal.ElapsedMilliseconds;
        m2.Success = ok2;
        m2.Rows = rows2;

        if (!ok2 && err2 is Npgsql.PostgresException pg2)
        {
            m2.ErrorCode = pg2.SqlState;
            m2.ErrorMessage = pg2.MessageText;
        }

        logger.Append(m2);

        if (!ok2)
        {
            ConsolePrinter.FinalFail(err2, schemaText);
        }
    }
}