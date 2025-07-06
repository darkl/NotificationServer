namespace GNS.Async;

/// <summary>
/// Default console-based logging implementation
/// </summary>
public class ConsoleLog : ILog
{
    private readonly string _componentName;

    public ConsoleLog(string componentName = null)
    {
        _componentName = componentName ?? "Unknown";
    }

    public void Info(string message)
    {
        Console.WriteLine($"[INFO] [{_componentName}] {message}");
    }

    public void Info(string message, params object[] args)
    {
        Console.WriteLine($"[INFO] [{_componentName}] {string.Format(message, args)}");
    }

    public void Warn(string message)
    {
        Console.WriteLine($"[WARN] [{_componentName}] {message}");
    }

    public void Warn(string message, params object[] args)
    {
        Console.WriteLine($"[WARN] [{_componentName}] {string.Format(message, args)}");
    }

    public void Error(string message)
    {
        Console.WriteLine($"[ERROR] [{_componentName}] {message}");
    }

    public void Error(string message, params object[] args)
    {
        Console.WriteLine($"[ERROR] [{_componentName}] {string.Format(message, args)}");
    }

    public void Error(Exception exception, string message)
    {
        Console.WriteLine($"[ERROR] [{_componentName}] {message}: {exception}");
    }

    public void Error(Exception exception, string message, params object[] args)
    {
        Console.WriteLine($"[ERROR] [{_componentName}] {string.Format(message, args)}: {exception}");
    }
}