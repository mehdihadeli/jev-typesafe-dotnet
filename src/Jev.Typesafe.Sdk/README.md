# Jev.Typesafe.Sdk

Core .NET client for TypeSafe AI's **System One** classification API, using
**Jev** as the default model.

This library is a community .NET port of the official
[TypeSafe Python SDK](https://github.com/typesafe-ai/typesafe-sdk-python). It
keeps the same TypeSafe API concepts and System One workflow while expressing
them through idiomatic .NET models, async APIs, `HttpClient`, and NuGet
packaging.

## What This Package Does

Use this package when your application needs direct, typed access to TypeSafe classification results. It sends application state and named questions to System One, then deserializes the response into strongly typed models.

Supported question types:

- `Noul`: probability that a yes/no proposition is true.
- `Choice`: one named option, with confidence and probabilities.
- `Score`: a numeric position on an ordered rubric, with confidence, legend, and probabilities.

The package also provides model discovery through `ListModelsAsync`, configurable retry behavior, cancellation support, caller-supplied `HttpClient` support, and explicit API error details.

This package does not reference `Microsoft.Extensions.AI`. Install `Jev.Typesafe.Sdk.Extensions.AI` separately when an `IChatClient` adapter or evaluation integration is needed.

## Install

```bash
dotnet add package Jev.Typesafe.Sdk
```

The package targets `.NET Standard 2.0` and `.NET 10`.

## Getting Started

Install the package:

```bash
dotnet add package Jev.Typesafe.Sdk
```

Set `TYPESAFE_API_KEY`, then create a client:

```csharp
using Jev.Typesafe.Sdk;
using Jev.Typesafe.Sdk.Models;

using var client = new TypeSafeClient();

var response = await client.SystemOneAsync(
    state: "I was charged twice.",
    questions: new Dictionary<string, TypeSafeQuestion>
    {
        ["billing"] = new Noul
        {
            Instructions = "Is this about billing?"
        },
        ["priority"] = new Score
        {
            Instructions = "How urgent is this?",
            Criteria = ["can wait", "this week", "today"]
        }
    });

Console.WriteLine(response.Nouls["billing"].Noul);
Console.WriteLine(response.Scores["priority"].Score);
```

The default endpoint is `https://api.typesafe.ai/` and the default model is `jev-latest`. Configure `ApiKey`, `BaseUrl`, `DefaultModel`, and `Retry` through `TypeSafeClientOptions`.

## Response Data

`SystemOneResponse` exposes:

- `Answers`: every answer keyed by question name.
- `Nouls`, `Choices`, and `Scores`: typed filtered views.
- `Model`: model used by the service.
- `Usage`: reported input and output token counts.

## Configuration

The client reads these environment variables when corresponding options are not supplied:

- `TYPESAFE_API_KEY`
- `TYPESAFE_BASE_URL`
- `TYPESAFE_DEFAULT_MODEL`
- `TYPESAFE_LOG_LEVEL`

The SDK uses `Microsoft.Extensions.Resilience` and its Polly v8 `ResiliencePipeline` to execute retries. The existing `RetryPolicy` configures two retries by default for HTTP `408`, `429`, and `5xx` responses, connection failures, and request timeouts. It preserves capped exponential backoff, jitter, `Retry-After`, per-request overrides, and a total retry budget. Cancellation interrupts requests and retry delays. The budget prevents further retries; it does not discard a successful in-flight request. No circuit breaker, rate limiter, or hedging strategy is enabled by default.

Explicit `TypeSafeClientOptions` values take precedence over environment
variables. `BaseUrl` is used when the supplied `HttpClient` has no
`BaseAddress`; an existing `BaseAddress` takes precedence. When an HTTP
resilience handler owns retries and timeouts, set `Retry.MaxRetries = 0` and
`Timeout = Timeout.InfiniteTimeSpan` in the SDK to avoid overlapping policies.

## Error Handling

Unsuccessful HTTP responses raise `TypeSafeApiException` after retry handling.
The exception exposes `StatusCode`, `ResponseBody`, `Headers`, `RequestId`, and
`RetryAfter`. Common status-specific derived types include
`TypeSafeAuthenticationException`, `TypeSafePermissionDeniedException`,
`TypeSafeNotFoundException`, `TypeSafeUnprocessableEntityException`,
`TypeSafeRateLimitException`, and `TypeSafeInternalServerException`.

Other failures use typed exceptions:

- `TypeSafeException` for invalid options, missing credentials, invalid state or
    questions, and request serialization failures.
- `TypeSafeApiConnectionException` for network failures.
- `TypeSafeApiTimeoutException` for SDK request timeouts.
- `TypeSafeApiResponseValidationException` when response data cannot be decoded;
    inspect `FieldPath` when available.

Cancellation requested by the caller is propagated and is not retried. Treat
response bodies and request state as potentially sensitive. Prefer logging the
status and request ID rather than the full response body, headers, or
credentials.

```csharp
using Jev.Typesafe.Sdk.Exceptions;

try
{
    var models = await client.ListModelsAsync(CancellationToken.None);
}
catch (TypeSafeApiException exception)
{
        Console.Error.WriteLine($"TypeSafe API returned {(int)exception.StatusCode}; request {exception.RequestId}.");
}
catch (TypeSafeApiTimeoutException)
{
        Console.Error.WriteLine("The TypeSafe request timed out.");
}
```

## Namespaces

Client and configuration types use `Jev.Typesafe.Sdk`. Question and response models use `Jev.Typesafe.Sdk.Models`; exceptions use `Jev.Typesafe.Sdk.Exceptions`; serialization types use `Jev.Typesafe.Sdk.Serialization`. The optional AI package uses `Jev.Typesafe.Sdk.Extensions.AI`, with evaluators under `.Evaluation`.

## Observability

OpenTelemetry can collect SDK traces, metrics, and structured logs. Both the activity source and meter are named `Jev.Typesafe.Sdk`, exposed as `TypeSafeDiagnostics.ActivitySourceName` and `TypeSafeDiagnostics.MeterName`. Logs use `ILogger<TypeSafeClient>` with category `Jev.Typesafe.Sdk.TypeSafeClient`.

Instrumentation uses `ActivitySource`, `Meter`, and the supplied logger factory. The SDK does not configure global providers or exporters. Unobserved instruments emit nothing; without a duration listener, no telemetry stopwatch is started, and without trace/token listeners, response enrichment is skipped. These are runtime APIs on modern .NET; the existing `System.Diagnostics.DiagnosticSource` package supplies compatibility for .NET Standard 2.0.

In a hosted application, install `OpenTelemetry.Extensions.Hosting` and an exporter such as `OpenTelemetry.Exporter.OpenTelemetryProtocol`. Register all three signals in the host's service collection:

```csharp
using Jev.Typesafe.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter(TypeSafeDiagnostics.MeterName)
        .AddOtlpExporter())
    .WithTracing(traces => traces
        .AddSource(TypeSafeDiagnostics.ActivitySourceName)
        .AddOtlpExporter());
services.AddLogging(logging => logging.AddOpenTelemetry(options =>
{
    options.IncludeFormattedMessage = true;
    options.AddOtlpExporter();
}));
services.AddSingleton(provider => new TypeSafeClient(new TypeSafeClientOptions
{
    LoggerFactory = provider.GetRequiredService<ILoggerFactory>(),
    LogLevel = TypeSafeLogLevel.Info
}));
```

Here `services` is your host's `IServiceCollection`. The host owns provider startup/shutdown and client disposal. Configure the OTLP destination through application configuration, for example `OTEL_EXPORTER_OTLP_ENDPOINT`.

### Calls, Attempts, and Usage

A client activity represents a complete SDK operation, including retry waits and decoding. `http.request.resend_count` counts SDK retries that actually started; `typesafe.retry.count` remains available for compatibility. Retry events describe scheduled backoff, so cancellation during a wait may leave an event without a subsequent attempt. HTTP-attempt spans are not created by the SDK. Add `OpenTelemetry.Instrumentation.Http` and `.AddHttpClientInstrumentation()` to the tracing registration when transport-level spans are wanted.

Activities include the operation name, HTTP method, server host/port, final attempt's response status, and exception type. Successful classifications also report `gen_ai.request.model`, `gen_ai.response.model`, `gen_ai.usage.input_tokens`, and `gen_ai.usage.output_tokens`. These facts come from the wire response, including when the caller selects a custom DTO. Cancellation and invalid responses mark the activity as an error without adding exception messages or a status description.

| Instrument | Kind | Meaning |
| --- | --- | --- |
| `jev.typesafe.sdk.operation.completed` | Counter | One finished call, with `success`, `error`, or `cancelled` outcome |
| `jev.typesafe.sdk.operation.duration` | Histogram, seconds | Entire operation latency, including backoff and response decoding |
| `jev.typesafe.sdk.retry.attempts` | Counter | Additional SDK attempts that started, excluding cancelled waits |
| `jev.typesafe.sdk.token.usage` | Histogram, tokens | Input/output token samples for successful classifications |

Completion and duration labels are `typesafe.operation.name`, `typesafe.outcome`, and, when available, `http.response.status_code` and `error.type`. Retry labels contain only the operation. Token samples use operation and `gen_ai.token.type` (`input` or `output`); model names, URLs, and request IDs are deliberately excluded from metric labels to limit cardinality. Missing or invalid token values produce no sample, and model discovery produces no token measurements. Application-owned HTTP retries are measured by that handler's instrumentation, not by the SDK retry counter.

### Logs and Privacy

SDK structured logs record completion status, retry number/delay, and failure type, correlating with the active trace. The default SDK threshold is `Warning`; choose `Info` for retry/completion logs, and configure the host's logging filters too. Set `LoggerFactory` to the host-owned factory; the SDK never disposes it.

SDK traces, measurements, and structured logs exclude state, question text, answers, headers, credentials, full URLs, and exception messages. Trace model fields contain configured/service-reported names: do not use secrets as model identifiers. Host and port are included in traces, but URL user information, query strings, and paths are not exported as URL tags. Additional HTTP instrumentation follows its own redaction rules and must be configured separately. The legacy `Logger` callback is outside this privacy guarantee: debug wire diagnostics can include bodies and should not be forwarded blindly to telemetry exporters.

## Host-Owned HTTP Resilience

Use `Microsoft.Extensions.Http.Resilience` in the application when `IHttpClientFactory` should own resilience. Disable SDK retries and its timeout to avoid overlapping policies:

```csharp
using Jev.Typesafe.Sdk;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddHttpClient("typesafe", http => http.Timeout = Timeout.InfiniteTimeSpan)
    .AddStandardResilienceHandler();
using var provider = services.BuildServiceProvider();
using var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient("typesafe");
using var client = new TypeSafeClient(new TypeSafeClientOptions
{
    HttpClient = http,
    LeaveOpen = true,
    Timeout = Timeout.InfiniteTimeSpan,
    Retry = new RetryPolicy { MaxRetries = 0 }
});
var models = await client.Models.ListAsync();
```

The standard handler adds retries, rate limiting, circuit breaking, and attempt/total timeouts. Its defaults and exception behavior differ from the SDK's Python-compatible retry settings. Configure it for your application; System One POST retries can consume additional usage. Do not add hedging unless duplicate classification requests are acceptable. `Microsoft.Extensions.Resilience` is a core SDK dependency; `Microsoft.Extensions.Http.Resilience` is only needed when the application opts into HTTP handler resilience.

## When To Choose This Package

Choose `Jev.Typesafe.Sdk` for application code that wants TypeSafe's native typed classification API and full control over state, question definitions, and response handling. Choose the separate AI package only when interoperability with `Microsoft.Extensions.AI` is a requirement.

## Related Package

[Microsoft.Extensions.AI integration](../Jev.Typesafe.Sdk.Extensions.AI/README.md)

## License

MIT. See the repository [LICENSE](../../LICENSE).
