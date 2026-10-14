using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;

namespace Jev.Typesafe.Sdk;

public static class TypeSafeDiagnostics
{
    public const string ActivitySourceName = "Jev.Typesafe.Sdk";
    public const string MeterName = "Jev.Typesafe.Sdk";
    public const string OperationDurationName = "jev.typesafe.sdk.operation.duration";
    public const string CompletedOperationsName = "jev.typesafe.sdk.operation.completed";
    public const string RetryAttemptsName = "jev.typesafe.sdk.retry.attempts";
    public const string TokenUsageName = "jev.typesafe.sdk.token.usage";

    internal static ActivitySource Source { get; } = new(
        ActivitySourceName, typeof(TypeSafeClient).Assembly.GetName().Version?.ToString()
    );

    private static readonly Meter Meter = new(MeterName, typeof(TypeSafeClient).Assembly.GetName().Version?.ToString());
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(OperationDurationName, "s", "Elapsed time for an SDK operation, including retry waits.");
    private static readonly Counter<long> Completed = Meter.CreateCounter<long>(CompletedOperationsName, "{operation}", "Finished SDK operations grouped by outcome.");
    private static readonly Counter<long> Retries = Meter.CreateCounter<long>(RetryAttemptsName, "{attempt}", "Additional SDK attempts that actually started.");
    private static readonly Histogram<long> Tokens = Meter.CreateHistogram<long>(TokenUsageName, "{token}", "Reported tokens per successful classification, separated by direction.");

    internal static long? StartMeasurement() => Duration.Enabled ? Stopwatch.GetTimestamp() : null;

    internal static void RecordRetry(string operation)
    {
        if (Retries.Enabled) Retries.Add(1, new KeyValuePair<string, object?>("typesafe.operation.name", operation));
    }

    internal static void RecordCompletion(string operation, int? status, Exception? failure, bool cancelled, long? started)
    {
        if (!Completed.Enabled && (!Duration.Enabled || started is null)) return;
        var tags = new TagList
        {
            { "typesafe.operation.name", operation },
            { "typesafe.outcome", cancelled ? "cancelled" : failure is null ? "success" : "error" },
        };
        if (status is { } code) tags.Add("http.response.status_code", code);
        if (failure is not null) tags.Add("error.type", cancelled ? typeof(OperationCanceledException).FullName : failure.GetType().FullName);
        if (Completed.Enabled) Completed.Add(1, tags);
        if (Duration.Enabled && started is { } timestamp)
            Duration.Record((Stopwatch.GetTimestamp() - timestamp) / (double)Stopwatch.Frequency, tags);
    }

    internal static void RecordResponse(Activity? activity, string operation, RawHttpResponse response)
    {
        if (operation != "systemone") return;
        if (activity is null && !Tokens.Enabled) return;
        if (response.Body is not JsonElement { ValueKind: JsonValueKind.Object } body) return;
        if (body.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.String)
            activity?.SetTag("gen_ai.response.model", model.GetString());
        if (!body.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object) return;
        RecordTokens(activity, operation, usage, "input_tokens", "input");
        RecordTokens(activity, operation, usage, "output_tokens", "output");
    }

    private static void RecordTokens(Activity? activity, string operation, JsonElement usage, string property, string direction)
    {
        if (!usage.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt64(out var count) || count < 0) return;
        activity?.SetTag("gen_ai.usage." + property, count);
        if (Tokens.Enabled)
            Tokens.Record(count, new TagList
            {
                { "typesafe.operation.name", operation },
                { "gen_ai.token.type", direction },
            });
    }
}