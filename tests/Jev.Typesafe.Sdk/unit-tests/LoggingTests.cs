using System.Text.Json;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Jev.Typesafe.Sdk;

namespace Jev.Typesafe.Sdk.UnitTests;

[Collection("Process environment")]
public sealed class LoggingTests
{
    [Fact]
    public async Task OpenTelemetry_MetricsCaptureSuccessfulRetriedCallWithoutPayloads()
    {
        var counts = new List<(string Name, long Value, Dictionary<string, object?> Tags)>();
        var durations = new List<(double Value, Dictionary<string, object?> Tags)>();
        using var listener = CaptureMetrics(counts, durations);
        var handler = new ParityHandler((index, _, _) => Task.FromResult(index == 0
            ? ParityTestHelper.Response("{\"message\":\"payload-secret\"}", 429)
            : ParityTestHelper.Response()));
        using var client = new TypeSafeClient(new TypeSafeClientOptions
        {
            ApiKey = "credential-secret", Handler = handler, Model = "request-model",
            Retry = new RetryPolicy { MaxRetries = 1, BackoffInitial = TimeSpan.Zero },
            LogLevel = TypeSafeLogLevel.Off,
        });
        await client.SystemOneAsync("payload-secret", ParityTestHelper.Questions(),
            cancellationToken: TestContext.Current.CancellationToken);

        counts.Single(item => item.Name == TypeSafeDiagnostics.CompletedOperationsName).Value.ShouldBe(1);
        counts.Single(item => item.Name == TypeSafeDiagnostics.RetryAttemptsName).Value.ShouldBe(1);
        var tokens = counts.Where(item => item.Name == TypeSafeDiagnostics.TokenUsageName).ToArray();
        tokens.Length.ShouldBe(2);
        tokens.Single(item => Equals(item.Tags["gen_ai.token.type"], "input")).Value.ShouldBe(10);
        tokens.Single(item => Equals(item.Tags["gen_ai.token.type"], "output")).Value.ShouldBe(8);
        var duration = durations.Single();
        duration.Value.ShouldBeGreaterThanOrEqualTo(0);
        duration.Tags["typesafe.operation.name"].ShouldBe("systemone");
        duration.Tags["typesafe.outcome"].ShouldBe("success");
        duration.Tags["http.response.status_code"].ShouldBe(200);
        var exported = string.Join(" ", counts.SelectMany(item => item.Tags).Concat(duration.Tags).Select(pair => pair.Value));
        exported.ShouldNotContain("credential-secret");
        exported.ShouldNotContain("payload-secret");
        exported.ShouldNotContain("request-model");
        exported.ShouldNotContain("jev-test");
    }

    [Fact]
    public async Task OpenTelemetry_MeterProviderExportsCountersAndHistograms()
    {
        var exported = new List<Metric>();
        using var provider = OpenTelemetry.Sdk.CreateMeterProviderBuilder()
            .AddMeter(TypeSafeDiagnostics.MeterName).AddInMemoryExporter(exported).Build();
        using var client = ParityTestHelper.Client(ParityTestHelper.Handler());
        await client.SystemOneAsync("state", ParityTestHelper.Questions(),
            cancellationToken: TestContext.Current.CancellationToken);
        provider.ForceFlush().ShouldBeTrue();
        exported.Select(metric => metric.Name).Order().ShouldBe(new[]
        {
            TypeSafeDiagnostics.CompletedOperationsName, TypeSafeDiagnostics.OperationDurationName,
            TypeSafeDiagnostics.TokenUsageName,
        }.Order());
        var completed = exported.Single(metric => metric.Name == TypeSafeDiagnostics.CompletedOperationsName);
        foreach (ref readonly var point in completed.GetMetricPoints()) point.GetSumLong().ShouldBe(1L);
        exported.Single(metric => metric.Name == TypeSafeDiagnostics.OperationDurationName).Unit.ShouldBe("s");
        var tokens = exported.Single(metric => metric.Name == TypeSafeDiagnostics.TokenUsageName);
        tokens.MetricType.ShouldBe(MetricType.Histogram);
        tokens.Unit.ShouldBe("{token}");
        var total = 0.0;
        var samples = 0L;
        foreach (ref readonly var point in tokens.GetMetricPoints())
        {
            total += point.GetHistogramSum();
            samples += (long)point.GetHistogramCount();
        }
        total.ShouldBe(18);
        samples.ShouldBe(2);
    }

