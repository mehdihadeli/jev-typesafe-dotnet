# Quickstart

The SDK sends one state and one or more typed questions to the TypeSafe API. Start with the API key, then create a `TypeSafeClient` and call `SystemOneAsync`.

## Prerequisites

- .NET 10 or a compatible target framework supported by the package
- A TypeSafe API key from the [TypeSafe console](https://console.typesafe.ai/keys)

Set the key in your environment:

```bash
export TYPESAFE_API_KEY="your-api-key"
```

On Windows PowerShell:

```powershell
$env:TYPESAFE_API_KEY = "your-api-key"
```

## Install

Add the package to an application:

```bash
dotnet add package Jev.Typesafe.Sdk --prerelease
```

## Ask mixed questions

```csharp
using Jev.Typesafe.Sdk;
using Jev.Typesafe.Sdk.Models;

using var client = new TypeSafeClient();

var response = await client.SystemOneAsync(
    state: "I was charged twice. Please fix this ASAP.",
    questions: new Dictionary<string, TypeSafeQuestion>
    {
        ["billing"] = new Noul
        {
            Instructions = "Is this ticket about billing?"
        },
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
            Criteria = ["can wait", "this week", "today"]
        }
    });

Console.WriteLine(response.Nouls["billing"].Noul);
Console.WriteLine(response.Choices["tone"].Choice);
Console.WriteLine(response.Scores["urgency"].Score);
```

The client reads `TYPESAFE_API_KEY` automatically and uses `jev-latest` by default.

## Use structured state

`state` accepts any JSON-compatible value. For example:

```csharp
var response = await client.SystemOneAsync(
    state: new
    {
        ticket = new
        {
            title = "Duplicate charge",
            message = "The same payment appeared twice.",
            customerTier = "enterprise"
        }
    },
    questions: new Dictionary<string, TypeSafeQuestion>
    {
        ["needsBillingReview"] = new Noul
        {
            Instructions = "Does this ticket require billing review?"
        }
    });
```

## Override the model or endpoint

```csharp
var client = new TypeSafeClient(
    options: new TypeSafeClientOptions
    {
        ApiKey = "your-api-key",
        DefaultModel = "jev-latest",
        BaseUrl = "https://api.typesafe.ai/"
    });

var response = await client.SystemOneAsync(
    state: "A customer cannot export a report.",
    questions: questions,
    model: "jev-latest");
```

Dispose a client when it owns its `HttpClient`. When using dependency injection, pass an `HttpClient` managed by the host.

## Next steps

- Learn the request and response flow in the [architecture guide](./architecture.md).
- Choose the right primitive in the [API reference](../reference/api.md).
- Run isolated and live checks with the [testing workflow](./workflow.md).
