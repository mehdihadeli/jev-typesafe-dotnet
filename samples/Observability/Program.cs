using System.Net;
using System.Net.Http.Headers;
using Jev.Typesafe.Sdk;
using Jev.Typesafe.Sdk.Exceptions;
using Jev.Typesafe.Sdk.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

if (args.Contains("--help"))
{
    Console.WriteLine("Observability sample: export SDK traces, metrics, and logs to the console.");
    Console.WriteLine("Use --offline for a simulated 429 followed by success; no API calls or credentials needed.");
    Console.WriteLine("Add --http-resilience to let an IHttpClientFactory handler own retries instead of the SDK.");
    return 0;
}

var offline = args.Contains("--offline");
var httpResilience = args.Contains("--http-resilience");
if (!offline && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TYPESAFE_API_KEY")))
{
    Console.Error.WriteLine("Set TYPESAFE_API_KEY for live classification, or use --offline.");
    return 1;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

using var tracing = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
    .AddSource(TypeSafeDiagnostics.ActivitySourceName)
    .AddConsoleExporter()
    .Build();
using var metrics = OpenTelemetry.Sdk.CreateMeterProviderBuilder()
    .AddMeter(TypeSafeDiagnostics.MeterName)
    .AddConsoleExporter()
    .Build();

var services = new ServiceCollection();
services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Information).AddOpenTelemetry(options =>
{
    options.IncludeFormattedMessage = true;
    options.AddConsoleExporter();
}));

var offlineHandler = offline ? new OfflineClassificationHandler() : null;
if (httpResilience)
{
    var httpBuilder = services.AddHttpClient("typesafe", http => http.Timeout = Timeout.InfiniteTimeSpan);
    if (offlineHandler is not null) httpBuilder.ConfigurePrimaryHttpMessageHandler(() => offlineHandler);
    httpBuilder.AddStandardResilienceHandler(options =>
    {
        options.Retry.MaxRetryAttempts = 2;
        options.Retry.Delay = TimeSpan.FromMilliseconds(100);
    });
}

using var provider = services.BuildServiceProvider();
using var http = httpResilience ? provider.GetRequiredService<IHttpClientFactory>().CreateClient("typesafe") : null;
try
{
    using var client = new TypeSafeClient(new TypeSafeClientOptions
    {
        ApiKey = offline ? "offline-sample" : null,
        BaseUrl = offline ? "https://offline.example" : null,
        Handler = httpResilience ? null : offlineHandler,
        HttpClient = http,
        LeaveOpen = httpResilience,
        Timeout = httpResilience ? Timeout.InfiniteTimeSpan : null,
        LoggerFactory = provider.GetRequiredService<ILoggerFactory>(),
        LogLevel = TypeSafeLogLevel.Info,
        Retry = new RetryPolicy { MaxRetries = httpResilience ? 0 : 2 },
    });
    Console.WriteLine($"Retry owner: {(httpResilience ? "HTTP resilience handler" : "SDK resilience pipeline")}");
    var response = await client.SystemOneAsync("An invoice was charged twice.",
        new Dictionary<string, TypeSafeQuestion>
        {
            ["billing"] = new Noul { Instructions = "Does this message describe a billing problem?" },
        }, cancellationToken: cancellation.Token);
    Console.WriteLine($"Model: {response.Model}; billing probability: {response.Nouls["billing"].Noul:F3}");
    if (offlineHandler is not null)
    {
        Console.WriteLine($"Simulated HTTP attempts: {offlineHandler.RequestCount}; no API request was made.");
        if (offlineHandler.RequestCount != 2) return 1;
    }
    return 0;
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.Error.WriteLine("Cancelled.");
    return 2;
}
catch (TypeSafeApiException failure)
{
    Console.Error.WriteLine($"API failure: HTTP {failure.Status} ({failure.GetType().Name}).");
    return 1;
}
catch (TypeSafeException failure)
{
    Console.Error.WriteLine($"SDK failure: {failure.GetType().Name}. Check credentials, configuration, and connectivity.");
    return 1;
}
finally
{
    metrics.ForceFlush();
    tracing.ForceFlush();
}

sealed class OfflineClassificationHandler : HttpMessageHandler
{
    private int _requestCount;
    public int RequestCount => _requestCount;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Increment(ref _requestCount) == 1)
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("""{"message":"Simulated temporary rate limit."}"""),
            };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
            return Task.FromResult(response);
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"model":"offline-model","usage":{"input_tokens":12,"output_tokens":4},"answers":{"billing":{"type":"noul","noul":0.98}}}"""),
        });
    }
}