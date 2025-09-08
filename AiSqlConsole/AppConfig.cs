namespace AiSqlConsole;

public static class AppConfig
{
    public const string CONN_STRING = "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=123";
    public const string OLLAMA_BASE = "http://localhost:11434";
    public const string MODEL = "mistral";
    public const int LIMIT = 100;
}