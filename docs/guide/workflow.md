# Testing workflow

Use two test layers, each with a different job.

## Unit tests first

Unit tests run without credentials or network access. They verify HTTP methods, paths, bearer authentication, serialized request fields, and polymorphic response models.

```bash
dotnet test --project tests/Jev.Typesafe.Sdk/unit-tests/Typesafe.Sdk.UnitTests.csproj
dotnet test --project tests/Jev.Typesafe.Sdk.Extensions.AI/unit-tests/Jev.Typesafe.Sdk.Extensions.AI.UnitTests.csproj
```

When adding a client method, assert both sides of the boundary:

- Request: method, path, headers, and JSON body.
- Response: status handling, model shape, and typed accessors.

## Integration tests second

Integration tests verify that the SDK can authenticate against the real TypeSafe service and that the documented endpoints return usable data.

```bash
export TYPESAFE_API_KEY="your-api-key"
dotnet test --project tests/Jev.Typesafe.Sdk/integration-tests/Typesafe.Sdk.IntegrationTests.csproj
dotnet test --project tests/Jev.Typesafe.Sdk.Extensions.AI/integration-tests/Jev.Typesafe.Sdk.Extensions.AI.IntegrationTests.csproj
```

Without the key, xUnit v3 skips the live tests through `SkipUnless`. Do not put a key in a test file, committed `.env`, or CI log.

## Classification evaluation

The AI package includes a deterministic `TypeSafeAnswerEvaluator` based on `Microsoft.Extensions.AI.Evaluation`. Supply known expected answers through `TypeSafeAnswerEvaluationContext`; evaluate the adapter's JSON response without another model call. The unit suite checks choice labels, numeric tolerance boundaries, missing answers, invalid JSON, and cancellation. The live AI test checks a known positive sentiment against an independently defined expected label.

This metric measures typed answer accuracy, not fluency, relevance, confidence calibration, or probability-distribution quality. See the [API reference](../reference/api.md#typesafeanswerevaluator) for its contract.

## Test a new primitive

For a new question or answer type:

1. Add a representative request to the unit test stub.
2. Assert its discriminator and concrete fields in the serialized JSON.
3. Add the response payload to the stub.
4. Assert the typed answer and filtered response view.
5. Add a live test only if the endpoint contract itself needs confirmation.

## Review checklist

- Does the test pass its `TestContext.Current.CancellationToken`?
- Does it work with the Microsoft Testing Platform runner?
- Does it avoid real network access unless it is in `integration-tests`?
- Does it cover invalid input or non-success HTTP responses when behavior changes?
- Does the documentation show the public behavior a user needs?