    [Theory]
    [InlineData("http", 503)]
    [InlineData("validation", 200)]
    [InlineData("connection", null)]
    [InlineData("timeout", null)]
    public async Task OpenTelemetry_FailedCallsCountOnceWithoutTokenMeasurements(string mode, int? status)
    {
        var counts = new List<(string Name, long Value, Dictionary<string, object?> Tags)>();
        var durations = new List<(double Value, Dictionary<string, object?> Tags)>();
        using var listener = CaptureMetrics(counts, durations);
        var handler = new ParityHandler((_, _, _) => mode switch
        {
            "connection" => throw new HttpRequestException("payload-secret"),
            "timeout" => throw new TaskCanceledException("payload-secret"),
            _ => Task.FromResult(ParityTestHelper.Response(mode == "validation" ? "{}" : "{\"message\":\"payload-secret\"}", status!.Value)),
        });
        using var client = ParityTestHelper.Client(handler);
        var failure = await Should.ThrowAsync<TypeSafeException>(() => client.SystemOneAsync("payload-secret", ParityTestHelper.Questions(),
            cancellationToken: TestContext.Current.CancellationToken));
        var completed = counts.Single();
        completed.Name.ShouldBe(TypeSafeDiagnostics.CompletedOperationsName);
        completed.Value.ShouldBe(1);
        completed.Tags["typesafe.outcome"].ShouldBe("error");
        completed.Tags["error.type"].ShouldBe(failure.GetType().FullName);
        completed.Tags.GetValueOrDefault("http.response.status_code").ShouldBe(status);
        var duration = durations.Single();
        duration.Tags.ShouldBe(completed.Tags);
        string.Join(" ", duration.Tags.Values).ShouldNotContain("payload-secret");
        handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task OpenTelemetry_CancelledBackoffDoesNotCountUnstartedRetry()
    {
        var counts = new List<(string Name, long Value, Dictionary<string, object?> Tags)>();
        using var listener = CaptureMetrics(counts);
        var spans = new List<Activity>();
        using var tracing = OpenTelemetry.Sdk.CreateTracerProviderBuilder().AddSource(TypeSafeDiagnostics.ActivitySourceName)
            .SetSampler(new AlwaysOnSampler()).AddInMemoryExporter(spans).Build();
        using var cancellation = new CancellationTokenSource();
        var handler = new ParityHandler((_, _, _) => Task.FromResult(ParityTestHelper.Response("{}", 429)));
        using var client = new TypeSafeClient(new TypeSafeClientOptions
        {
            ApiKey = "test", Handler = handler, LogLevel = TypeSafeLogLevel.Off,
            Retry = new RetryPolicy { MaxRetries = 1, BackoffInitial = TimeSpan.Zero },
            DelayAsync = (_, token) => { cancellation.Cancel(); return Task.FromCanceled(token); },
        });
        await Should.ThrowAsync<OperationCanceledException>(() => client.Models.ListAsync(cancellationToken: cancellation.Token));
        var completed = counts.Single();
        completed.Name.ShouldBe(TypeSafeDiagnostics.CompletedOperationsName);
        completed.Tags["typesafe.operation.name"].ShouldBe("models.list");
        completed.Tags["typesafe.outcome"].ShouldBe("cancelled");
        completed.Tags["error.type"].ShouldBe(typeof(OperationCanceledException).FullName);
        handler.Requests.Count.ShouldBe(1);
        var span = spans.Single();
        span.Status.ShouldBe(ActivityStatusCode.Error);
        span.StatusDescription.ShouldBeNull();
        span.GetTagItem("http.request.resend_count").ShouldBe(0);
    }

    [Fact]
    public async Task OpenTelemetry_DurationIncludesRetryWait()
    {
        var counts = new List<(string Name, long Value, Dictionary<string, object?> Tags)>();
        var durations = new List<(double Value, Dictionary<string, object?> Tags)>();
        using var listener = CaptureMetrics(counts, durations);
        var waited = 0.0;
        var handler = new ParityHandler((index, _, _) => Task.FromResult(index == 0
            ? ParityTestHelper.Response("{}", 429) : ParityTestHelper.Response()));
        using var client = new TypeSafeClient(new TypeSafeClientOptions
        {
            ApiKey = "test", Handler = handler, LogLevel = TypeSafeLogLevel.Off,
            Retry = new RetryPolicy { MaxRetries = 1, BackoffInitial = TimeSpan.Zero },
            DelayAsync = async (_, token) =>
            {
                var start = Stopwatch.GetTimestamp();
                await Task.Delay(TimeSpan.FromMilliseconds(20), token);
                waited = (Stopwatch.GetTimestamp() - start) / (double)Stopwatch.Frequency;
            },
        });
        await client.SystemOneAsync("state", ParityTestHelper.Questions(), cancellationToken: TestContext.Current.CancellationToken);
        waited.ShouldBeGreaterThan(0);
        durations.Single().Value.ShouldBeGreaterThanOrEqualTo(waited);
        handler.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task OpenTelemetry_TransportFailureAfterRetryClearsPreviousStatus()
    {
        var counts = new List<(string Name, long Value, Dictionary<string, object?> Tags)>();
        using var listener = CaptureMetrics(counts);
        var spans = new List<Activity>();
        using var tracing = OpenTelemetry.Sdk.CreateTracerProviderBuilder().AddSource(TypeSafeDiagnostics.ActivitySourceName)
            .SetSampler(new AlwaysOnSampler()).AddInMemoryExporter(spans).Build();
        var handler = new ParityHandler((index, _, _) => index == 0
            ? Task.FromResult(ParityTestHelper.Response("{}", 429)) : throw new HttpRequestException("payload-secret"));
        using var client = new TypeSafeClient(new TypeSafeClientOptions
        {
            ApiKey = "test", Handler = handler, LogLevel = TypeSafeLogLevel.Off,
            Retry = new RetryPolicy { MaxRetries = 1, BackoffInitial = TimeSpan.Zero },
        });
        await Should.ThrowAsync<TypeSafeApiConnectionException>(() => client.Models.ListAsync(cancellationToken: TestContext.Current.CancellationToken));
        var completed = counts.Single(item => item.Name == TypeSafeDiagnostics.CompletedOperationsName);
        completed.Tags["typesafe.outcome"].ShouldBe("error");
        completed.Tags.ContainsKey("http.response.status_code").ShouldBeFalse();
        counts.Single(item => item.Name == TypeSafeDiagnostics.RetryAttemptsName).Value.ShouldBe(1);
        counts.Any(item => item.Name == TypeSafeDiagnostics.TokenUsageName).ShouldBeFalse();
        var span = spans.Single();
        span.GetTagItem("http.response.status_code").ShouldBeNull();
        span.GetTagItem("http.request.resend_count").ShouldBe(1);
        span.GetTagItem("error.type").ShouldBe(typeof(TypeSafeApiConnectionException).FullName);
    }

    [Fact]
    public async Task OpenTelemetry_CustomDtoStillReportsWireModelAndUsage()
    {
        var spans = new List<Activity>();
        using var tracing = OpenTelemetry.Sdk.CreateTracerProviderBuilder().AddSource(TypeSafeDiagnostics.ActivitySourceName)
            .SetSampler(new AlwaysOnSampler()).AddInMemoryExporter(spans).Build();
        using var client = ParityTestHelper.Client(ParityTestHelper.Handler());
        var response = await client.SystemOneAsync<TelemetryModelOnly>("state", ParityTestHelper.Questions(), new SystemOneOptions
        {
            Model = "superseded-model", ExtraBody = new Dictionary<string, object?> { ["model"] = "wire-model" },
        }, TestContext.Current.CancellationToken);
        response.Model.ShouldBe("jev-test");
        var span = spans.Single();
        span.GetTagItem("gen_ai.request.model").ShouldBe("wire-model");
        span.GetTagItem("gen_ai.response.model").ShouldBe("jev-test");
        span.GetTagItem("gen_ai.usage.input_tokens").ShouldBe(10L);
        span.GetTagItem("gen_ai.usage.output_tokens").ShouldBe(8L);
    }

    [Fact]
    public async Task OpenTelemetry_NoListenersSkipsTimingAndPreservesResponse()
    {
        TypeSafeDiagnostics.Source.HasListeners().ShouldBeFalse();
        TypeSafeDiagnostics.StartMeasurement().ShouldBeNull();
        using var client = ParityTestHelper.Client(ParityTestHelper.Handler());
        var response = await client.SystemOneAsync("state", ParityTestHelper.Questions(),
            cancellationToken: TestContext.Current.CancellationToken);
        response.Model.ShouldBe("jev-test");
        response.Usage!.InputTokens.ShouldBe(10);
        TypeSafeDiagnostics.StartMeasurement().ShouldBeNull();
    }

    private sealed class TelemetryModelOnly
    {
        [System.Text.Json.Serialization.JsonPropertyName("model")]
        public string? Model { get; init; }
    }

    private static MeterListener CaptureMetrics(List<(string Name, long Value, Dictionary<string, object?> Tags)> counts,
        List<(double Value, Dictionary<string, object?> Tags)>? durations = null)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, receiver) =>
        {
            if (instrument.Meter.Name == TypeSafeDiagnostics.MeterName) receiver.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            counts.Add((instrument.Name, value, tags.ToArray().ToDictionary(pair => pair.Key, pair => pair.Value))));
        if (durations is not null)
            listener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
                durations.Add((value, tags.ToArray().ToDictionary(pair => pair.Key, pair => pair.Value))));
        listener.Start();
        return listener;
    }

    [Fact]
    public async Task OpenTelemetry_ExportsCorrelatedRetryTraceAndPayloadFreeLogs()
    {
        var spans = new List<Activity>();
        var logs = new List<LogRecord>();
        using var tracing = OpenTelemetry.Sdk.CreateTracerProviderBuilder().AddSource(TypeSafeDiagnostics.ActivitySourceName)
            .SetSampler(new AlwaysOnSampler()).AddInMemoryExporter(spans).Build();
        using var loggerFactory = CreateTelemetryLoggerFactory(logs);
        using var parent = new Activity("application operation").Start();
        var handler = new ParityHandler((index, _, _) => Task.FromResult(index == 0
            ? ParityTestHelper.Response("{\"message\":\"payload-secret\"}", 429)
            : ParityTestHelper.Response()));
        using var client = new TypeSafeClient(new TypeSafeClientOptions
        {
            ApiKey = "credential-secret", Handler = handler, BaseUrl = "https://api.typesafe.ai",
            LoggerFactory = loggerFactory, LogLevel = TypeSafeLogLevel.Debug,
            Logger = _ => { }, Retry = new RetryPolicy { MaxRetries = 1, BackoffInitial = TimeSpan.Zero },
        });
        var response = await client.SystemOneAsync("payload-secret", ParityTestHelper.Questions(),
            cancellationToken: TestContext.Current.CancellationToken);

        response.Model.ShouldBe("jev-test");
        handler.Requests.Count.ShouldBe(2);
        var span = spans.Single();
        span.DisplayName.ShouldBe("POST /v1/systemone");
        span.Kind.ShouldBe(ActivityKind.Client);
        span.ParentSpanId.ShouldBe(parent.SpanId);
        span.Status.ShouldBe(ActivityStatusCode.Unset);
        span.GetTagItem("http.response.status_code").ShouldBe(200);
        span.GetTagItem("typesafe.retry.count").ShouldBe(1);
        span.GetTagItem("http.request.resend_count").ShouldBe(1);
        span.GetTagItem("gen_ai.request.model").ShouldBe("jev-latest");
        span.GetTagItem("gen_ai.response.model").ShouldBe("jev-test");
        span.GetTagItem("gen_ai.usage.input_tokens").ShouldBe(10L);
        span.GetTagItem("gen_ai.usage.output_tokens").ShouldBe(8L);
        var retry = span.Events.Single();
        retry.Name.ShouldBe("typesafe.retry");
        retry.Tags.Single(pair => pair.Key == "typesafe.retry.attempt").Value.ShouldBe(1);
        logs.Count.ShouldBe(2);
        foreach (var record in logs)
        {
            record.CategoryName.ShouldBe(typeof(TypeSafeClient).FullName);
            record.TraceId.ShouldBe(span.TraceId);
            record.SpanId.ShouldBe(span.SpanId);
            record.Exception.ShouldBeNull();
        }
        var exported = string.Join(" ", spans.SelectMany(activity => activity.TagObjects).Select(pair => pair.Value))
            + string.Join(" ", logs.Select(record => record.FormattedMessage))
            + string.Join(" ", logs.SelectMany(record => record.Attributes ?? []).Select(pair => pair.Value));
        exported.ShouldNotContain("credential-secret");
        exported.ShouldNotContain("payload-secret");
        span.GetTagItem("url.full").ShouldBeNull();
    }

    [Fact]
    public async Task OpenTelemetry_FailureHasErrorStatusWithoutExceptionMessage()
    {
        var spans = new List<Activity>();
        var logs = new List<LogRecord>();
        using var tracing = OpenTelemetry.Sdk.CreateTracerProviderBuilder().AddSource(TypeSafeDiagnostics.ActivitySourceName)
            .AddInMemoryExporter(spans).Build();
        using var loggerFactory = CreateTelemetryLoggerFactory(logs);
        var handler = new ParityHandler((_, _, _) => Task.FromResult(
            ParityTestHelper.Response("{\"message\":\"payload-secret\"}", 503)));
        using var client = new TypeSafeClient(new TypeSafeClientOptions
        {
            ApiKey = "credential-secret", Handler = handler, BaseUrl = "https://api.typesafe.ai",
            LoggerFactory = loggerFactory, Retry = new RetryPolicy { MaxRetries = 0 },
        });
        var failure = await Should.ThrowAsync<TypeSafeApiException>(() => client.Models.ListAsync(
            cancellationToken: TestContext.Current.CancellationToken));
        failure.Status.ShouldBe(503);
        var span = spans.Single();
        span.Status.ShouldBe(ActivityStatusCode.Error);
        span.GetTagItem("error.type").ShouldBe(failure.GetType().FullName);
        span.GetTagItem("http.response.status_code").ShouldBe(503);
        span.StatusDescription.ShouldBeNull();
        var record = logs.Single();
        record.LogLevel.ShouldBe(LogLevel.Error);
        record.Exception.ShouldBeNull();
        record.FormattedMessage!.ShouldNotContain("payload-secret");
        record.FormattedMessage!.ShouldNotContain("credential-secret");
        record.TraceId.ShouldBe(span.TraceId);
    }

    [Fact]
    public async Task OpenTelemetry_UnknownAnswerWarningOmitsAnswerContent()
    {
        var logs = new List<LogRecord>();
        using var loggerFactory = CreateTelemetryLoggerFactory(logs);
        var handler = ParityTestHelper.Handler("{\"model\":\"jev-test\",\"usage\":{\"input_tokens\":1,\"output_tokens\":1},\"answers\":{\"q\":{\"type\":\"payload-secret\"}}}");
        using var client = new TypeSafeClient(new TypeSafeClientOptions
        {
            ApiKey = "test", Handler = handler, LoggerFactory = loggerFactory,
            Logger = _ => { }, Retry = new RetryPolicy { MaxRetries = 0 },
        });
        var response = await client.SystemOneAsync("state", ParityTestHelper.Questions(),
            cancellationToken: TestContext.Current.CancellationToken);
        response.Answers.Count.ShouldBe(0);
        response.UnmodeledAnswers.Count.ShouldBe(1);
        var record = logs.Single();
        record.LogLevel.ShouldBe(LogLevel.Warning);
        record.FormattedMessage.ShouldBe("TypeSafe response skipped an unsupported answer type");
        record.FormattedMessage!.ShouldNotContain("payload-secret");
    }

    private static ILoggerFactory CreateTelemetryLoggerFactory(List<LogRecord> records) =>
        LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddOpenTelemetry(options =>
        {
            options.IncludeFormattedMessage = true;
            options.AddInMemoryExporter(records);
        }));

    [Theory]
    [InlineData("  DeBuG ", TypeSafeLogLevel.Debug)]
    [InlineData("info", TypeSafeLogLevel.Info)]
    [InlineData("warn", TypeSafeLogLevel.Warning)]
    [InlineData("warning", TypeSafeLogLevel.Warning)]
    [InlineData("error", TypeSafeLogLevel.Error)]
    [InlineData("off", TypeSafeLogLevel.Off)]
    [InlineData("bogus", TypeSafeLogLevel.Warning)]
    [InlineData("", TypeSafeLogLevel.Warning)]
    public Task EnvironmentLogLevel_ControlsOutputWithoutGlobalConfiguration(string value, TypeSafeLogLevel expected) => ConfigTests.WithEnvironment(async () =>
    {
        var logs = new List<TypeSafeLogEntry>();
        var handler = ParityTestHelper.Handler("{\"models\":[]}");
        using var client = new TypeSafeClient(new TypeSafeClientOptions { ApiKey = "test", Handler = handler, Logger = logs.Add });
        await client.Models.ListAsync(cancellationToken: TestContext.Current.CancellationToken);
        logs.Select(entry => entry.Level).Distinct().Order().ShouldBe(expected switch
        {
            TypeSafeLogLevel.Debug => new[] { TypeSafeLogLevel.Debug, TypeSafeLogLevel.Info },
            TypeSafeLogLevel.Info => new[] { TypeSafeLogLevel.Info },
            _ => Array.Empty<TypeSafeLogLevel>(),
        });
        if (expected <= TypeSafeLogLevel.Info)
        {
            logs.Single(entry => entry.Level == TypeSafeLogLevel.Info).Message.ShouldContain("GET");
            logs.Single(entry => entry.Level == TypeSafeLogLevel.Info).Message.ShouldContain("req-test");
        }
        if (expected != TypeSafeLogLevel.Debug) logs.Any(entry => entry.Message.Contains("headers=")).ShouldBeFalse();
        handler.Requests.Count.ShouldBe(1);
    }, "test", "https://api.typesafe.ai", null, value);

    public static IEnumerable<object[]> SecretHeaders() =>
        new[] { "Authorization", "Proxy-Authorization", "X-API-Key", "API-Key", "Cookie", "Set-Cookie", "X-Access-Token", "X-Client-Secret", "x-MiXeD-ToKeN" }
            .SelectMany(header => new[] { 200, 400, 429 }.Select(status => new object[] { header, status }));

    [Theory]
    [MemberData(nameof(SecretHeaders))]
    public async Task SecretHeaders_RequestResponseAndRetries_RedactedButBodyVisible(string header, int status)
    {
        var logs = new List<TypeSafeLogEntry>();
        var handler = new ParityHandler((_, _, _) => Task.FromResult(ParityTestHelper.Response(status == 200 ? "{\"models\":[],\"body\":\"body-credential\"}" : "{\"message\":\"failure\",\"body\":\"body-credential\"}", status,
            "req-test", new Dictionary<string, string> { [header] = "response-credential", ["x-visible"] = "response-visible" })));
        using var client = new TypeSafeClient(new TypeSafeClientOptions
        {
            ApiKey = "auth-credential", BaseUrl = "https://api.typesafe.ai", Handler = handler, LogLevel = TypeSafeLogLevel.Debug, Logger = logs.Add,
            Headers = new Dictionary<string, string> { [header] = "request-credential", ["x-visible"] = "request-visible" },
            Retry = new RetryPolicy { BackoffInitial = TimeSpan.Zero }, DelayAsync = (_, _) => Task.CompletedTask,
        });
        if (status == 200) (await client.Models.ListAsync(cancellationToken: TestContext.Current.CancellationToken)).Models.Count.ShouldBe(0);
        else (await Should.ThrowAsync<TypeSafeApiException>(() => client.Models.ListAsync())).Status.ShouldBe(status);
        handler.Requests.Count.ShouldBe(status == 429 ? 3 : 1);
        var text = string.Join("\n", logs.Select(entry => entry.Message));
        text.ShouldContain("request-visible");
        text.ShouldContain("response-visible");
        text.ShouldContain("body-credential");
        text.ShouldContain("***");
        foreach (var secret in new[] { "auth-credential", "request-credential", "response-credential" }) text.ShouldNotContain(secret);
        if (status == 429) { text.ShouldContain("retry 1"); text.ShouldContain("retry 2"); }
    }

    [Theory]
    [InlineData("ts_live_private")]
    [InlineData("ts_live_quo'te\"slash\\tail")]
    public async Task TransportException_InnerChainAndJsonEscapes_RedactedWithoutMutatingOriginal(string credential)
    {
        var original = new HttpRequestException("Illegal header value: Bearer " + credential + "; json=" + JsonSerializer.Serialize(credential),
            new InvalidOperationException("Rejected authorization: " + credential + "; provider: provider-credential"));
        var handler = new ParityHandler((_, _, _) => throw original);
        var logs = new List<TypeSafeLogEntry>();
        using var client = new TypeSafeClient(new TypeSafeClientOptions
        {
            ApiKey = credential, Handler = handler, Headers = new Dictionary<string, string> { ["x-client-secret"] = "provider-credential" },
            Retry = new RetryPolicy { MaxRetries = 0 }, LogLevel = TypeSafeLogLevel.Debug, Logger = logs.Add,
        });
        var error = await Should.ThrowAsync<TypeSafeApiConnectionException>(() => client.Models.ListAsync());
        error.GetType().ShouldBe(typeof(TypeSafeApiConnectionException));
        var diagnostic = error.ToString();
        diagnostic.ShouldNotContain(credential);
        diagnostic.ShouldNotContain(JsonSerializer.Serialize(credential)[1..^1]);
        diagnostic.ShouldNotContain("provider-credential");
        diagnostic.ShouldContain("Illegal header value");
        error.InnerException.ShouldNotBeSameAs(original);
        error.InnerException!.InnerException!.Message.ShouldBe("Rejected authorization: ***; provider: ***");
        original.Message.ShouldContain(credential);
        original.InnerException!.Message.ShouldContain("provider-credential");
        string.Join("\n", logs.Select(entry => entry.Message)).ShouldNotContain(credential);
        handler.Requests.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("'")]
    [InlineData("\"")]
    public void Redact_QuotedEscapes_AreReplacedBeforeRawCredentials(string quote)
    {
        const string credential = "private'quoted\"value\\tail";
        var value = "Bearer " + credential;
        var escaped = value.Replace("\\", "\\\\").Replace(quote, "\\" + quote);
        var headers = new Dictionary<string, string> { ["Authorization"] = value };
        TypeSafeLogging.Redact("Illegal header value b" + quote + escaped + quote, headers)
            .ShouldBe("Illegal header value b" + quote + "***" + quote);
    }

    [Fact]
    public async Task TransportException_ReconstructibleNetworkTypeAndDiagnostics_Preserved()
    {
        var original = new HttpRequestException("Network is unreachable", new System.IO.IOException("Network is unreachable"));
        var handler = new ParityHandler((_, _, _) => throw original);
        using var client = ParityTestHelper.Client(handler);
        var error = await Should.ThrowAsync<TypeSafeApiConnectionException>(() => client.Models.ListAsync());
        error.Message.ShouldBe("Connection error: Network is unreachable");
        error.InnerException!.GetType().ShouldBe(typeof(HttpRequestException));
        error.InnerException.Message.ShouldBe(original.Message);
        error.InnerException.ShouldNotBeSameAs(original);
        error.InnerException.InnerException!.GetType().ShouldBe(typeof(System.IO.IOException));
    }

    [Fact]
    public async Task TransportException_ByteReprEscapedCredential_ExactSafeChainWithoutMutation()
    {
        const string credential = "private'quoted\"value\\tail";
        const string originalMessage = "Illegal header value b'Bearer private\\'quoted\"value\\\\tail'";
        var original = new HttpRequestException(originalMessage, new InvalidOperationException("Rejected " + credential));
        var handler = new ParityHandler((_, _, _) => throw original);
        using var client = new TypeSafeClient(new TypeSafeClientOptions { ApiKey = credential, Handler = handler, Retry = new RetryPolicy { MaxRetries = 0 } });
        var error = await Should.ThrowAsync<TypeSafeApiConnectionException>(() => client.Models.ListAsync());
        error.Message.ShouldBe("Connection error: Illegal header value b'***'");
        error.InnerException!.Message.ShouldBe("Illegal header value b'***'");
        error.InnerException.InnerException!.Message.ShouldBe("Rejected ***");
        error.ToString().ShouldNotContain("private\\'quoted");
        original.Message.ShouldBe(originalMessage);
        original.InnerException!.Message.ShouldBe("Rejected " + credential);
        handler.Requests.Count.ShouldBe(1);
    }
}