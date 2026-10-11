# 📦 TypeSafe .NET SDK

Ask typed questions about text or JSON state and use structured answers in .NET code.

This community-maintained SDK calls the [TypeSafe AI System One API](https://docs.typesafe.ai/).
It sends application state and named questions, deserializes the answers into C#
models, and exposes probabilities, choices, scores, and model metadata. Jev is the
default model; you can select another model available to your account.

The library targets .NET Standard 2.0 and .NET 10. It is an independent project,
not an official TypeSafe SDK. API keys, billing, and model availability are
managed by TypeSafe AI.

> [!WARNING]
> The configured package ID, `Typesafe.Sdk`, is already used on NuGet.org by
> [another community SDK](https://www.nuget.org/packages/TypeSafe.Sdk).
> Installing that package does not install this repository's implementation.
> Until the publication name is resolved, use a project reference to this checkout.

## 🔄 How It Works

1. **Provide state** as text or a structured object, such as a support ticket or an application event.
2. **Define questions** with named `Noul`, `Choice`, or `Score` primitives.
3. **Send** the questions in one HTTP request with `SystemOneAsync`.
4. **Use answers** through typed response properties and your application's decision thresholds.

> [!IMPORTANT]
> The SDK sends state and questions to the configured API endpoint. It does not
> run models locally. Review what you send and avoid including credentials or
> unnecessary personal data in request state.

## 🚀 Quick Start

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

Reference the SDK project as described under [Installation](#-installation), then
ask questions about a support ticket:

```csharp
using Typesafe.Sdk;

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

## ⚙️ Configuration

```csharp
using var client = new TypeSafeClient(options: new TypeSafeClientOptions
{
    ApiKey = Environment.GetEnvironmentVariable("TYPESAFE_API_KEY"),
    BaseUrl = "https://api.typesafe.ai/",
    DefaultModel = "jev-latest",
    MaxRetries = 2,
    RetryDelay = TimeSpan.FromMilliseconds(250)
});
```

| Option | Default | Behavior |
| --- | --- | --- |
| `ApiKey` | `TYPESAFE_API_KEY` | Required bearer credential; leading and trailing whitespace is trimmed |
| `BaseUrl` | `TYPESAFE_BASE_URL`, then `https://api.typesafe.ai/` | API root when the supplied `HttpClient` has no base address |
| `DefaultModel` | `jev-latest` | Model used unless a request supplies an override |
| `MaxRetries` | `2` | Additional attempts after a retryable HTTP response; `0` disables retries |
| `RetryDelay` | `250 ms` | Initial delay, doubled for each subsequent retry |

The SDK reads `TYPESAFE_API_KEY` and `TYPESAFE_BASE_URL`; model selection is
configured through options or the request argument, not an environment variable.
For a custom API root, include a trailing slash and use an endpoint that supports
the TypeSafe request and response format.

### Supply an HTTP Client

```csharp
using var httpClient = new HttpClient
{
    Timeout = TimeSpan.FromSeconds(60)
};
using var client = new TypeSafeClient(httpClient: httpClient);
```

Reuse clients across requests. When the SDK creates its own `HttpClient`, it
disposes it with `TypeSafeClient`. A caller-supplied `HttpClient` remains owned
by the caller. Its existing `BaseAddress` takes precedence over `BaseUrl`.
The SDK sets its bearer authorization header, so avoid sharing that HTTP client
with unrelated services.

## 🛠️ Error Handling

After retries are exhausted, unsuccessful HTTP responses raise
`TypeSafeApiException`, which exposes `StatusCode` and `ResponseBody`:

```csharp
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

The SDK retries HTTP `408`, `429`, and `5xx` responses. It honors a relative
`Retry-After` duration when present; otherwise it uses exponential backoff.
Network exceptions are not retried. Transport, cancellation, and JSON parsing
errors propagate to the caller.

Missing API credentials raise `InvalidOperationException`. Negative
`MaxRetries`, null state, and null or empty questions are rejected before a
request is sent. Treat response bodies as potentially sensitive when logging.

## 📥 Installation

Until this repository has its own published NuGet identity, clone it and add a
project reference from your application directory:

```bash
git clone https://github.com/mehdihadeli/typesafe-jev-dotnet-sdk.git
dotnet add reference ./typesafe-jev-dotnet-sdk/src/Typesafe.Sdk.csproj
```

Adjust the reference path if you clone the repository elsewhere. Building from
source requires the .NET 10 SDK selected by the repository's SDK configuration.
The library produces .NET Standard 2.0 and .NET 10 assemblies.

To inspect a local release package without publishing it:

```bash
dotnet pack src/Typesafe.Sdk.csproj --configuration Release --output artifacts
```

Run that command from the repository root. It produces a NuGet package and a
symbol package. Local packing does not upload either artifact.

## 🏗️ Architecture

The production library has two main source files:

- `TypeSafeClient.cs` contains client options, HTTP calls, retry handling, and JSON converters.
- `TypesafeModels.cs` contains question types, answer types, responses, and model metadata.

Unit tests use a stub HTTP handler. Integration tests call the live TypeSafe API
when credentials are available. See the
[architecture guide](https://github.com/mehdihadeli/typesafe-jev-dotnet-sdk/blob/main/docs/guide/architecture.md)
for the request path and project dependencies.

## 💻 Development

From the repository root:

```bash
dotnet build Typesafe.Sdk.slnx --configuration Release
dotnet test --project tests/unit-tests/Typesafe.Sdk.UnitTests.csproj
dotnet test --project tests/integration-tests/Typesafe.Sdk.IntegrationTests.csproj
```

Unit tests do not contact the service. Integration tests require
`TYPESAFE_API_KEY` and may incur API usage charges; without credentials, live
tests are skipped.

For release preparation and versioning, see the
[release versioning guide](https://github.com/mehdihadeli/typesafe-jev-dotnet-sdk/blob/main/docs/guide/versioning.md).

## 📚 Documentation

Repository guides:

- [Quickstart](https://github.com/mehdihadeli/typesafe-jev-dotnet-sdk/blob/main/docs/guide/quickstart.md): first request and typed answers.
- [Architecture](https://github.com/mehdihadeli/typesafe-jev-dotnet-sdk/blob/main/docs/guide/architecture.md): transport, models, and converters.
- [Development](https://github.com/mehdihadeli/typesafe-jev-dotnet-sdk/blob/main/docs/guide/development.md): project layout and contributor workflow.
- [API reference](https://github.com/mehdihadeli/typesafe-jev-dotnet-sdk/blob/main/docs/reference/api.md): public client and model types.

Run the VitePress documentation locally:

```bash
cd docs
npm install
npm run docs:dev
```

Then open `http://localhost:5173`.

## 🤝 Contributing

Open an [issue](https://github.com/mehdihadeli/typesafe-jev-dotnet-sdk/issues)
for bugs or proposed changes. For pull requests, build the solution, run unit
tests, and update examples when the public API changes. Keep live API checks
separate from deterministic unit tests.

## License

MIT. See [LICENSE](https://github.com/mehdihadeli/typesafe-jev-dotnet-sdk/blob/main/LICENSE).
