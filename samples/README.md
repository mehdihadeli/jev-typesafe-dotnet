# SDK Samples

Three runnable .NET 10 console applications reference the SDK projects in this repository. They are included in the solution, build with the libraries, and are excluded from NuGet packaging.

| Project | Demonstrates |
| --- | --- |
| [CoreSdk/CoreSdk.csproj](CoreSdk/CoreSdk.csproj) | Structured state, Noul/Choice/Score questions, typed answers, model discovery, native retries, and cancellation |
| [ExtensionsAI/ExtensionsAI.csproj](ExtensionsAI/ExtensionsAI.csproj) | IChatClient adaptation, per-call state, JSON answer maps, and deterministic answer evaluation |
| [Observability/Observability.csproj](Observability/Observability.csproj) | OpenTelemetry traces, metrics, and logs; SDK retries or optional host-owned HTTP resilience |

Run the commands below from the repository root using the .NET SDK selected by the repository configuration.

## Offline Runs

These paths do not contact TypeSafe or require an API key:

```bash
dotnet run --project samples/CoreSdk/CoreSdk.csproj -- --help
dotnet run --project samples/ExtensionsAI/ExtensionsAI.csproj -- --offline
dotnet run --project samples/Observability/Observability.csproj -- --offline
dotnet run --project samples/Observability/Observability.csproj -- --offline --http-resilience
```

The AI sample evaluates a fixed positive-sentiment answer and prints `TypeSafe.AnswerAccuracy: 100%`. It does not claim to exercise the chat adapter's transport in offline mode.

The observability sample uses an in-process handler that returns HTTP 429, then a successful Noul response. It prints two simulated HTTP attempts plus console-exported telemetry. The placeholder credential and endpoint in this mode are only used with that handler; nothing is sent over the network. Console exporters are for inspecting the sample, not a recommended production destination.

## Live Runs

Set an API key in your shell. Do not put real credentials in source code or commit them.

PowerShell:

```powershell
$env:TYPESAFE_API_KEY = "YOUR_API_KEY"
```

Bash:

```bash
export TYPESAFE_API_KEY="YOUR_API_KEY"
```

Then run a sample:

```bash
dotnet run --project samples/CoreSdk/CoreSdk.csproj
dotnet run --project samples/CoreSdk/CoreSdk.csproj -- --list-models
dotnet run --project samples/ExtensionsAI/ExtensionsAI.csproj
dotnet run --project samples/Observability/Observability.csproj
dotnet run --project samples/Observability/Observability.csproj -- --http-resilience
```

Live classification sends the sample state and questions to TypeSafe and can consume billed API usage. Retries may consume additional usage. Live runs are not part of the offline smoke checks. Press Ctrl+C to cancel a request.

The samples also honor `TYPESAFE_DEFAULT_MODEL` and `TYPESAFE_BASE_URL` through the SDK. Without overrides, the SDK uses `jev-latest` and `https://api.typesafe.ai`. Observability explicitly enables SDK information-level logs.

## Retry Ownership

The core and AI examples use the SDK's `Microsoft.Extensions.Resilience` pipeline. The observability example defaults to that pipeline too.

With `--http-resilience`, the observability sample creates an `IHttpClientFactory` client with `AddStandardResilienceHandler()`. It sets SDK `Retry.MaxRetries = 0` and `Timeout = Timeout.InfiniteTimeSpan`, leaving retry and timeout ownership with the handler. It keeps the supplied HTTP client open until the sample disposes it.

In the offline SDK mode, `jev.typesafe.sdk.retry.attempts` records one retry. In the HTTP-handler mode, the SDK retry count is zero even though the handler made two HTTP attempts. Those are intentionally separate ownership boundaries. The standard handler also introduces circuit breaking, rate limiting, and timeouts; its behavior differs from the native SDK settings.

## Build

```bash
dotnet build Jev.Typesafe.Sdk.slnx --configuration Release
```

The OpenTelemetry console exporter and optional HTTP handler dependency belong only to the observability sample. No exporter dependency is added to either production SDK package.