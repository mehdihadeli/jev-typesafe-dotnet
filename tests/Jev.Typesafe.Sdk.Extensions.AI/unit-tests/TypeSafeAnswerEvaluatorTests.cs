using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Jev.Typesafe.Sdk.Extensions.AI.Evaluation;

namespace Jev.Typesafe.Sdk.Extensions.AI.UnitTests;

public sealed class TypeSafeAnswerEvaluatorTests
{
    [Fact]
    public async Task Evaluation_scores_mixed_answers_with_numeric_tolerance()
    {
        IEvaluator evaluator = new TypeSafeAnswerEvaluator();
        var context = new TypeSafeAnswerEvaluationContext(new Dictionary<string, TypeSafeAnswer>
        {
            ["billing"] = new NoulAnswer { Type = "noul", Noul = 0.9 },
            ["tone"] = new ChoiceAnswer { Type = "choice", Choice = "positive" },
            ["severity"] = new ScoreAnswer { Type = "score", Score = 1.5 },
        }, numericTolerance: 0.02);
        var response = Response("""
            {"billing":{"type":"noul","noul":0.91},"tone":{"type":"choice","choice":"negative"},"severity":{"type":"score","score":1.49},"extra":{"type":"noul","noul":0.1}}
            """);

        var result = await evaluator.EvaluateAsync([], response, additionalContext: [context],
            cancellationToken: TestContext.Current.CancellationToken);
        evaluator.EvaluationMetricNames.ShouldBe([TypeSafeAnswerEvaluator.AccuracyMetricName]);
        result.Metrics.Count.ShouldBe(1);
        var metric = result.Get<NumericMetric>(TypeSafeAnswerEvaluator.AccuracyMetricName);
        metric.Value.ShouldBe(2.0 / 3);
        metric.Reason.ShouldBe("2 of 3 expected answers matched. Extra answers are ignored.");
    }

    [Theory]
    [InlineData(0.5, 1)]
    [InlineData(0.625, 1)]
    [InlineData(0.626, 0)]
    [InlineData(0.375, 1)]
    [InlineData(0.374, 0)]
    public async Task Numeric_tolerance_includes_boundary(double actual, double accuracy)
    {
        var context = new TypeSafeAnswerEvaluationContext(new Dictionary<string, TypeSafeAnswer>
        {
            ["answer"] = new NoulAnswer { Type = "noul", Noul = 0.5 },
        }, numericTolerance: 0.125);
        var response = Response(JsonSerializer.Serialize(new { answer = new { type = "noul", noul = actual } }));
        var result = await new TypeSafeAnswerEvaluator().EvaluateAsync([], response, additionalContext: [context],
            cancellationToken: TestContext.Current.CancellationToken);
        result.Get<NumericMetric>(TypeSafeAnswerEvaluator.AccuracyMetricName).Value.ShouldBe(accuracy);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"answer\":null}")]
    [InlineData("{\"answer\":{\"type\":\"score\",\"score\":0.5}}")]
    [InlineData("{\"answer\":{\"type\":\"noul\",\"noul\":\"0.5\"}}")]
    public async Task Missing_malformed_or_wrong_type_answers_score_zero(string json)
    {
        var context = new TypeSafeAnswerEvaluationContext(new Dictionary<string, TypeSafeAnswer>
        {
            ["answer"] = new NoulAnswer { Type = "noul", Noul = 0.5 },
        });
        var result = await new TypeSafeAnswerEvaluator().EvaluateAsync([], Response(json), additionalContext: [context],
            cancellationToken: TestContext.Current.CancellationToken);
        result.Get<NumericMetric>(TypeSafeAnswerEvaluator.AccuracyMetricName).Value.ShouldBe(0);
    }

    [Theory]
    [InlineData("positive", 1)]
    [InlineData("Positive", 0)]
    [InlineData("negative", 0)]
    public async Task Choice_matching_is_ordinal(string label, double accuracy)
    {
        var context = new TypeSafeAnswerEvaluationContext(new Dictionary<string, TypeSafeAnswer>
        {
            ["answer"] = new ChoiceAnswer { Type = "choice", Choice = "positive" },
        });
        var response = Response(JsonSerializer.Serialize(new { answer = new { type = "choice", choice = label } }));
        var result = await new TypeSafeAnswerEvaluator().EvaluateAsync([], response, additionalContext: [context],
            cancellationToken: TestContext.Current.CancellationToken);
        result.Get<NumericMetric>(TypeSafeAnswerEvaluator.AccuracyMetricName).Value.ShouldBe(accuracy);
    }

    [Fact]
    public async Task Evaluation_requires_one_context_and_valid_json_object()
    {
        var evaluator = new TypeSafeAnswerEvaluator();
        var context = new TypeSafeAnswerEvaluationContext(Expected());
        await Should.ThrowAsync<ArgumentException>(async () => await evaluator.EvaluateAsync([], Response("{}")));
        await Should.ThrowAsync<ArgumentException>(async () => await evaluator.EvaluateAsync([], Response("{}"), additionalContext: [context, context]));
        await Should.ThrowAsync<JsonException>(async () => await evaluator.EvaluateAsync([], Response("not json"), additionalContext: [context]));
        await Should.ThrowAsync<JsonException>(async () => await evaluator.EvaluateAsync([], Response("[]"), additionalContext: [context]));
        await Should.ThrowAsync<ArgumentNullException>(async () => await evaluator.EvaluateAsync(null!, Response("{}"), additionalContext: [context]));
        await Should.ThrowAsync<ArgumentNullException>(async () => await evaluator.EvaluateAsync([], null!, additionalContext: [context]));
    }

    [Fact]
    public async Task Evaluation_honors_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(async () => await new TypeSafeAnswerEvaluator().EvaluateAsync(
            [], Response("{}"), additionalContext: [new TypeSafeAnswerEvaluationContext(Expected())], cancellationToken: cancellation.Token));
    }

    [Fact]
    public void Context_snapshots_answers_and_records_serializable_ground_truth()
    {
        var expected = Expected();
        var context = new TypeSafeAnswerEvaluationContext(expected, 0.125);
        expected.Clear();
        context.ExpectedAnswers.Count.ShouldBe(1);
        var text = context.Contents.Single().ShouldBeOfType<TextContent>();
        using var document = JsonDocument.Parse(text.Text!);
        document.RootElement.GetProperty("expectedAnswers").GetProperty("answer").GetProperty("noul").GetDouble().ShouldBe(0.5);
        document.RootElement.GetProperty("numericTolerance").GetDouble().ShouldBe(0.125);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Invalid_tolerance_is_rejected(double tolerance) =>
        Should.Throw<ArgumentOutOfRangeException>(() => new TypeSafeAnswerEvaluationContext(Expected(), tolerance));

    [Fact]
    public void Empty_or_null_expected_answers_are_rejected()
    {
        Should.Throw<ArgumentException>(() => new TypeSafeAnswerEvaluationContext(new Dictionary<string, TypeSafeAnswer>()));
        Should.Throw<ArgumentNullException>(() => new TypeSafeAnswerEvaluationContext(null!));
    }

    private static Dictionary<string, TypeSafeAnswer> Expected() =>
        new() { ["answer"] = new NoulAnswer { Type = "noul", Noul = 0.5 } };

    private static ChatResponse Response(string json) => new(new ChatMessage(ChatRole.Assistant, json));
}