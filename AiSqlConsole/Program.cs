using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;

const string CONN = "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=123";
const string OLLAMA = "http://localhost:11434";
const string MODEL = "mistral";
const int LIMIT = 100;

// Limites para manter o prompt enxuto
const int MAX_TABLES = 200; // máximo de tabelas a listar
const int MAX_COLUMNS = 60; // máximo de colunas por tabela
Console.OutputEncoding = Encoding.UTF8;

Console.WriteLine("Pergunta (ex.: listar usuarios ativos com a empresa):");
var question = Console.ReadLine();
if (string.IsNullOrWhiteSpace(question)) return;

await using var conn = new NpgsqlConnection(CONN);
await conn.OpenAsync();

// 0) Descobre o esquema automaticamente (tabelas/colunas + FKs)
var schemaText = await BuildSchemaTextAsync(conn, MAX_TABLES, MAX_COLUMNS);

// 1) Monta prompts
var systemPrompt = BuildSystemPrompt(schemaText, LIMIT);
var userPrompt = BuildUserPrompt(question, LIMIT);

using var http = new HttpClient { BaseAddress = new Uri(OLLAMA) };

// 2) Primeira tentativa de geração/executar SQL

var exampleUser = "Pergunta: 'listar usuarios ativos com cargo e empresa' — Devolva só: '{\"sql\": \"SELECT ...\"}'";
var exampleSql = @"
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
LIMIT " + LIMIT + ";";

var exampleAssistant = JsonSerializer.Serialize(new { sql = exampleSql }, new JsonSerializerOptions { WriteIndented = false });

// Então, em vez de só system+user, envie system + exemplo + user real:
var messages = new[]
{
    new { role = "system", content = systemPrompt },
    new { role = "user", content = exampleUser },
    new { role = "assistant", content = exampleAssistant },
    new { role = "user", content = userPrompt }
};

var sql1 = await GetSqlFromModelAsync(http, messages, MODEL);
sql1 = FixCommonSyntaxIssues(sql1, LIMIT);

Console.WriteLine($"\nSQL (1ª tentativa): {sql1}\n");

var (ok1, err1) = await ExecuteAndPrintAsync(conn, sql1);
if (ok1) Environment.Exit(0);

// 3) Segunda tentativa: pedir correção usando o erro e a SQL que falhou
Console.WriteLine($"\nFalhou a 1ª tentativa: {err1?.Message}\nGerando correção…");

var repairPrompt = BuildRepairUserPrompt(question, sql1, err1?.Message ?? "erro desconhecido", LIMIT);
messages = new[]
{
    new { role = "system", content = systemPrompt },
    new { role = "user", content = repairPrompt }
};
var sql2 = await GetSqlFromModelAsync(http, messages, MODEL);
sql2 = FixCommonSyntaxIssues(sql2, LIMIT);
Console.WriteLine($"\nSQL (2ª tentativa): {sql2}\n");

var (ok2, err2) = await ExecuteAndPrintAsync(conn, sql2);
if (!ok2)
{
    Console.WriteLine("\nAinda falhou. Último erro:");
    Console.WriteLine(err2?.ToString());
    Console.WriteLine("\nEsquema descoberto (resumo):\n" + schemaText);
}

// ----------------- Helpers -----------------
static string BuildSystemPrompt(string schemaText, int limit) => $@"
Você é um gerador de SQL para PostgreSQL. Responda APENAS JSON com a chave ""sql"".
Regras:
- Apenas SELECT (CTE WITH somente se estritamente necessário; prefira um único SELECT com JOINs diretos).
- Nunca deixe vírgula após a última CTE. A sintaxe correta é: WITH cte1 AS (...), cte2 AS (...) SELECT ...
- Evite SELECT *; liste colunas e use aliases (ex.: usuario_email).
- Use SOMENTE tabelas/colunas do esquema abaixo; respeite FKs e tabelas de junção (ex.: usuario_cargo, usuario_empresa).
- Utilize LIMIT {limit}. Sem comentários, markdown ou texto extra.

Esquema disponível:
{schemaText}
";

static string BuildUserPrompt(string question, int limit) => $"""
                                                              Pergunta do usuário: {question}
                                                              Gere uma SQL válida que responda à pergunta, seguindo as regras do sistema (apenas SELECT, usar apenas colunas do esquema, incluir LIMIT {limit}).
                                                              Retorne SOMENTE JSON com a chave "sql".
                                                              """;

