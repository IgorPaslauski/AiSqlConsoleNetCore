namespace AiSqlConsole.Services;


public static class ConsolePrinter
{
    public static void SqlAttempt(int attempt, string sql)
        => Console.WriteLine($"\nSQL ({attempt}ª tentativa): {sql}\n");

    public static void FirstFail(Exception? ex)
    {
        Console.WriteLine($"\nFalhou a 1ª tentativa: {ex?.Message}\nGerando correção…");
    }

    public static void FinalFail(Exception? ex, string schemaText)
    {
        Console.WriteLine("\nAinda falhou. Último erro:");
        Console.WriteLine(ex?.ToString());
        Console.WriteLine("\nEsquema descoberto (resumo):\n" + schemaText);
    }
}