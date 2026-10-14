using System.Text.Json.Nodes;
using Jev.Typesafe.Sdk;

namespace Jev.Typesafe.Sdk.UnitTests;

public sealed class ClientsTests
{
    [Fact]
    public async Task Headers_SuppliedRetryDefault_IsSuppressedAndReplacedOnRetry()
    {
        var handler = new ParityHandler((index, _, _) => Task.FromResult(index == 0
            ? ParityTestHelper.Response("{\"message\":\"busy\"}", 503)
            : ParityTestHelper.Response("{\"models\":[]}")));
        using var http = new HttpClient(handler);
        http.DefaultRequestHeaders.TryAddWithoutValidation("X-TypeSafe-Retry-Count", "99");
        using var client = new TypeSafeClient(new TypeSafeClientOptions
        {
            ApiKey = "test-key", HttpClient = http, LeaveOpen = true,
            Retry = new RetryPolicy { MaxRetries = 1, BackoffInitial = TimeSpan.Zero },
            DelayAsync = (_, _) => Task.CompletedTask,
        });
        await client.Models.ListAsync(cancellationToken: TestContext.Current.CancellationToken);
        handler.Requests.Count.ShouldBe(2);
        handler.Requests[0].Headers.GetValueOrDefault("X-TypeSafe-Retry-Count").ShouldBeNullOrEmpty();
        handler.Requests[1].Headers["X-TypeSafe-Retry-Count"].ShouldBe("1");
        http.DefaultRequestHeaders.GetValues("X-TypeSafe-Retry-Count").ShouldBe(new[] { "99" });
    }

    [Fact]
    public async Task Headers_ProtectedValuesWinAndSuppliedClientRemainsUnchanged()
    {
        var handler = new ParityHandler((_, request, _) => Task.FromResult(ParityTestHelper.Response(request.Method == HttpMethod.Get ? "{\"models\":[]}" : ParityTestHelper.Result)));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://wrong.test"), Timeout = TimeSpan.FromSeconds(17) };
        var protectedHeaders = new Dictionary<string, string>
        {
            ["authorization"] = "wrong", ["accept"] = "text/plain", ["user-agent"] = "wrong", ["x-typesafe-sdk"] = "wrong", ["x-typesafe-runtime"] = "wrong", ["X-TypeSafe-Retry-Count"] = "99",
        };
        foreach (var pair in protectedHeaders) http.DefaultRequestHeaders.TryAddWithoutValidation(pair.Key, pair.Value);
        http.DefaultRequestHeaders.TryAddWithoutValidation("x-http-default", "kept");
        http.DefaultRequestHeaders.TryAddWithoutValidation("x-team", "http");
        var originalHeaders = http.DefaultRequestHeaders.ToString();
        using var client = new TypeSafeClient(new TypeSafeClientOptions
        {
            ApiKey = "test-key", BaseUrl = "https://example.test/prefix///", HttpClient = http, LeaveOpen = true,
            Headers = new Dictionary<string, string>(protectedHeaders) { ["X-Team"] = "sdk", ["x-sdk-default"] = "kept" },
        });
        var extras = new Dictionary<string, string>(protectedHeaders) { ["x-team"] = "call", ["content-type"] = "wrong" };
        var resource = client.Models;
        resource.ShouldBeSameAs(client.Models);
        var models = await resource.ListAsync(new RequestOptions { ExtraHeaders = extras }, TestContext.Current.CancellationToken);
        models.Models.Count.ShouldBe(0);
        models.RequestId.ShouldBe("req-test");
        await client.SystemOneAsync("x", ParityTestHelper.Questions(), new SystemOneOptions { ExtraHeaders = extras }, TestContext.Current.CancellationToken);
        handler.Requests.Count.ShouldBe(2);
        handler.Requests[0].Method.ShouldBe(HttpMethod.Get);
        handler.Requests[0].Body.Length.ShouldBe(0);
        handler.Requests[0].Url.ShouldBe("https://example.test/prefix/v1/models");
        handler.Requests[1].Url.ShouldBe("https://example.test/prefix/v1/systemone");
        handler.Requests[1].Headers["Content-Type"].ShouldBe("application/json");
        foreach (var request in handler.Requests)
        {
            request.Headers["Authorization"].ShouldBe("Bearer test-key");
            request.Headers["Accept"].ShouldBe("application/json");
            request.Headers["User-Agent"].ShouldStartWith("jev-typesafe-dotnet/");
            request.Headers["X-TypeSafe-SDK"].ShouldBe(request.Headers["User-Agent"]);
            request.Headers["X-TypeSafe-Runtime"].ShouldStartWith("dotnet/");
            request.Headers["x-http-default"].ShouldBe("kept");
            request.Headers["x-sdk-default"].ShouldBe("kept");
            request.Headers["x-team"].ShouldBe("call");
            request.Headers.GetValueOrDefault("X-TypeSafe-Retry-Count").ShouldBeNullOrEmpty();
        }
        http.DefaultRequestHeaders.ToString().ShouldBe(originalHeaders);
        http.BaseAddress.ShouldBe(new Uri("https://wrong.test"));
        http.Timeout.ShouldBe(TimeSpan.FromSeconds(17));
        extras["authorization"].ShouldBe("wrong");
        extras["x-team"].ShouldBe("call");
    }

