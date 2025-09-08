using System.Text;
using AiSqlConsole.Domain;
using AiSqlConsole.Services;
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

        var sql1 = await llm.GetSqlFromModelAsync(messages);
        sql1 = SqlPostProcessor.FixCommonSyntaxIssues(sql1, AppConfig.LIMIT);

        ConsolePrinter.SqlAttempt(1, sql1);

        var (ok1, err1) = await SqlRunner.ExecuteAndPrintAsync(conn, sql1);
        if (ok1) return;

        ConsolePrinter.FirstFail(err1);
        var repairPrompt = PromptFactory.BuildRepairUserPrompt(
            question, sql1, err1?.Message ?? "erro desconhecido", AppConfig.LIMIT
        );

        messages = new[] { new ChatMessage("system", systemPrompt), new ChatMessage("user", repairPrompt) };

        var sql2 = await llm.GetSqlFromModelAsync(messages);
        sql2 = SqlPostProcessor.FixCommonSyntaxIssues(sql2, AppConfig.LIMIT);

        ConsolePrinter.SqlAttempt(2, sql2);

        var (ok2, err2) = await SqlRunner.ExecuteAndPrintAsync(conn, sql2);
        if (!ok2)
        {
            ConsolePrinter.FinalFail(err2, schemaText);
        }
    }
}