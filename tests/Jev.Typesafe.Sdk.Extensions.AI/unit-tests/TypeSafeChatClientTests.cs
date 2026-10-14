using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Jev.Typesafe.Sdk.Extensions.AI.UnitTests;

public sealed class TypeSafeChatClientTests
{
    private const string ResponseJson = """
        {"model":"jev-test","answers":{"billing":{"type":"noul","noul":0.9},"tone":{"type":"choice","choice":"positive","confidence":0.8,"probabilities":{"positive":0.8,"negative":0.2}},"severity":{"type":"score","score":1.5,"confidence":0.7,"legend":{"1":"low","2":"high"},"probabilities":{"1":0.5,"2":0.5}}},"usage":{"input_tokens":10,"output_tokens":8}}
        """;

    [Fact]
    public async Task Chat_maps_messages_questions_model_and_mixed_answers()
    {
        using var http = CreateHttp(async request =>
        {
            request.RequestUri!.PathAndQuery.ShouldBe("/v1/systemone");
            var body = await request.Content!.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("model").GetString().ShouldBe("jev-request");
            var state = body.GetProperty("state");
            state.GetArrayLength().ShouldBe(2);
            state[0].GetProperty("role").GetString().ShouldBe("system");
            state[0].GetProperty("content").GetString().ShouldBe("Evaluate this ticket.");
            state[1].GetProperty("content").GetString().ShouldBe("I was charged twice.");
            body.GetProperty("questions").GetProperty("billing").GetProperty("type").GetString().ShouldBe("noul");
        });
        using var core = CreateCore(http);
        using var chat = core.AsChatClient(Questions());

        var response = await chat.GetResponseAsync(
            [new(ChatRole.System, "Evaluate this ticket."), new(ChatRole.User, "I was charged twice.")],
            new ChatOptions { ModelId = "jev-request", ResponseFormat = ChatResponseFormat.Json },
            TestContext.Current.CancellationToken
        );

        response.ModelId.ShouldBe("jev-test");
        response.FinishReason.ShouldBe(ChatFinishReason.Stop);
        response.Messages.Single().Role.ShouldBe(ChatRole.Assistant);
        response.Usage!.InputTokenCount.ShouldBe(10);
        response.Usage.OutputTokenCount.ShouldBe(8);
        response.Usage.TotalTokenCount.ShouldBe(18);
        var raw = response.RawRepresentation.ShouldBeOfType<SystemOneResponse>();
        raw.Nouls["billing"].Noul.ShouldBe(0.9);
        using var answers = JsonDocument.Parse(response.Text);
        answers.RootElement.GetProperty("billing").GetProperty("noul").GetDouble().ShouldBe(0.9);
        answers.RootElement.GetProperty("tone").GetProperty("choice").GetString().ShouldBe("positive");
        answers.RootElement.GetProperty("tone").GetProperty("probabilities").GetProperty("negative").GetDouble().ShouldBe(0.2);
        answers.RootElement.GetProperty("severity").GetProperty("score").GetDouble().ShouldBe(1.5);
    }

    [Fact]
    public async Task Plain_json_format_survives_options_serialization()
    {
        using var http = CreateHttp();
        using var core = CreateCore(http);
        using var chat = core.AsChatClient(Questions());
        var options = JsonSerializer.Deserialize<ChatOptions>(JsonSerializer.Serialize(
            new ChatOptions { ResponseFormat = ChatResponseFormat.Json }
        ));
        var response = await chat.GetResponseAsync([new(ChatRole.User, "ticket")], options,
            TestContext.Current.CancellationToken);
        using var answers = JsonDocument.Parse(response.Text);
        answers.RootElement.GetProperty("tone").GetProperty("choice").GetString().ShouldBe("positive");
    }