    [Fact]
    public async Task ExtraBody_AllCoreKeysLastWinShallowly_AndOriginalValidationRunsFirst()
    {
        var handler = ParityTestHelper.Handler();
        using var client = ParityTestHelper.Client(handler);
        var replacement = new JsonObject { ["only"] = new JsonArray("replacement", null) };
        var extra = new Dictionary<string, object?> { ["state"] = replacement, ["model"] = "override-model", ["questions"] = null, ["beam_width"] = 4, ["nullable"] = null };
        var options = new SystemOneOptions { Model = "call-model", ExtraBody = extra };
        await client.SystemOneAsync(new { old = "not deeply merged" }, ParityTestHelper.Questions(), options, TestContext.Current.CancellationToken);
        JsonNode.DeepEquals(handler.Requests.Single().Json, JsonNode.Parse("{\"state\":{\"only\":[\"replacement\",null]},\"model\":\"override-model\",\"questions\":null,\"beam_width\":4,\"nullable\":null}")).ShouldBeTrue();
        replacement["only"]!.AsArray().Count.ShouldBe(2);
        extra.Count.ShouldBe(5);
        var state = await Should.ThrowAsync<TypeSafeException>(() => client.SystemOneAsync(null!, ParityTestHelper.Questions(), options));
        state.Message.ShouldBe("State is required.");
        var questions = await Should.ThrowAsync<TypeSafeException>(() => client.SystemOneAsync("x", new Dictionary<string, TypeSafeQuestion>(), options));
        questions.Message.ShouldBe("At least one question is required.");
        handler.Requests.Count.ShouldBe(1);
        await client.SystemOneAsync("next", ParityTestHelper.Questions(), cancellationToken: TestContext.Current.CancellationToken);
        handler.Requests.Last().Json["state"]!.GetValue<string>().ShouldBe("next");
        handler.Requests.Last().Json["model"]!.GetValue<string>().ShouldBe("jev-latest");
        handler.Requests.Last().Json.ContainsKey("beam_width").ShouldBeFalse();
        handler.Requests.Last().Json["questions"]!["q"]!["type"]!.GetValue<string>().ShouldBe("noul");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetryOverride_StatusCountAndBudget_ResetOnSubsequentCalls(bool models)
    {
        var now = 0.0;
        var handler = new ParityHandler((_, request, _) =>
        {
            now += 20;
            return Task.FromResult(ParityTestHelper.Response("{}", request.Headers.GetValueOrDefault("x-call") == "override" ? 409 : 429, null, new Dictionary<string, string> { ["retry-after-ms"] = "0" }));
        });
        using var client = new TypeSafeClient(new TypeSafeClientOptions { ApiKey = "test", Handler = handler, Clock = () => now, DelayAsync = (_, _) => Task.CompletedTask });
        foreach (var (policy, count, overridden) in new (RetryPolicy?, int, bool)[]
        {
            (null, 2, false), (new RetryPolicy { Timeout = TimeSpan.FromSeconds(1), HttpStatuses = new[] { 409 } }, 1, true),
            (new RetryPolicy { Timeout = null, HttpStatuses = new[] { 409 }, MaxRetries = 4 }, 5, true), (null, 2, false),
        })
        {
            handler.Requests.Clear();
            var headers = new Dictionary<string, string> { ["x-call"] = overridden ? "override" : "inherited" };
            var error = await Should.ThrowAsync<TypeSafeApiException>(async () =>
            {
                if (models) await client.Models.ListAsync(new RequestOptions { Retry = policy, ExtraHeaders = headers });
                else await client.SystemOneAsync("x", ParityTestHelper.Questions(), new SystemOneOptions { Retry = policy, ExtraHeaders = headers });
            });
            error.Status.ShouldBe(overridden ? 409 : 429);
            handler.Requests.Count.ShouldBe(count);
            handler.Requests.Select(request => request.Headers.GetValueOrDefault("X-TypeSafe-Retry-Count")).ShouldBe(Enumerable.Range(0, count).Select(index => index == 0 ? null : index.ToString()));
        }
    }

    [Theory]
    [InlineData(false, null, null, 17.0)]
    [InlineData(false, 3.0, null, 3.0)]
    [InlineData(false, null, 2.0, 2.0)]
    [InlineData(false, 3.0, -1.0, -0.001)]
    [InlineData(true, null, null, -0.001)]
    [InlineData(true, 3.0, null, 3.0)]
    [InlineData(true, null, 2.0, 2.0)]
    [InlineData(true, 3.0, -1.0, -0.001)]
    public async Task Timeout_SuppliedClientConstructorAndCallPrecedence_NoMutation(bool unlimited, double? sdk, double? call, double expected)
    {
        var handler = new ParityHandler((_, _, _) => throw new TaskCanceledException("deterministic timeout"));
        using var http = new HttpClient(handler) { Timeout = unlimited ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(17) };
        using var client = new TypeSafeClient(new TypeSafeClientOptions { ApiKey = "test", HttpClient = http, LeaveOpen = true, Timeout = sdk is null ? null : TimeSpan.FromSeconds(sdk.Value), Retry = new RetryPolicy { MaxRetries = 0 } });
        var first = await Should.ThrowAsync<TypeSafeApiTimeoutException>(() => client.Models.ListAsync(new RequestOptions { Timeout = call is null ? null : call < 0 ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(call.Value) }));
        first.Timeout.ShouldBe(TimeSpan.FromSeconds(expected));
        first.Message.ShouldBe($"Request timed out (timeout={expected.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture)}).");
        var inherited = await Should.ThrowAsync<TypeSafeApiTimeoutException>(() => client.Models.ListAsync());
        inherited.Timeout.ShouldBe(sdk is null ? http.Timeout : TimeSpan.FromSeconds(sdk.Value));
        http.Timeout.ShouldBe(unlimited ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(17));
        handler.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task PublicApi_LegacyOptionsMixedQuestionsGenericAndModels_CompileAndExecute()
    {
        var handler = new ParityHandler((_, request, _) => Task.FromResult(ParityTestHelper.Response(request.Method == HttpMethod.Get ? "{\"models\":[{\"name\":\"legacy\",\"description\":\"Legacy\",\"release_date\":\"any\"}]}" : ParityTestHelper.Result)));
        using var http = new HttpClient(handler);
        await using var client = new TypeSafeClient(http, new TypeSafeClientOptions { ApiKey = "test", DefaultModel = "legacy-model", MaxRetries = 0, RetryDelay = TimeSpan.Zero, LeaveOpen = true });
        var legacy = await client.SystemOneAsync("x", ParityTestHelper.Questions(), "legacy-call", TestContext.Current.CancellationToken);
        legacy.Nouls["n"].Noul.ShouldBe(0.93);
        var modern = await client.SystemOneAsync<SystemOneResponse>("x", new Dictionary<string, Question> { ["q"] = new Noul("?") }, new SystemOneOptions { Model = "modern-call" }, TestContext.Current.CancellationToken);
        modern.Scores["s"].Score.ShouldBe(1.4);
        var models = await client.ListModelsAsync(TestContext.Current.CancellationToken);
        models.Single().Name.ShouldBe("legacy");
        handler.Requests[0].Json["model"]!.GetValue<string>().ShouldBe("legacy-call");
        handler.Requests[1].Json["model"]!.GetValue<string>().ShouldBe("modern-call");
        handler.Requests[2].Method.ShouldBe(HttpMethod.Get);
    }
}