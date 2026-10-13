namespace Typesafe.Sdk;

public sealed class TypeSafeClientOptions
{
    public string? ApiKey { get; init; }
    public string? BaseUrl { get; init; }
    public string DefaultModel { get; init; } = "jev-latest";
    public int MaxRetries { get; init; } = 2;
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromMilliseconds(250);
}