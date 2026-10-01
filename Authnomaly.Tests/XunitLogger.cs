using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Authnomaly.Tests;

// Routes ILogger output straight into the test's own _output, interleaved with everything else the
// test writes there. Built directly (no host/DI involved) since production code is constructed
// manually in these tests, not resolved from a container.
public class XunitLogger : ILogger
{
    private readonly ITestOutputHelper _output;
    private readonly string _categoryName;

    public XunitLogger(ITestOutputHelper output, string categoryName)
    {
        _output = output;
        _categoryName = categoryName;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        try
        {
            _output.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{logLevel}] {_categoryName}: {formatter(state, exception)}");
            if(exception is not null)_output.WriteLine(exception.ToString());
        }
        catch (InvalidOperationException)
        {
            // the test already finished (e.g. a log call from a background task after completion) - nothing to write to
        }
    }
}

public sealed class XunitLogger<T> : XunitLogger, ILogger<T>
{
    public XunitLogger(ITestOutputHelper output) : base(output, typeof(T).Name)
    {
    }
}
