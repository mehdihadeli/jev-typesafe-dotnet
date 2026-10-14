# API reference

## `TypeSafeClient`

Creates an authenticated client for the TypeSafe API.

Client types live in `Jev.Typesafe.Sdk`, models in `Jev.Typesafe.Sdk.Models`, and exceptions in `Jev.Typesafe.Sdk.Exceptions`.

```csharp
using var client = new TypeSafeClient(
    options: new TypeSafeClientOptions
    {
        ApiKey = "your-api-key"
    });
```

### `SystemOneAsync`

```csharp
Task<SystemOneResponse> SystemOneAsync(
    object state,
    IReadOnlyDictionary<string, TypeSafeQuestion> questions,
    string? model = null,
    CancellationToken cancellationToken = default);
```

Posts to `POST /v1/systemone`. At least one question is required.

### `ListModelsAsync`

```csharp
Task<IReadOnlyList<ModelMetadata>> ListModelsAsync(
    CancellationToken cancellationToken = default);
```

Gets available models from `GET /v1/models`.

## `TypeSafeClientOptions`

| Property       | Default                    | Description                                    |
| -------------- | -------------------------- | ---------------------------------------------- |
| `ApiKey`       | `TYPESAFE_API_KEY`         | Bearer token used for authentication.          |
| `BaseUrl`      | `https://api.typesafe.ai/` | API root, useful for test servers.             |
| `DefaultModel` | `jev-latest`               | Model used when a call has no override.        |
| `MaxRetries`   | `2`                        | Retries for `408`, `429`, and `5xx` responses. |
| `RetryDelay`   | `500 ms`                   | Compatibility option for initial retry backoff. |
| `Retry`        | SDK `RetryPolicy`          | `Microsoft.Extensions.Resilience` pipeline with status, connection/timeout retries, backoff, jitter, and retry budget. |
| `LoggerFactory` | `null`                   | App-owned factory for `ILogger<TypeSafeClient>` and OpenTelemetry logs. |
| `LogLevel`     | `Warning`                  | SDK logging threshold; also reads `TYPESAFE_LOG_LEVEL`. |

Subscribe to `TypeSafeDiagnostics.ActivitySourceName` (`Jev.Typesafe.Sdk`) for traces. Exporter configuration belongs to the application. The core package README includes OpenTelemetry and host-owned HTTP resilience examples.

## Question primitives

### `Noul`

Use a `Noul` for a yes/no proposition. The answer is a probability from `0` to `1` that the proposition is true.

```csharp
new Noul
{
    Instructions = "Does this message request a refund?",
    Criteria = new NoulCriteria
    {
        True = "The customer explicitly asks for money back.",
        False = "The customer does not request money back."
    }
}
```

### `Choice`

Use a `Choice` when the answer must be one named alternative.

```csharp
new Choice
{
    Instructions = "Which team should handle this ticket?",
    Criteria = new Dictionary<string, object?>
    {
        ["billing"] = "Charges and payment problems",
        ["technical"] = "Bugs and integration problems",
        ["other"] = null
    }
}
```

`ChoiceAnswer.Probabilities` contains the distribution for every option and `Confidence` summarizes how concentrated it is.

### `Score`

Use a `Score` when the answer is a position on an ordered rubric.

```csharp
new Score
{
    Instructions = "How severe is this issue?",
    Criteria = [
        "Cosmetic; no impact to functionality",
        "Broken or degraded feature, but a workaround exists",
        "Blocking issue; no workaround exists"
    ]
}
```

`ScoreAnswer.Score` can be fractional. `Probabilities` is keyed by rubric level and `Legend` maps levels back to their descriptions.

## Response models

`SystemOneResponse` contains:

- `Model`: model used by the service.
- `Answers`: all answers keyed by the question IDs supplied by the caller.
- `Nouls`, `Choices`, and `Scores`: typed filtered views.
- `Usage`: reported input and output token counts when available.

## Errors

`TypeSafeApiException` is thrown for unsuccessful HTTP responses after retry handling. It exposes:

- `StatusCode`: returned HTTP status.
- `ResponseBody`: raw response text for diagnostics.

Missing API keys and invalid client options throw `InvalidOperationException` or `ArgumentOutOfRangeException` during client creation.

## `TypeSafeChatClient`

Available in the optional `Jev.Typesafe.Sdk.Extensions.AI` package, namespace `Jev.Typesafe.Sdk.Extensions.AI`.

```csharp
IChatClient AsChatClient(
    this TypeSafeClient client,
    IReadOnlyDictionary<string, TypeSafeQuestion> questions,
    bool ownsClient = false);
```

The adapter implements `GetResponseAsync`, `GetStreamingResponseAsync`, `GetService`, and `Dispose`. It accepts non-empty text-message history with user, system, or assistant roles. Default state is an array of `{ role, content }` objects. Questions are copied on construction. Disposal leaves the borrowed core client usable unless `ownsClient` is `true`.

Responses contain answer-map JSON keyed by question ID, retaining discriminators, confidence, probabilities, and score legends. `RawRepresentation` contains the typed `SystemOneResponse`. `ModelId`, `FinishReason`, and available token counts are populated. Total token count is present only when both input and output counts are available.

`GetService` exposes the core client, adapter, and `ChatClientMetadata`; keyed service requests return `null`. Streaming emits completed-response updates, including usage when available, after the service response arrives.

Only `ModelId`, null or `ChatResponseFormat.Json` response format, and custom `TypeSafeChatOptions` are supported. Tools, non-text content, sampling controls, stored conversations, text output, and custom JSON schemas throw `NotSupportedException` before HTTP. Schema-based `GetResponseAsync<T>` helpers are unsupported.

## `TypeSafeChatOptions`

Extends `ChatOptions` with optional per-request overrides:

- `State`: JSON-compatible state, replacing the message-history array.
- `Questions`: non-empty question dictionary, replacing configured questions.

`Clone()` preserves custom fields and copies the question dictionary, while retaining references to state and individual question definitions.

## `TypeSafeAnswerEvaluator`

Available in namespace `Jev.Typesafe.Sdk.Extensions.AI.Evaluation`. Implements `Microsoft.Extensions.AI.Evaluation.IEvaluator` and reports a single `NumericMetric` named `TypeSafe.AnswerAccuracy`.

```csharp
new TypeSafeAnswerEvaluationContext(
    IReadOnlyDictionary<string, TypeSafeAnswer> expectedAnswers,
    double numericTolerance = 0.01);
```

Pass exactly one context through `EvaluateAsync`'s `additionalContext` parameter. Expected answers must be a non-empty named map of `NoulAnswer`, `ChoiceAnswer`, or `ScoreAnswer` objects. Numeric tolerance must be finite and non-negative.

Choice labels compare ordinally. Noul and Score values match when absolute error is no greater than tolerance. Missing answers, wrong discriminators, and incorrect values count as mismatches; extra answers are ignored. Accuracy is matching expected answers divided by all expected answers, from 0 to 1. Confidence and probability distributions are not scored. Invalid JSON or a non-object response throws `JsonException`.

Evaluation uses response text rather than `RawRepresentation`, so it also works after responses are cached or reconstructed. It requires no judge client and makes no network calls.