    [Fact]
    public async Task Per_request_options_override_state_and_questions()
    {
        using var http = CreateHttp(async request =>
        {
            var body = await request.Content!.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("state").GetProperty("ticket").GetString().ShouldBe("urgent");
            body.GetProperty("questions").EnumerateObject().Select(property => property.Name).ShouldBe(["urgent"]);
            body.GetProperty("questions").GetProperty("urgent").GetProperty("instructions").GetString().ShouldBe("Is this urgent?");
        });
        using var core = CreateCore(http);
        using var chat = core.AsChatClient(Questions());
        await chat.GetResponseAsync([new(ChatRole.User, "context")], new TypeSafeChatOptions
        {
            State = new { ticket = "urgent" },
            Questions = new Dictionary<string, TypeSafeQuestion> { ["urgent"] = new Noul { Instructions = "Is this urgent?" } },
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Streaming_emits_complete_answer_and_usage_updates()
    {
        using var http = CreateHttp();
        using var core = CreateCore(http);
        using var chat = core.AsChatClient(Questions());
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in chat.GetStreamingResponseAsync(
            [new(ChatRole.User, "ticket")], cancellationToken: TestContext.Current.CancellationToken))
        {
            updates.Add(update);
        }

        updates.Count.ShouldBe(2);
        var content = updates[0].Contents.Single().ShouldBeOfType<TextContent>();
        using var answers = JsonDocument.Parse(content.Text!);
        answers.RootElement.GetProperty("billing").GetProperty("noul").GetDouble().ShouldBe(0.9);
        updates[0].ModelId.ShouldBe("jev-test");
        updates[0].FinishReason.ShouldBe(ChatFinishReason.Stop);
        updates[1].Contents.Single().ShouldBeOfType<UsageContent>().Details.TotalTokenCount.ShouldBe(18);
    }

    [Theory]
    [InlineData("temperature")]
    [InlineData("tokens")]
    [InlineData("tools")]
    [InlineData("instructions")]
    [InlineData("conversation")]
    [InlineData("background")]
    [InlineData("text")]
    [InlineData("schema")]
    [InlineData("properties")]
    public async Task Unsupported_options_fail_before_http(string option)
    {
        using var http = CreateHttp(_ => throw new InvalidOperationException("HTTP must not be called."));
        using var core = CreateCore(http);
        using var chat = core.AsChatClient(Questions());
        var options = option switch
        {
            "temperature" => new ChatOptions { Temperature = 0.5f },
            "tokens" => new ChatOptions { MaxOutputTokens = 100 },
            "tools" => new ChatOptions { ToolMode = ChatToolMode.Auto },
            "instructions" => new ChatOptions { Instructions = "override" },
            "conversation" => new ChatOptions { ConversationId = "stored" },
            "background" => new ChatOptions { AllowBackgroundResponses = true },
            "text" => new ChatOptions { ResponseFormat = ChatResponseFormat.Text },
            "schema" => new ChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(JsonSerializer.SerializeToElement(new { })) },
            _ => new ChatOptions { AdditionalProperties = new() { ["unsupported"] = true } },
        };
        await Should.ThrowAsync<NotSupportedException>(() => chat.GetResponseAsync([new(ChatRole.User, "ticket")], options));
    }

    [Fact]
    public async Task Unsupported_content_and_tool_roles_fail_before_http()
    {
        using var http = CreateHttp(_ => throw new InvalidOperationException("HTTP must not be called."));
        using var core = CreateCore(http);
        using var chat = core.AsChatClient(Questions());
        await Should.ThrowAsync<NotSupportedException>(() => chat.GetResponseAsync(
            [new(ChatRole.User, [new UriContent(new Uri("https://example.com/image.png"), "image/png")])]));
        await Should.ThrowAsync<NotSupportedException>(() => chat.GetResponseAsync([new(ChatRole.Tool, "result")]));
    }

    [Fact]
    public async Task Cancellation_is_forwarded_to_http()
    {
        using var cancellation = new CancellationTokenSource();
        using var http = new HttpClient(new StubHandler(async (_, token) =>
        {
            cancellation.Cancel();
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        using var core = CreateCore(http);
        using var chat = core.AsChatClient(Questions());
        await Should.ThrowAsync<OperationCanceledException>(() => chat.GetResponseAsync(
            [new(ChatRole.User, "ticket")], cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task Api_errors_preserve_status_and_body()
    {
        using var http = new HttpClient(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("invalid question"),
        })));
        using var core = CreateCore(http);
        using var chat = core.AsChatClient(Questions());
        var error = await Should.ThrowAsync<TypeSafeApiException>(() => chat.GetResponseAsync([new(ChatRole.User, "ticket")]));
        error.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        error.ResponseBody.ShouldBe("invalid question");
    }

    [Fact]
    public async Task Disposing_adapter_leaves_borrowed_core_usable()
    {
        using var http = CreateHttp();
        using var core = CreateCore(http);
        var chat = core.AsChatClient(Questions());
        chat.GetService(typeof(TypeSafeClient)).ShouldBeSameAs(core);
        chat.GetService(typeof(IChatClient)).ShouldBeSameAs(chat);
        chat.GetService(typeof(ChatClientMetadata)).ShouldBeOfType<ChatClientMetadata>().ProviderName.ShouldBe("typesafe");
        chat.GetService(typeof(TypeSafeClient), "key").ShouldBeNull();
        chat.GetService(typeof(string)).ShouldBeNull();
        chat.Dispose();
        chat.Dispose();
        await Should.ThrowAsync<ObjectDisposedException>(() => chat.GetResponseAsync([new(ChatRole.User, "ticket")]));
        var response = await core.SystemOneAsync("ticket", Questions(), cancellationToken: TestContext.Current.CancellationToken);
        response.Model.ShouldBe("jev-test");
    }

    [Fact]
    public async Task Questions_are_snapshotted_and_invalid_inputs_rejected()
    {
        using var http = CreateHttp(async request =>
        {
            var body = await request.Content!.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("questions").EnumerateObject().Count().ShouldBe(1);
        });
        using var core = CreateCore(http);
        var questions = Questions();
        using var chat = core.AsChatClient(questions);
        questions.Clear();
        await chat.GetResponseAsync([new(ChatRole.User, "ticket")], cancellationToken: TestContext.Current.CancellationToken);
        Should.Throw<ArgumentException>(() => core.AsChatClient(questions));
        Should.Throw<ArgumentNullException>(() => core.AsChatClient(null!));
        Should.Throw<ArgumentNullException>(() => new TypeSafeChatClient(null!, Questions()));
        await Should.ThrowAsync<ArgumentException>(() => chat.GetResponseAsync([]));
        await Should.ThrowAsync<ArgumentNullException>(() => chat.GetResponseAsync(null!));
    }

    [Fact]
    public void Clone_preserves_custom_options_without_sharing_question_dictionary()
    {
        var questions = Questions();
        var state = new { ticket = "urgent" };
        var original = new TypeSafeChatOptions { State = state, Questions = questions, ModelId = "jev-test" };
        var clone = original.Clone().ShouldBeOfType<TypeSafeChatOptions>();
        questions.Clear();
        clone.State.ShouldBeSameAs(state);
        clone.ModelId.ShouldBe("jev-test");
        clone.Questions!.Count.ShouldBe(1);
        clone.Questions["billing"].ShouldBeOfType<Noul>().Instructions.ShouldBe("Is this about billing?");
    }

    private static Dictionary<string, TypeSafeQuestion> Questions() =>
        new() { ["billing"] = new Noul { Instructions = "Is this about billing?" } };

    private static TypeSafeClient CreateCore(HttpClient http) =>
        new(http, new TypeSafeClientOptions { ApiKey = "test-key", MaxRetries = 0 });

    private static HttpClient CreateHttp(Func<HttpRequestMessage, Task>? inspect = null) =>
        new(new StubHandler(async (request, _) =>
        {
            if (inspect is not null)
            {
                await inspect(request);
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ResponseJson) };
        }));

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}