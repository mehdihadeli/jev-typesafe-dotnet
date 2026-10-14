using Jev.Typesafe.Sdk;

namespace Jev.Typesafe.Sdk.UnitTests;

[CollectionDefinition("Process environment", DisableParallelization = true)]
public sealed class ProcessEnvironmentCollection;

[Collection("Process environment")]
public sealed class ConfigTests
{
    private static readonly string[] Names = ["TYPESAFE_API_KEY", "TYPESAFE_BASE_URL", "TYPESAFE_DEFAULT_MODEL", "TYPESAFE_LOG_LEVEL"];

    internal static async Task WithEnvironment(Func<Task> action, params string?[] values)
    {
        var saved = Names.Select(Environment.GetEnvironmentVariable).ToArray();
        try
        {
            for (var index = 0; index < Names.Length; index++) Environment.SetEnvironmentVariable(Names[index], values.ElementAtOrDefault(index));
            await action();
        }
        finally
        {
            for (var index = 0; index < Names.Length; index++) Environment.SetEnvironmentVariable(Names[index], saved[index]);
        }
    }

    [Theory]
    [InlineData("default", "test-key", "https://api.typesafe.ai/v1/systemone", "jev-latest")]
    [InlineData("env", "env-key", "https://env.test/prefix/v1/systemone", "env-model")]
    [InlineData("constructor", "code-key", "https://code.test/prefix/v1/systemone", "code-model")]
    public Task Resolution_ExplicitEnvironmentDefault_Precedence(string source, string key, string url, string model) => WithEnvironment(async () =>
    {
        var handler = ParityTestHelper.Handler();
        using var client = new TypeSafeClient(new TypeSafeClientOptions
        {
            ApiKey = source == "env" ? null : source == "constructor" ? "code-key" : "test-key",
            BaseUrl = source == "constructor" ? "https://code.test/prefix///" : null,
            Model = source == "constructor" ? "code-model" : null,
            Handler = handler, Retry = new RetryPolicy { MaxRetries = 0 },
        });
        await client.SystemOneAsync("hello", ParityTestHelper.Questions());
        handler.Requests.Count.ShouldBe(1);
        handler.Requests[0].Url.ShouldBe(url);
        handler.Requests[0].Headers["Authorization"].ShouldBe("Bearer " + key);
        handler.Requests[0].Json["model"]!.GetValue<string>().ShouldBe(model);
    }, source == "default" ? null : "  env-key  ", source == "default" ? null : "  https://env.test/prefix///  ", source == "default" ? null : "  env-model  ");

    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\t")]
    [InlineData("\u001f")]
    [InlineData("\u007f")]
    [InlineData(" ")]
    [InlineData("\u00e9")]
    [InlineData("\u200b")]
    public Task ApiKey_InvalidCharacters_ExactErrorWithoutCredentialLeak(string character) => WithEnvironment(() =>
    {
        foreach (var fromEnvironment in new[] { false, true })
        {
            var secret = "ts_live_private" + character + "suffix";
            Environment.SetEnvironmentVariable("TYPESAFE_API_KEY", fromEnvironment ? secret : "env-key");
            var handler = ParityTestHelper.Handler();
            var error = Should.Throw<TypeSafeException>(() => new TypeSafeClient(new TypeSafeClientOptions { ApiKey = fromEnvironment ? null : secret, Handler = handler }));
            error.GetType().ShouldBe(typeof(TypeSafeException));
            error.Message.ShouldBe("API key must contain only printable ASCII characters without whitespace.");
            error.ToString().ShouldNotContain("ts_live_private");
            handler.Requests.Count.ShouldBe(0);
            handler.DisposeCount.ShouldBe(0);
        }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("")]
    [InlineData(" \t\r\n ")]
    public Task ApiKey_ExplicitEmpty_DoesNotFallBack(string key) => WithEnvironment(() =>
    {
        var error = Should.Throw<TypeSafeException>(() => new TypeSafeClient(new TypeSafeClientOptions { ApiKey = key }));
        error.Message.ShouldBe("No API key was provided. Pass api_key or set the TYPESAFE_API_KEY environment variable.");
        return Task.CompletedTask;
    }, "env-key");

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData(" \t\r\n ")]
    public Task ApiKey_Padding_TrimmedForExplicitAndEnvironment(string padding) => WithEnvironment(async () =>
    {
        foreach (var fromEnvironment in new[] { false, true })
        {
            Environment.SetEnvironmentVariable("TYPESAFE_API_KEY", padding + "test-key" + padding);
            var handler = ParityTestHelper.Handler("{\"models\":[]}");
            using var client = new TypeSafeClient(new TypeSafeClientOptions { ApiKey = fromEnvironment ? null : padding + "test-key" + padding, Handler = handler });
            (await client.Models.ListAsync()).Models.Count.ShouldBe(0);
            handler.Requests.Single().Headers["Authorization"].ShouldBe("Bearer test-key");
        }
    });

    [Fact]
    public Task EmptyEnvironment_UsesDefaultsAndExplicitEmptyModelIsRetained() => WithEnvironment(async () =>
    {
        var handler = ParityTestHelper.Handler();
        using var client = new TypeSafeClient(new TypeSafeClientOptions { ApiKey = "test-key", Handler = handler });
        await client.SystemOneAsync("hello", ParityTestHelper.Questions());
        await client.SystemOneAsync("hello", ParityTestHelper.Questions(), new SystemOneOptions { Model = "" });
        handler.Requests[0].Url.ShouldBe("https://api.typesafe.ai/v1/systemone");
        handler.Requests[0].Json["model"]!.GetValue<string>().ShouldBe("jev-latest");
        handler.Requests[1].Json["model"]!.GetValue<string>().ShouldBe("");
    }, "test-key", " \t ", " \t ", " \t ");

    [Fact]
    public Task HandlerAndHttpClient_RejectBeforeConfigurationWithoutClosing() => WithEnvironment(() =>
    {
        using var handler = ParityTestHelper.Handler();
        using var http = new HttpClient(handler);
        var error = Should.Throw<TypeSafeException>(() => new TypeSafeClient(new TypeSafeClientOptions { Handler = handler, HttpClient = http }));
        error.Message.ShouldBe("transport and http_client are mutually exclusive.");
        handler.DisposeCount.ShouldBe(0);
        http.Timeout.ShouldBe(TimeSpan.FromSeconds(100));
        handler.Requests.Count.ShouldBe(0);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public async Task Timeout_InvalidConstructorAndCall_FailBeforeNetwork(int seconds)
    {
        var handler = ParityTestHelper.Handler();
        var error = Should.Throw<TypeSafeException>(() => new TypeSafeClient(new TypeSafeClientOptions { ApiKey = "test-key", Handler = handler, Timeout = TimeSpan.FromSeconds(seconds) }));
        error.Message.ShouldBe("timeout must be a positive, finite number of seconds.");
        using var client = ParityTestHelper.Client(handler);
        var callError = await Should.ThrowAsync<TypeSafeException>(() => client.Models.ListAsync(new RequestOptions { Timeout = TimeSpan.FromSeconds(seconds) }));
        callError.Message.ShouldBe(error.Message);
        handler.Requests.Count.ShouldBe(0);
    }
}