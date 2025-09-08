using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using AiSqlConsole.Domain;

namespace AiSqlConsole.Services;

public sealed class OllamaChatClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _model;

    public OllamaChatClient(string baseAddress, string model)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseAddress) };
        _model = model;
    }

    public async Task<string> GetSqlFromModelAsync(IEnumerable<ChatMessage> messages)
    {
        var body = new
        {
            model = _model,
            stream = false,
            format = "json",
            options = new { temperature = 0.0 },
            messages = messages.Select(m => new { m.role, m.content })
        };

        using var resp = await _http.PostAsJsonAsync("/api/chat", body);
        resp.EnsureSuccessStatusCode();
        var raw = await resp.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(raw);
        var content = doc.RootElement.GetProperty("message").GetProperty("content").GetString() ?? "{}";
        content = StripCodeFences(content).Trim();

        try
        {
            using var json = JsonDocument.Parse(content);
            if (!json.RootElement.TryGetProperty("sql", out var sqlEl))
                throw new Exception("Resposta sem campo 'sql'.");

            var sql = (sqlEl.GetString() ?? "").Trim();
            if (!IsSelectOrWith(sql)) throw new Exception("Apenas SELECT/CTE são permitidos.");
            return sql;
        }
        catch
        {
            var m = Regex.Match(content, "\"sql\"\\s*:\\s*\"(.*?)\"", RegexOptions.Singleline);
            if (m.Success)
            {
                var sql = Regex.Unescape(m.Groups[1].Value).Trim();
                if (IsSelectOrWith(sql)) return sql;
            }

            throw new Exception("Não foi possível extrair uma SQL válida do modelo.");
        }
    }

    private static bool IsSelectOrWith(string sql)
    {
        var s = sql.TrimStart();
        return s.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) ||
               s.StartsWith("WITH", StringComparison.OrdinalIgnoreCase);
    }

    private static string StripCodeFences(string s)
    {
        var m = Regex.Match(s, "^```[a-zA-Z0-9_-]*\\s*\\n([\\s\\S]*?)\\n```\\s*$", RegexOptions.Multiline);
        return m.Success ? m.Groups[1].Value : s;
    }

    public void Dispose() => _http.Dispose();
}