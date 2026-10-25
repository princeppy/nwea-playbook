// Console + runs/<timestamp>/run.log output. Every line is masked before it is written.

namespace PlaybookRunner;

public interface IRunLog
{
    string File { get; }
    void Info(string message, int depth = 0);
    void Error(string message);
}

public sealed class RunLog(string file, Func<string, string> mask) : IRunLog
{
    public string File { get; } = file;

    public void Info(string message, int depth = 0) => Write(new string(' ', depth * 2) + message, Console.Out);

    public void Error(string message) => Write(message, Console.Error);

    private void Write(string line, TextWriter stream)
    {
        var masked = mask(line);
        stream.WriteLine(masked);
        try
        {
            System.IO.File.AppendAllText(File, masked + "\n");
        }
        catch (IOException e)
        {
            Console.Error.WriteLine($"(could not write {File}: {e.Message})");
        }
    }

    /// <summary>First line of an error message, for compact logs.</summary>
    public static string ErrorLine(Exception e) => e.Message.Split('\n')[0].TrimEnd('\r');
}
