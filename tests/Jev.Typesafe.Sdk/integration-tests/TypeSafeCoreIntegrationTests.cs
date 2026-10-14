namespace Jev.Typesafe.Sdk.IntegrationTests;

public sealed class TypeSafeCoreIntegrationTests : TypeSafeIntegrationTestBase
{
    public static new bool LiveTestsEnabled => TypeSafeIntegrationTestBase.LiveTestsEnabled;

    [Fact(
        Skip = "Provide TYPESAFE_API_KEY in the environment to run live tests.",
        SkipUnless = nameof(LiveTestsEnabled)
    )]
    public async Task ListModels_returns_live_models()
    {
        var (client, cancellationToken) = CreateLiveClient();
        using (client)
        {
            var models = await client.ListModelsAsync(cancellationToken);
            models.ShouldNotBeEmpty();
            models.ShouldContain(model => !string.IsNullOrWhiteSpace(model.Name));
        }
    }

    [Fact(
        Skip = "Provide TYPESAFE_API_KEY in the environment to run live tests.",
        SkipUnless = nameof(LiveTestsEnabled)
    )]
    public async Task SystemOne_returns_live_typed_answers()
    {
        var (client, cancellationToken) = CreateLiveClient();
        using (client)
        {
            var response = await client.SystemOneAsync(
                "I was charged twice and need help urgently.",
                new Dictionary<string, TypeSafeQuestion>
                {
                    ["billing"] = new Noul { Instructions = "Is this about billing?" },
                    ["urgency"] = new Score
                    {
                        Instructions = "How urgent is this ticket?",
                        Criteria = ["can wait", "this week", "today"],
                    },
                },
                cancellationToken: cancellationToken
            );

            response.Answers.ShouldContainKey("billing");
            response.Answers.ShouldContainKey("urgency");
            response.Nouls["billing"].Noul.ShouldBeInRange(0, 1);
            response.Scores["urgency"].Score.ShouldBeInRange(0, 2);
        }
    }
}
