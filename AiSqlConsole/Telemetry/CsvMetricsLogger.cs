using System.Text;

namespace AiSqlConsole.Telemetry;

public sealed class CsvMetricsLogger
{
    private readonly string _path;
    private readonly object _lock = new();

    public CsvMetricsLogger(string? path = null)
    {
        _path = string.IsNullOrWhiteSpace(path)
            ? Path.Combine(AppContext.BaseDirectory, "ai_sql_metrics.csv")
            : path!;
        EnsureHeader();
    }

    private void EnsureHeader()
    {
        if (!File.Exists(_path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, RunMetrics.CsvHeader + Environment.NewLine, Encoding.UTF8);
        }
    }

    public void Append(RunMetrics m)
    {
        lock (_lock)
        {
            File.AppendAllText(_path, m.ToCsvLine() + Environment.NewLine, Encoding.UTF8);
        }
    }
}