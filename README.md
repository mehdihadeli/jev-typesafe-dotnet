# Jev.Typesafe.Sdk

[![build-test-publish](https://img.shields.io/github/actions/workflow/status/mehdihadeli/typesafe-jev-dotnet-sdk/build-and-publish.yml?branch=main&style=for-the-badge&label=build-test-publish)](https://github.com/mehdihadeli/typesafe-jev-dotnet-sdk/actions/workflows/build-and-publish.yml)
[![License](https://img.shields.io/github/license/mehdihadeli/typesafe-jev-dotnet-sdk?style=for-the-badge)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET%20Standard%202.0%20%7C%20.NET%2010-512bd4?style=for-the-badge)](https://dotnet.microsoft.com/platform/dotnet)

**Jev.Typesafe.Sdk** is a community .NET library for [TypeSafe AI](https://typesafe.ai/)
and its **System One** classification API.

Send text or JSON state with named typed questions, then use structured `Noul`,
`Choice`, and `Score` answers in application code.

**Jev** is the default System One model, with support for selecting another model
available to your account. This project is a community .NET port of the official
[TypeSafe Python SDK](https://github.com/typesafe-ai/typesafe-sdk-python), adapted
to .NET models, async APIs, `HttpClient`, and NuGet packaging. It is not an
official TypeSafe-maintained .NET SDK. API keys, billing, and model availability
remain managed by TypeSafe AI.

The libraries target .NET Standard 2.0 and .NET 10. Install the core package for
native typed System One calls, or add the optional AI package for
`Microsoft.Extensions.AI` interoperability and evaluation.

> [!NOTE]
> This repository publishes two packages: `Jev.Typesafe.Sdk` for the core typed
> API client and `Jev.Typesafe.Sdk.Extensions.AI` for optional
> `Microsoft.Extensions.AI` integration.

## Packages

| Package | Description | Latest NuGet |
| --- | --- | --- |
| [`Jev.Typesafe.Sdk`](https://www.nuget.org/packages/Jev.Typesafe.Sdk) | Core typed client for System One. Sends text or structured state with `Noul`, `Choice`, and `Score` questions, then returns structured answers, probabilities, confidence, usage, and model metadata. | [![NuGet](https://img.shields.io/nuget/v/Jev.Typesafe.Sdk?style=for-the-badge&color=blue&label=NuGet)](https://www.nuget.org/packages/Jev.Typesafe.Sdk) |
| [`Jev.Typesafe.Sdk.Extensions.AI`](https://www.nuget.org/packages/Jev.Typesafe.Sdk.Extensions.AI) | Optional `Microsoft.Extensions.AI` integration with an `IChatClient` adapter and deterministic evaluation for TypeSafe answers. | [![NuGet](https://img.shields.io/nuget/v/Jev.Typesafe.Sdk.Extensions.AI?style=for-the-badge&color=blue&label=NuGet)](https://www.nuget.org/packages/Jev.Typesafe.Sdk.Extensions.AI) |

Install only the core package for direct typed classification. Add the AI
package when an application already uses `IChatClient` pipelines or needs the
standard evaluation abstractions. The AI package does not replace the core
client; it builds on it.

Each package has its own NuGet README:

- [Core SDK README](src/Jev.Typesafe.Sdk/README.md)
- [Microsoft.Extensions.AI README](src/Jev.Typesafe.Sdk.Extensions.AI/README.md)

## Samples

Runnable console examples for core classification, `IChatClient` and evaluation,
and OpenTelemetry with optional HTTP resilience are in [samples/README.md](samples/README.md).
Offline evaluator and telemetry modes need no API key and send no network requests.

## 🔄 How It Works

1. **Provide state** as text or a structured object, such as a support ticket or an application event.
2. **Define questions** with named `Noul`, `Choice`, or `Score` primitives.
3. **Send** the questions in one HTTP request with `SystemOneAsync`.
4. **Use answers** through typed response properties and your application's decision thresholds.

> [!IMPORTANT]
> The SDK sends state and questions to the configured API endpoint. It does not
> run models locally. Review what you send and avoid including credentials or
> unnecessary personal data in request state.

## Getting Started

Install the core package for direct typed System One calls:

```bash
dotnet add package Jev.Typesafe.Sdk
```

Add `Jev.Typesafe.Sdk.Extensions.AI` only when the application needs the
`Microsoft.Extensions.AI` adapter or evaluation APIs.

Create an API key in the [TypeSafe console](https://console.typesafe.ai/) and set
`TYPESAFE_API_KEY` in your shell.

PowerShell:

```powershell
$env:TYPESAFE_API_KEY = "YOUR_API_KEY"
```

Bash:

```bash
export TYPESAFE_API_KEY="YOUR_API_KEY"
```

Then create a client and ask typed questions about a support ticket:

```csharp
using Jev.Typesafe.Sdk;
using Jev.Typesafe.Sdk.Models;

using var client = new TypeSafeClient();

var response = await client.SystemOneAsync(
    state: "I was charged twice. Please fix this ASAP.",
    questions: new Dictionary<string, TypeSafeQuestion>
    {
        ["billing"] = new Noul { Instructions = "Is this ticket about billing?" },
        ["tone"] = new Choice
        {
            Instructions = "What is the customer's tone?",
            Criteria = new Dictionary<string, object?>
            {
                ["calm"] = null,
                ["frustrated"] = null,
                ["angry"] = null
            }
        },
        ["urgency"] = new Score
        {
            Instructions = "How urgent is this ticket?",
            Criteria = new object?[] { "can wait", "this week", "today" }
        }
    });

Console.WriteLine(response.Nouls["billing"].Noul);
Console.WriteLine(response.Choices["tone"].Choice);
Console.WriteLine(response.Scores["urgency"].Score);
```

> [!NOTE]
> The client reads `TYPESAFE_API_KEY` when `ApiKey` is not supplied. Requests use
> `jev-latest` and `https://api.typesafe.ai/` by default.

## ✨ Features

- ✅ Typed `Noul`, `Choice`, and `Score` questions with named answers.
- ✅ Text or structured state and multiple questions in one request.
- ✅ Model discovery, a configurable default model, and per-request model selection.
- ✅ Asynchronous HTTP calls, cancellation, and retries for HTTP `408`, `429`, and `5xx` responses.
- ✅ .NET Standard 2.0 and .NET 10 support with caller-supplied `HttpClient` integration.

## 📤 What the SDK Produces

Every `SystemOneAsync` call returns a `SystemOneResponse` containing the model,
typed answers, and optional usage information:

| Response view | Contains |
| --- | --- |
| `Answers` | Every answer keyed by the question name |
| `Nouls` | `NoulAnswer` values for yes/no questions |
| `Choices` | `ChoiceAnswer` values and option probabilities |
| `Scores` | `ScoreAnswer` values, confidence, legend, and probabilities |

Answer names are the same names supplied in the request, so application code can
route on stable keys instead of parsing generated text.

## 📤 Question Types and Answers

| Question | Purpose | Answer properties |
| --- | --- | --- |
| `Noul` | Evaluate a yes/no statement | `Noul`, the probability that the statement is true |
| `Choice` | Select from named alternatives | `Choice`, `Confidence`, `Probabilities` |
| `Score` | Evaluate against an ordered rubric | `Score`, `Confidence`, `Probabilities`, `Legend` |

`Choice.Criteria` maps option names to optional descriptions.
`Score.Criteria` contains an ordered list of rubric descriptions.
`Noul.Criteria` can describe the true and false cases through `NoulCriteria`.

Answers retain the question names supplied in the request. Access them through
`response.Nouls`, `response.Choices`, `response.Scores`, or the mixed
`response.Answers` dictionary. The response also includes `Model` and optional
`Usage` information.

## 🧰 Common Usage

### Send Structured State

Pass a serializable object instead of a plain string:

```csharp
var response = await client.SystemOneAsync(
    state: new { Subject = "Duplicate charge", Amount = 49.00m },
    questions: new Dictionary<string, TypeSafeQuestion>
    {
        ["billing"] = new Noul
        {
            Instructions = "Does this ticket concern billing?"
        }
    });
```

Examples in this section assume an existing `TypeSafeClient` named `client`.

### Discover Models

```csharp
var models = await client.ListModelsAsync();

foreach (var model in models)
{
    Console.WriteLine($"{model.Name}: {model.Description}");
}
```

Set `TypeSafeClientOptions.DefaultModel` for all requests, or pass `model` to
`SystemOneAsync` to override it for one call. The service determines which model
names are available to your account.

### Cancel a Request

```csharp
using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
var models = await client.ListModelsAsync(cancellation.Token);
```

Both API methods accept a `CancellationToken`. Cancellation also interrupts
the delay between retry attempts.

### Use a Caller-Owned HTTP Client

```csharp
using var httpClient = new HttpClient
{
    Timeout = TimeSpan.FromSeconds(60)
};
using var client = new TypeSafeClient(httpClient: httpClient);
```

Reuse `HttpClient` instances across requests. A caller-supplied client remains
owned by the caller; the SDK only disposes clients it creates itself.

## Configuration

```csharp
using var client = new TypeSafeClient(options: new TypeSafeClientOptions
{
    ApiKey = Environment.GetEnvironmentVariable("TYPESAFE_API_KEY"),
    BaseUrl = "https://api.typesafe.ai/",
    DefaultModel = "jev-latest",
    Retry = new RetryPolicy { MaxRetries = 2 }
});
```

| Option | Default | Behavior |
| --- | --- | --- |
| `ApiKey` | `TYPESAFE_API_KEY` | Required bearer credential; leading and trailing whitespace is trimmed |
| `BaseUrl` | `TYPESAFE_BASE_URL`, then `https://api.typesafe.ai/` | API root when the supplied `HttpClient` has no base address |
| `DefaultModel` | `TYPESAFE_DEFAULT_MODEL`, then `jev-latest` | Model used unless a request supplies an override |
| `Retry` | SDK `RetryPolicy` | Executed by `Microsoft.Extensions.Resilience`; two retries by default, including connection failures and request timeouts; capped backoff, jitter, `Retry-After`, and total budget |
| `MaxRetries` | `2` | Compatibility option when `Retry` is not supplied; `0` disables SDK retries |
| `RetryDelay` | `500 ms` | Compatibility option for initial retry backoff |
| `LoggerFactory` | `null` | App-owned factory for `ILogger<TypeSafeClient>` and OpenTelemetry logs |
| `LogLevel` | `TYPESAFE_LOG_LEVEL`, then `Warning` | SDK logging threshold |

The SDK reads `TYPESAFE_API_KEY`, `TYPESAFE_BASE_URL`, `TYPESAFE_DEFAULT_MODEL`,
and `TYPESAFE_LOG_LEVEL` when corresponding options are not supplied. Explicit
`TypeSafeClientOptions` values take precedence over environment variables.
For a custom API root, include a trailing slash and use an endpoint that supports
the TypeSafe request and response format.

The [core package README](src/Jev.Typesafe.Sdk/README.md#observability) includes
OpenTelemetry tracing/metrics/logging registration and optional `Microsoft.Extensions.Http.Resilience`
integration. Core SDK retries use `Microsoft.Extensions.Resilience` with a Polly v8
pipeline. The application owns exporters and HTTP resilience handlers; disable SDK
retries when an HTTP handler owns retries to avoid multiplying attempts.

When the SDK creates its own `HttpClient`, it disposes it with `TypeSafeClient`.
An existing `BaseAddress` takes precedence over `BaseUrl`. The SDK sets the
bearer authorization header, so avoid sharing that client with unrelated services.

## Observability

OpenTelemetry support covers traces, metrics, and structured `ILogger<TypeSafeClient>` logs. Subscribe to activity source and meter **`Jev.Typesafe.Sdk`** through the public names on `TypeSafeDiagnostics`. Exporters and provider lifetime belong to the application, not the SDK.

For a hosted application using `OpenTelemetry.Extensions.Hosting` and `OpenTelemetry.Exporter.OpenTelemetryProtocol`, register these with the host's `IServiceCollection`:

```csharp
using Jev.Typesafe.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter(TypeSafeDiagnostics.MeterName).AddOtlpExporter())
    .WithTracing(traces => traces.AddSource(TypeSafeDiagnostics.ActivitySourceName).AddOtlpExporter());
services.AddLogging(logging => logging.AddOpenTelemetry(options => options.AddOtlpExporter()));
services.AddSingleton(provider => new TypeSafeClient(new TypeSafeClientOptions
{
    LoggerFactory = provider.GetRequiredService<ILoggerFactory>(),
    LogLevel = TypeSafeLogLevel.Info
}));
```

Each SDK operation gets one client span spanning its attempts and backoff. Trace metadata includes final HTTP status, retry count (`http.request.resend_count`), requested/returned model names, and reported token usage. Transport-attempt spans are optional: add `.AddHttpClientInstrumentation()` from `OpenTelemetry.Instrumentation.Http` separately. SDK subscriptions alone do not enable them.

| Metric | Measures |
| --- | --- |
| `jev.typesafe.sdk.operation.completed` | Finished calls grouped by success, error, or cancellation |
| `jev.typesafe.sdk.operation.duration` | End-to-end latency in seconds, retry waits included |
| `jev.typesafe.sdk.retry.attempts` | Retries that started, not merely scheduled waits |
| `jev.typesafe.sdk.token.usage` | Per-call token histogram split into input and output |

Metrics keep labels bounded: operation, outcome, status/error type where applicable, and token direction. Model names are trace-only. SDK telemetry excludes request/response content, headers, full URLs, credentials, and exception messages. The legacy debug `Logger` callback can include bodies and is not covered by that guarantee. When no listeners subscribe, instruments stay silent and telemetry timing/enrichment is skipped. Instrumentation uses runtime diagnostic APIs, with the existing compatibility package for .NET Standard 2.0; no SDK-owned OpenTelemetry exporter is introduced.

See [observability details](src/Jev.Typesafe.Sdk/README.md#observability) for trace attributes, privacy boundaries, log levels, and host-owned retry behavior.

## Error Handling

After retries are exhausted, unsuccessful HTTP responses raise
`TypeSafeApiException`, which exposes `StatusCode` and `ResponseBody`:

```csharp
using Jev.Typesafe.Sdk.Exceptions;

try
{
    var models = await client.ListModelsAsync();
    Console.WriteLine($"Available models: {models.Count}");
}
catch (TypeSafeApiException exception)
{
    Console.Error.WriteLine($"TypeSafe returned HTTP {(int)exception.StatusCode}.");
}
```

The SDK retries HTTP `408`, `429`, and `5xx` responses, connection failures,
and request timeouts by default. It honors `Retry-After` when present; otherwise
it uses capped exponential backoff with jitter. Caller cancellation is not retried.
Transport and response-validation failures expose typed SDK exceptions.

Common exception types are:

- `TypeSafeException`: invalid client options, missing credentials, invalid state
    or questions, and request serialization failures.
- `TypeSafeApiException`: an unsuccessful API response. Derived types identify
    common `400`, `401`, `403`, `404`, `422`, `429`, and `5xx` responses.
- `TypeSafeApiConnectionException`: a network or connection failure after retry
    handling.
- `TypeSafeApiTimeoutException`: an SDK timeout after retry handling.
- `TypeSafeApiResponseValidationException`: a response could not be decoded;
    `FieldPath` identifies the invalid response field when available.

`TypeSafeApiException` exposes `StatusCode`, `ResponseBody`, `Headers`,
`RequestId`, and `RetryAfter`. Treat response bodies and request state as
potentially sensitive; log status and request ID instead of full bodies or
credentials. If an HTTP resilience handler also owns retries, set
`Retry.MaxRetries = 0` in the SDK to avoid multiplying attempts.

Missing API credentials raise `TypeSafeException`. Negative
`MaxRetries`, null state, and null or empty questions are rejected before a
request is sent. Treat response bodies as potentially sensitive when logging.

## 📥 Installation

The core NuGet package is `Jev.Typesafe.Sdk`. The optional integration package is
`Jev.Typesafe.Sdk.Extensions.AI`; it depends on the core package. Both target
.NET Standard 2.0 and .NET 10. Once the desired release is published, install it:

```bash
dotnet add package Jev.Typesafe.Sdk --prerelease
dotnet add package Jev.Typesafe.Sdk.Extensions.AI --prerelease
```

For unpublished versions, clone the repository and add a project reference:

```bash
git clone https://github.com/mehdihadeli/typesafe-jev-dotnet-sdk.git
dotnet add reference ./typesafe-jev-dotnet-sdk/src/Jev.Typesafe.Sdk/Jev.Typesafe.Sdk.csproj
```

Adjust the reference path if you clone the repository elsewhere. Building from
source requires the .NET 10 SDK selected by the repository's SDK configuration.
The library produces .NET Standard 2.0 and .NET 10 assemblies.

Use the `Jev.Typesafe.Sdk` and `Jev.Typesafe.Sdk.Extensions.AI` package identities
shown above. Do not install an unrelated package named `Typesafe.Sdk`.

To inspect a local release package without publishing it:

```bash
dotnet pack Jev.Typesafe.Sdk.slnx --configuration Release --output artifacts
```

Run that command from the repository root. It produces separate NuGet and
symbol packages for the core SDK and AI integration. Local packing does not
upload artifacts.

## Microsoft.Extensions.AI Integration

The optional package adapts typed classification to `IChatClient`. Configure
questions explicitly; chat messages become an array of `{ role, content }`
objects in the request state.

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Jev.Typesafe.Sdk;
using Jev.Typesafe.Sdk.Models;
using Jev.Typesafe.Sdk.Extensions.AI;
using Jev.Typesafe.Sdk.Extensions.AI.Evaluation;

using var core = new TypeSafeClient();
using IChatClient chat = core.AsChatClient(
    new Dictionary<string, TypeSafeQuestion>
    {
        ["sentiment"] = new Choice
        {
            Instructions = "Classify the sentiment of the user's message.",
            Criteria = new Dictionary<string, object?>
            {
                ["positive"] = null,
                ["negative"] = null,
            },
        },
    });

ChatMessage[] messages = [new(ChatRole.User, "I love this product!")];
var response = await chat.GetResponseAsync(messages);
Console.WriteLine(response.Text);

IEvaluator evaluator = new TypeSafeAnswerEvaluator();
var result = await evaluator.EvaluateAsync(messages, response,
    additionalContext:
    [
        new TypeSafeAnswerEvaluationContext(
            new Dictionary<string, TypeSafeAnswer>
            {
                ["sentiment"] = new ChoiceAnswer
                {
                    Type = "choice",
                    Choice = "positive",
                },
            }),
    ]);
Console.WriteLine(result.Get<NumericMetric>(
    TypeSafeAnswerEvaluator.AccuracyMetricName).Value);
```

`TypeSafeChatOptions` supports per-request `State` and `Questions` overrides,
alongside `ChatOptions.ModelId`. `ChatResponse.RawRepresentation` retains the
typed `SystemOneResponse`, and model and token usage metadata are propagated.
The adapter borrows the core client by default; `ownsClient: true` transfers
disposal responsibility.

Responses are fixed answer-map JSON, not free-form chat. Only text messages
with user, system, or assistant roles are supported. Tool calling, sampling
controls, stored conversations, text response format, and custom JSON schemas
are rejected. Schema-based `GetResponseAsync<T>` helpers are therefore not
supported. Streaming emits completed-response updates after the HTTP response;
it does not provide token-by-token streaming.

`TypeSafeAnswerEvaluator` uses `Microsoft.Extensions.AI.Evaluation` without an
LLM judge or additional API calls. `TypeSafe.AnswerAccuracy` ranges from 0 to 1:
choice labels match ordinally, Noul and Score values match within the context's
numeric tolerance (default `0.01`). Missing or mismatched expected answers count
as incorrect; extra answers are ignored. Invalid response JSON is rejected.
Confidence and probability distributions are not evaluated.

## 🏗️ Architecture

Production code is split into two projects:

- `src/Jev.Typesafe.Sdk`: client, options, typed models, and JSON converters, with no Microsoft AI dependency.
- `src/Jev.Typesafe.Sdk.Extensions.AI`: `IChatClient` adapter and classification evaluation using Microsoft AI abstractions.

Unit tests use a stub HTTP handler. Integration tests call the live TypeSafe API
when credentials are available. See the
[architecture guide](https://github.com/mehdihadeli/typesafe-jev-dotnet-sdk/blob/main/docs/guide/architecture.md)
for the request path and project dependencies.

## 📖 Documentation

The [documentation site](docs) expands on the examples and implementation:

- [Quickstart](docs/guide/quickstart.md): install the SDK and send a first request.
- [Workflow](docs/guide/workflow.md): test deterministic HTTP behavior and live API calls.
- [Architecture](docs/guide/architecture.md): follow request serialization, transport, and response conversion.
- [Development](docs/guide/development.md): build, test, and extend the repository.
- [API reference](docs/reference/api.md): browse the public client and model types.

Run the documentation site locally:

```bash
cd docs
npm install
npm run docs:dev
```

Then open `http://localhost:5173`.

## 💻 Development

From the repository root:

```bash
dotnet build Jev.Typesafe.Sdk.slnx --configuration Release
dotnet test --project tests/Jev.Typesafe.Sdk/unit-tests/Typesafe.Sdk.UnitTests.csproj
dotnet test --project tests/Jev.Typesafe.Sdk.Extensions.AI/unit-tests/Jev.Typesafe.Sdk.Extensions.AI.UnitTests.csproj
dotnet test --project tests/Jev.Typesafe.Sdk/integration-tests/Typesafe.Sdk.IntegrationTests.csproj
dotnet test --project tests/Jev.Typesafe.Sdk.Extensions.AI/integration-tests/Jev.Typesafe.Sdk.Extensions.AI.IntegrationTests.csproj
```

Unit tests do not contact the service. Integration tests require
`TYPESAFE_API_KEY` and may incur API usage charges; without credentials, live
tests are skipped.

For release preparation and versioning, see the
[release versioning guide](https://github.com/mehdihadeli/typesafe-jev-dotnet-sdk/blob/main/docs/guide/versioning.md).

## 🤝 Contributing

Open an [issue](https://github.com/mehdihadeli/typesafe-jev-dotnet-sdk/issues)
for bugs or proposed changes. For pull requests, build the solution, run unit
tests, and update examples when the public API changes. Keep live API checks
separate from deterministic unit tests.

## License

MIT. See [LICENSE](https://github.com/mehdihadeli/typesafe-jev-dotnet-sdk/blob/main/LICENSE).
