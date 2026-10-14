using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Jev.Typesafe.Sdk.Extensions.AI.Evaluation;

namespace Jev.Typesafe.Sdk.Extensions.AI.IntegrationTests;

public sealed class TypeSafeChatIntegrationTests
{
    public static bool LiveTestsEnabled =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TYPESAFE_API_KEY"));

    [Fact(
        Skip = "Provide TYPESAFE_API_KEY in the environment to run live tests.",
        SkipUnless = nameof(LiveTestsEnabled)
    )]
    public async Task Chat_returns_live_choice_and_evaluates_expected_label()
    {
        using var core = new TypeSafeClient();
        using var chat = core.AsChatClient(new Dictionary<string, TypeSafeQuestion>
        {
            ["sentiment"] = new Choice
            {
                Instructions = "Classify the sentiment of the user's message.",
                Criteria = new Dictionary<string, object?> { ["positive"] = null, ["negative"] = null },
            },
        });
        ChatMessage[] messages = [new(ChatRole.User, "I love this product. It works perfectly!")];
        var response = await chat.GetResponseAsync(messages, cancellationToken: TestContext.Current.CancellationToken);
        var raw = response.RawRepresentation.ShouldBeOfType<SystemOneResponse>();
        raw.Choices["sentiment"].Choice.ShouldBe("positive");
        var result = await new TypeSafeAnswerEvaluator().EvaluateAsync(messages, response, additionalContext:
        [
            new TypeSafeAnswerEvaluationContext(new Dictionary<string, TypeSafeAnswer>
            {
                ["sentiment"] = new ChoiceAnswer { Type = "choice", Choice = "positive" },
            }),
        ], cancellationToken: TestContext.Current.CancellationToken);
        result.Get<NumericMetric>(TypeSafeAnswerEvaluator.AccuracyMetricName).Value.ShouldBe(1);
    }
}