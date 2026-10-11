using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Typesafe.Sdk;

namespace Typesafe.Sdk.UnitTests;

public sealed class TypeSafeClientTests
{
    [Fact]
    public async Task SystemOne_serializes_mixed_questions_and_deserializes_answers()
    {
        using var client = CreateClient(
            """
            {"model":"jev-1.13.0","answers":{"billing":{"type":"noul","noul":0.93},"tone":{"type":"choice","choice":"frustrated","confidence":0.8,"probabilities":{"calm":0.2,"frustrated":0.8}},"severity":{"type":"score","score":1.4,"confidence":0.6,"legend":{"0":"low","1":"medium","2":"high"},"probabilities":{"0":0.1,"1":0.4,"2":0.5}}},"usage":{"input_tokens":10,"output_tokens":8}}
            """,
            async request =>
            {
                request.Method.ShouldBe(HttpMethod.Post);
                request.RequestUri!.PathAndQuery.ShouldBe("/v1/systemone");
                request.Headers.Authorization!.Scheme.ShouldBe("Bearer");
                request.Headers.Authorization.Parameter.ShouldBe("test-key");
                var body = await request.Content!.ReadFromJsonAsync<JsonElement>();
                body.GetProperty("model").GetString().ShouldBe("jev-latest");
                body.GetProperty("questions")
                    .GetProperty("billing")
                    .GetProperty("type")
                    .GetString()
                    .ShouldBe("noul");
                body.GetProperty("questions")
                    .GetProperty("severity")
                    .GetProperty("criteria")
                    .GetArrayLength()
                    .ShouldBe(3);
            }
        );

        using var typesafe = new TypeSafeClient(
            client,
            new TypeSafeClientOptions { ApiKey = "test-key" }
        );
        var response = await typesafe.SystemOneAsync(
            "I was charged twice.",
            new Dictionary<string, TypeSafeQuestion>
            {
                ["billing"] = new Noul { Instructions = "Is this about billing?" },
                ["tone"] = new Choice
                {
                    Instructions = "What is the tone?",
                    Criteria = new Dictionary<string, object?>
                    {
                        ["calm"] = null,
                        ["frustrated"] = null,
                    },
                },
                ["severity"] = new Score
                {
                    Instructions = "How severe?",
                    Criteria = ["low", "medium", "high"],
                },
            },
            cancellationToken: TestContext.Current.CancellationToken
        );

        response.Nouls["billing"].Noul.ShouldBe(0.93);
        response.Choices["tone"].Choice.ShouldBe("frustrated");
        response.Scores["severity"].Probabilities[2].ShouldBe(0.5);
    }

    [Fact]
    public async Task ListModels_uses_models_endpoint()
    {
        using var client = CreateClient(
            "{\"models\":[{\"name\":\"jev-latest\",\"description\":\"Current Jev model\",\"release_date\":\"2026-01-01\"}]}",
            request =>
            {
                request.Method.ShouldBe(HttpMethod.Get);
                request.RequestUri!.PathAndQuery.ShouldBe("/v1/models");
                return Task.CompletedTask;
            }
        );

        using var typesafe = new TypeSafeClient(
            client,
            new TypeSafeClientOptions { ApiKey = "test-key" }
        );
        var models = await typesafe.ListModelsAsync(TestContext.Current.CancellationToken);

        models.Single().Name.ShouldBe("jev-latest");
    }

    private static HttpClient CreateClient(
        string response,
        Func<HttpRequestMessage, Task> onRequest
    ) => new(new StubHandler(response, onRequest));

    private sealed class StubHandler(string response, Func<HttpRequestMessage, Task> onRequest)
        : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            await onRequest(request);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response),
            };
        }
    }
}