static string BuildRepairUserPrompt(string question, string failingSql, string errorMsg, int limit) => $"""
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

static async Task<string> GetSqlFromModelAsync(HttpClient http, object messages, string MODEL)
{
    var body = new { model = MODEL, stream = false, format = "json", options = new { temperature = 0.0 }, messages };

    using var resp = await http.PostAsJsonAsync("/api/chat", body);
    resp.EnsureSuccessStatusCode();
    var raw = await resp.Content.ReadAsStringAsync();

    using var doc = JsonDocument.Parse(raw);
    var content = doc.RootElement.GetProperty("message").GetProperty("content").GetString() ?? "{}";
    content = StripCodeFences(content).Trim();

    // Tenta parsear como JSON e extrair "sql"
    try
    {
        using var json = JsonDocument.Parse(content);
        if (!json.RootElement.TryGetProperty("sql", out var sqlEl))
            throw new Exception("Resposta sem campo 'sql'.");
        var sql = (sqlEl.GetString() ?? "").Trim();

        if (!IsSelect(sql))
            throw new Exception("Apenas SELECT (ou WITH … SELECT).");

        return sql;
    }
    catch (Exception)
    {
        // fallback por regex
        var m = Regex.Match(content, "\"sql\"\\s*:\\s*\"(.*?)\"", RegexOptions.Singleline);
        if (m.Success)
        {
            var sql = Regex.Unescape(m.Groups[1].Value).Trim();
            if (IsSelect(sql)) return sql;
        }

        throw new Exception("Não foi possível extrair uma SQL válida do modelo.");
    }

    static bool IsSelect(string sql)
    {
        var s = sql.TrimStart();
        return s.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
               || s.StartsWith("WITH", StringComparison.OrdinalIgnoreCase);
    }

    static string StripCodeFences(string s)
    {
        var m = Regex.Match(s, "^```[a-zA-Z0-9_-]*\\s*\\n([\\s\\S]*?)\\n```\\s*$", RegexOptions.Multiline);
        return m.Success ? m.Groups[1].Value : s;
    }
}

static async Task<(bool ok, Exception? error)> ExecuteAndPrintAsync(NpgsqlConnection conn, string sql)
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

static async Task<string> BuildSchemaTextAsync(NpgsqlConnection conn, int maxTables, int maxColumns)
{
    // Tabelas + colunas (todos schemas exceto de sistema)
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

            if (cols.Count < maxColumns)
                cols.Add(col);
        }
    }

    // FKs
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
            fks.Add(childS + "." + childT + "(" + childC + ") -> "
                    + parentS + "." + parentT + "(" + parentC + ")");
        }
    }

    var sb = new StringBuilder();
    sb.AppendLine("Tabelas/colunas:");
    foreach (var kv in tables.OrderBy(k => k.Key).Take(maxTables))
    {
        sb.Append("- ").Append(kv.Key).Append('(').Append(string.Join(", ", kv.Value)).AppendLine(")");
    }

    if (fks.Count > 0)
    {
        sb.AppendLine("Relações (FK):");
        foreach (var fk in fks.Take(500))
            sb.AppendLine("- " + fk);
    }

    return sb.ToString();
}

static string FixCommonSyntaxIssues(string sql, int limit)
{
    if (string.IsNullOrWhiteSpace(sql)) return sql;

    // tira code fences por segurança (já faz no GetSqlFromModelAsync, mas reforça)
    sql = Regex.Replace(sql, @"^\s*```(?:sql)?\s*([\s\S]*?)\s*```\s*$", "$1", RegexOptions.IgnoreCase);

    // remove ; final
    sql = Regex.Replace(sql, @";\s*$", "", RegexOptions.Multiline);

    // CORREÇÃO PRINCIPAL: vírgula indevida antes do SELECT após a lista de CTEs
    // transforma "WITH cte1 AS (...), cte2 AS (...), SELECT ..." em "WITH cte1 AS (...), cte2 AS (...) SELECT ..."
    sql = Regex.Replace(sql, @"\)\s*,\s*SELECT", ") SELECT", RegexOptions.IgnoreCase);

    // também cobre o caso com quebras de linha
    sql = Regex.Replace(sql, @"\)\s*,\s*\n\s*SELECT", ") SELECT", RegexOptions.IgnoreCase);

    // Evita WITH vazio: "WITH SELECT" -> "SELECT"
    sql = Regex.Replace(sql, @"^\s*WITH\s*,\s*SELECT", "SELECT", RegexOptions.IgnoreCase);

    // Garante que seja SELECT/WITH
    var s = sql.TrimStart();
    if (!(s.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) ||
          s.StartsWith("WITH", StringComparison.OrdinalIgnoreCase)))
        throw new Exception("Apenas SELECT/CTE são permitidos.");

    // Garante LIMIT se não houver (simples; não trata FETCH FIRST)
    if (!Regex.IsMatch(sql, @"\bLIMIT\b", RegexOptions.IgnoreCase))
        sql += $" LIMIT {limit}";

    return sql.Trim();
}