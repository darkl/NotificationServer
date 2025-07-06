namespace GNS.Async;

/// <summary>
/// Logging interface for the framework
/// </summary>
public interface ILog
{
    void Info(string message);
    void Info(string message, params object[] args);
    void Warn(string message);
    void Warn(string message, params object[] args);
    void Error(string message);
    void Error(string message, params object[] args);
    void Error(Exception exception, string message);
    void Error(Exception exception, string message, params object[] args);
}