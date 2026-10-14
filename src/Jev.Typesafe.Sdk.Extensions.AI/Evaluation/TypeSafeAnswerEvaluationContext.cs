using System.Collections.ObjectModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace Jev.Typesafe.Sdk.Extensions.AI.Evaluation;

public sealed class TypeSafeAnswerEvaluationContext : EvaluationContext
{
    public IReadOnlyDictionary<string, TypeSafeAnswer> ExpectedAnswers { get; }

    public double NumericTolerance { get; }

    public TypeSafeAnswerEvaluationContext(
        IReadOnlyDictionary<string, TypeSafeAnswer> expectedAnswers,
        double numericTolerance = 0.01
    ) : base(nameof(TypeSafeAnswerEvaluationContext), Array.Empty<AIContent>())
    {
        if (expectedAnswers is null)
        {
            throw new ArgumentNullException(nameof(expectedAnswers));
        }
        if (expectedAnswers.Count == 0 || expectedAnswers.Any(pair => string.IsNullOrWhiteSpace(pair.Key)
            || pair.Value is not (NoulAnswer or ChoiceAnswer or ScoreAnswer)))
        {
            throw new ArgumentException("Provide at least one named Noul, Choice, or Score answer.", nameof(expectedAnswers));
        }
        if (double.IsNaN(numericTolerance) || double.IsInfinity(numericTolerance) || numericTolerance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(numericTolerance));
        }
        ExpectedAnswers = new ReadOnlyDictionary<string, TypeSafeAnswer>(
            expectedAnswers.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
        );
        NumericTolerance = numericTolerance;
        using var answers = JsonDocument.Parse(TypeSafeChatSerialization.SerializeAnswers(ExpectedAnswers));
        Contents.Add(new TextContent(JsonSerializer.Serialize(new
        {
            expectedAnswers = answers.RootElement,
            numericTolerance,
        })));
    }
}