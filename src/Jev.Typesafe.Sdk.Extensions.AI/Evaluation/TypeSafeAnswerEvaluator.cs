using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace Jev.Typesafe.Sdk.Extensions.AI.Evaluation;

public sealed class TypeSafeAnswerEvaluator : IEvaluator
{
    public const string AccuracyMetricName = "TypeSafe.AnswerAccuracy";

    public IReadOnlyCollection<string> EvaluationMetricNames { get; } =
        Array.AsReadOnly(new[] { AccuracyMetricName });

    public ValueTask<EvaluationResult> EvaluateAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration = null,
        IEnumerable<EvaluationContext>? additionalContext = null,
        CancellationToken cancellationToken = default
    )
    {
        if (messages is null)
        {
            throw new ArgumentNullException(nameof(messages));
        }
        if (modelResponse is null)
        {
            throw new ArgumentNullException(nameof(modelResponse));
        }
        cancellationToken.ThrowIfCancellationRequested();
        var contexts = additionalContext?.OfType<TypeSafeAnswerEvaluationContext>().ToArray();
        if (contexts is not { Length: 1 })
        {
            throw new ArgumentException("Exactly one TypeSafeAnswerEvaluationContext is required.", nameof(additionalContext));
        }
        var context = contexts[0];
        using var document = JsonDocument.Parse(modelResponse.Text);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Expected a JSON object keyed by question names.");
        }
        var matches = 0;
        foreach (var expected in context.ExpectedAnswers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (document.RootElement.TryGetProperty(expected.Key, out var actual)
                && Matches(actual, expected.Value, context.NumericTolerance))
            {
                matches++;
            }
        }
        var metric = new NumericMetric(
            AccuracyMetricName,
            (double)matches / context.ExpectedAnswers.Count,
            $"{matches} of {context.ExpectedAnswers.Count} expected answers matched. Extra answers are ignored."
        );
        return new ValueTask<EvaluationResult>(new EvaluationResult(metric));
    }

    private static bool Matches(JsonElement actual, TypeSafeAnswer expected, double tolerance)
    {
        if (actual.ValueKind != JsonValueKind.Object
            || !actual.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
        {
            return false;
        }
        return expected switch
        {
            NoulAnswer noul => type.GetString() == "noul" && MatchesNumber(actual, "noul", noul.Noul, tolerance),
            ScoreAnswer score => type.GetString() == "score" && MatchesNumber(actual, "score", score.Score, tolerance),
            ChoiceAnswer choice => type.GetString() == "choice"
                && actual.TryGetProperty("choice", out var label) && label.ValueKind == JsonValueKind.String
                && string.Equals(label.GetString(), choice.Choice, StringComparison.Ordinal),
            _ => false,
        };
    }

    private static bool MatchesNumber(JsonElement actual, string name, double expected, double tolerance) =>
        actual.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out var number) && Math.Abs(number - expected) <= tolerance;
}