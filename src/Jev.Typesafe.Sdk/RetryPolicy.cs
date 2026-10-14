using System.Globalization;

namespace Jev.Typesafe.Sdk;

public sealed class RetryPolicy
{
    public int MaxRetries { get; init; } = 2;
    public TimeSpan BackoffInitial { get; init; } = TimeSpan.FromSeconds(0.5);
    public TimeSpan BackoffMax { get; init; } = TimeSpan.FromSeconds(5);
    public double BackoffJitter { get; init; } = 0.25;
    public IReadOnlyCollection<int> HttpStatuses { get; init; } = Array.AsReadOnly(new[] { 408, 429 }.Concat(Enumerable.Range(500, 100)).ToArray());
    public bool RespectRetryAfter { get; init; } = true;
    public bool ApiConnectionError { get; init; } = true;
    public bool ApiTimeoutError { get; init; } = true;
    public IReadOnlyCollection<Type> Exceptions { get; init; } = Array.Empty<Type>();
    public Func<Exception, bool>? Predicate { get; init; }
    public TimeSpan? Timeout { get; init; } = TimeSpan.FromSeconds(30);

    internal void Validate()
    {
        if (MaxRetries < 0) throw new TypeSafeException("max_retries must be a non-negative integer.");
        if (BackoffInitial < TimeSpan.Zero) throw new TypeSafeException("backoff_initial must be a non-negative, finite number of seconds.");
        if (BackoffMax < TimeSpan.Zero) throw new TypeSafeException("backoff_max must be a non-negative, finite number of seconds.");
        if (double.IsNaN(BackoffJitter) || BackoffJitter < 0 || BackoffJitter > 1) throw new TypeSafeException("backoff_jitter must be between zero and one.");
        if (Timeout <= TimeSpan.Zero) throw new TypeSafeException("timeout must be a positive, finite number of seconds.");
        if (HttpStatuses is null || Exceptions is null) throw new ArgumentException("Retry collections cannot be null.");
    }

    public bool ShouldRetry(Exception error)
    {
        if (error is TypeSafeApiTimeoutException && ApiTimeoutError) return true;
        if (error is TypeSafeApiConnectionException and not TypeSafeApiTimeoutException && ApiConnectionError) return true;
        if (error is TypeSafeApiException api && HttpStatuses.Contains(api.Status)) return true;
        return Exceptions.Any(type => type.IsInstanceOfType(error)) || Predicate?.Invoke(error) == true;
    }

    public TimeSpan GetDelay(int failedAttempt, Exception error, double randomValue)
    {
        if (RespectRetryAfter && error is TypeSafeApiException { RetryAfter: { } serverDelay }) return serverDelay;
        var seconds = Math.Min(BackoffMax.TotalSeconds, BackoffInitial.TotalSeconds * Math.Pow(2, Math.Min(failedAttempt - 1, 1023)));
        var jittered = Math.Round(seconds * (1 - randomValue * BackoffJitter), 3, MidpointRounding.ToEven);
        return TimeSpan.FromSeconds(Math.Min(seconds, jittered));
    }
}

internal static class RetryAfterParser
{
    internal static TimeSpan? Parse(IReadOnlyDictionary<string, string> headers)
    {
        foreach (var name in new[] { "retry-after-ms", "retry-after" })
        {
            if (!headers.TryGetValue(name, out var raw)) continue;
            var text = raw.Trim();
            if (double.TryParse(text.Length == 0 ? "0" : text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                var seconds = name == "retry-after-ms" ? number / 1000 : number;
                if (!double.IsNaN(seconds) && !double.IsInfinity(seconds) && seconds >= 0 && seconds < TimeSpan.MaxValue.TotalSeconds) return TimeSpan.FromSeconds(seconds);
                if (number < 0 && name == "retry-after") return null;
            }
            else if (name == "retry-after" && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
            {
                var delta = date - DateTimeOffset.UtcNow;
                return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
            }
        }
        return null;
    }
}