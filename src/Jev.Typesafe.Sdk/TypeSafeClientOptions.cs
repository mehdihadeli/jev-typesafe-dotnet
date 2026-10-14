namespace Jev.Typesafe.Sdk;

public sealed class TypeSafeClientOptions
{
    private string? _model;

    public string? ApiKey { get; init; }
    public string? BaseUrl { get; init; }
    public string? Model { get => _model; init => _model = value; }
    public string DefaultModel { get => _model ?? "jev-latest"; init => _model = value; }
    internal string? ConfiguredModel => _model;
    public TimeSpan? Timeout { get; init; }
    public IReadOnlyDictionary<string, string>? Headers { get; init; }
    public RetryPolicy? Retry { get; init; }
    public HttpMessageHandler? Handler { get; init; }
    public HttpClient? HttpClient { get; init; }
    public bool LeaveOpen { get; init; }
    public TypeSafeLogLevel? LogLevel { get; init; }
    public Action<TypeSafeLogEntry>? Logger { get; init; }
    public Microsoft.Extensions.Logging.ILoggerFactory? LoggerFactory { get; init; }
    public int MaxRetries { get; init; } = 2;
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(0.5);
    internal Func<TimeSpan, CancellationToken, Task> DelayAsync { get; init; } = Task.Delay;
    internal Func<double> Clock { get; init; } = () => (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency;
}