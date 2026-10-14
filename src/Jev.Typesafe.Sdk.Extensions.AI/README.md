# Jev.Typesafe.Sdk.Extensions.AI

Optional `Microsoft.Extensions.AI` integration for the TypeSafe AI System One API.

This is the companion .NET integration for the community port of the official
[TypeSafe Python SDK](https://github.com/typesafe-ai/typesafe-sdk-python). It
adds .NET-specific `Microsoft.Extensions.AI` interoperability without changing
the core TypeSafe request and response model.

## What This Package Does

This package adapts TypeSafe's typed classification API to the standard `IChatClient` abstraction and provides a deterministic evaluator for typed answers.

It contains two integrations:

- `TypeSafeChatClient`: sends text chat messages and configured TypeSafe questions through the core SDK, returning answer-map JSON in a `ChatResponse`.
- `TypeSafeAnswerEvaluator`: implements `IEvaluator` from `Microsoft.Extensions.AI.Evaluation` and scores expected typed answers without making another model call.

The package depends on `Jev.Typesafe.Sdk`; it cannot be used without the core client.

## Install

```bash
dotnet add package Jev.Typesafe.Sdk.Extensions.AI
```

This also brings in `Jev.Typesafe.Sdk`, `Microsoft.Extensions.AI.Abstractions`, and `Microsoft.Extensions.AI.Evaluation`. The package targets `.NET Standard 2.0` and `.NET 10`.

## Use As `IChatClient`

Configure questions explicitly when creating the adapter:

```csharp
using Microsoft.Extensions.AI;
using Jev.Typesafe.Sdk;
using Jev.Typesafe.Sdk.Models;
using Jev.Typesafe.Sdk.Extensions.AI;

using var core = new TypeSafeClient();
using IChatClient chat = core.AsChatClient(
    new Dictionary<string, TypeSafeQuestion>
    {
        ["sentiment"] = new Choice
        {
            Instructions = "Classify the sentiment of the message.",
            Criteria = new Dictionary<string, object?>
            {
                ["positive"] = null,
                ["negative"] = null
            }
        }
    });

var messages = new[]
{
    new ChatMessage(ChatRole.User, "I love this product!")
};

var response = await chat.GetResponseAsync(messages);
Console.WriteLine(response.Text);
```

The response text is a JSON object keyed by question name. `RawRepresentation` contains the original typed `SystemOneResponse`. Model ID, finish reason, and available token usage are mapped to `ChatResponse` metadata.

`TypeSafeChatOptions` can override `State` and `Questions` for one request. `ChatOptions.ModelId` overrides the configured model. The adapter borrows the core client by default; pass `ownsClient: true` to transfer disposal responsibility.

## Supported Chat Surface

Supported:

- User, system, and assistant text messages.
- `ChatOptions.ModelId`.
- `ChatResponseFormat.Json`.
- Per-request `TypeSafeChatOptions` state and question overrides.
- Completed-response updates through `GetStreamingResponseAsync`.

Not supported:

- Token-by-token streaming. System One returns a completed classification response.
- Tool calls, non-text content, sampling controls, stored conversations, text response format, and custom JSON schemas.
- Schema-based `GetResponseAsync<T>` helpers. TypeSafe returns its own typed answer-map schema.

Unsupported options fail before an HTTP request is sent.

## Evaluate Typed Answers

Use `TypeSafeAnswerEvaluationContext` to provide expected answers and `TypeSafeAnswerEvaluator` to produce a `NumericMetric` named `TypeSafe.AnswerAccuracy`:

```csharp
using Microsoft.Extensions.AI.Evaluation;
using Jev.Typesafe.Sdk;
using Jev.Typesafe.Sdk.Models;
using Jev.Typesafe.Sdk.Extensions.AI.Evaluation;

var evaluator = new TypeSafeAnswerEvaluator();
var result = await evaluator.EvaluateAsync(
    messages,
    response,
    additionalContext:
    [
        new TypeSafeAnswerEvaluationContext(
            new Dictionary<string, TypeSafeAnswer>
            {
                ["sentiment"] = new ChoiceAnswer
                {
                    Type = "choice",
                    Choice = "positive"
                }
            })
    ]);

var accuracy = result.Get<NumericMetric>(
    TypeSafeAnswerEvaluator.AccuracyMetricName).Value;
Console.WriteLine(accuracy);
```

Evaluation is local and deterministic. It does not use an LLM judge or make network calls.

- Choice answers compare labels using ordinal string comparison.
- Noul and Score values match when their absolute difference is within `NumericTolerance`.
- The default numeric tolerance is `0.01`.
- Missing or mismatched expected answers count as incorrect.
- Extra answers are ignored.
- Confidence and probability distributions are not evaluated.

Accuracy ranges from `0` to `1`, calculated as matching expected answers divided by the number of expected answers.

## When To Choose This Package

Choose this package when an application already uses `IChatClient`, `Microsoft.Extensions.AI` middleware, or the `Microsoft.Extensions.AI.Evaluation` reporting and evaluation ecosystem. Use [the core package](../Jev.Typesafe.Sdk/README.md) alone when direct typed System One calls are sufficient and no AI abstraction integration is needed.

## License

MIT. See the repository [LICENSE](../../LICENSE).
