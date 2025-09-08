using System.Text.Json;

namespace AiSqlConsole.Services;

public static class PromptFactory
{
    public static string BuildSystemPrompt(string schemaText, int limit) => $@"
Você é um gerador de SQL para PostgreSQL. Responda APENAS JSON com a chave ""sql"".
Regras:
- Apenas SELECT (CTE WITH somente se estritamente necessário; prefira um único SELECT com JOINs diretos).
- Nunca deixe vírgula após a última CTE. A sintaxe correta é: WITH cte1 AS (...), cte2 AS (...) SELECT ...
- Evite SELECT *; liste colunas e use aliases (ex.: usuario_email, kanban.id as table_kanban_id).
- Use SOMENTE tabelas/colunas do esquema abaixo; respeite FKs e tabelas de junção (ex.: usuario_cargo, usuario_empresa).
- Utilize LIMIT {limit}. Sem comentários, markdown ou texto extra.
- Use SOMENTE colunas exatas dessa lista.
Esquema disponível:
{schemaText}
";

    public static string BuildUserPrompt(string question, int limit) => $"""
                                                                         Pergunta do usuário: {question}
                                                                         Gere uma SQL válida que responda à pergunta, seguindo as regras do sistema (apenas SELECT, usar apenas colunas do esquema, incluir LIMIT {limit}).
                                                                         Retorne SOMENTE JSON com a chave "sql".
                                                                         """;

    public static string BuildRepairUserPrompt(string question, string failingSql, string errorMsg, int limit) => $"""
                                                                                                                   A consulta anterior falhou ao executar no PostgreSQL.
                                                                                                                   Pergunta original: {question}
                                                                                                                   SQL que falhou:
                                                                                                                   {failingSql}
                                                                                                                   Erro do PostgreSQL:
                                                                                                                   {errorMsg}
                                                                                                                   Gere UMA NOVA SQL corrigida que responda à pergunta, usando apenas o esquema informado no sistema.
                                                                                                                   Regras: apenas SELECT (CTE WITH permitido), evite SELECT *, use aliases para colunas, inclua LIMIT {limit}.
                                                                                                                   Retorne SOMENTE JSON com a chave "sql".
                                                                                                                   """;

    public static string WrapSqlAsJson(string sql) => JsonSerializer.Serialize(new { sql }, new JsonSerializerOptions { WriteIndented = false });
}