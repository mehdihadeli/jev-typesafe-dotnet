using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Jev.Typesafe.Sdk;

namespace Jev.Typesafe.Sdk.UnitTests;

public sealed class RetryTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HttpResilience_HostOwnsRetriesWithoutSdkRetryMultiplication(bool succeeds)
    {
        var handler = new ParityHandler((index, _, _) => Task.FromResult(index == 1 && succeeds
            ? ParityTestHelper.Response()
            : ParityTestHelper.Response("{\"message\":\"busy\"}", 503)));
        var services = new ServiceCollection();
        services.AddHttpClient("typesafe", http => http.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 1;
                options.Retry.Delay = TimeSpan.Zero;
                options.Retry.UseJitter = false;
            });
        using var provider = services.BuildServiceProvider();
        using var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient("typesafe");
        using var client = new TypeSafeClient(new TypeSafeClientOptions
        {
            ApiKey = "test", HttpClient = http, LeaveOpen = true, Timeout = Timeout.InfiniteTimeSpan,
            Retry = new RetryPolicy { MaxRetries = 0 }, LogLevel = TypeSafeLogLevel.Off,
        });
        if (succeeds)
            (await client.SystemOneAsync("state", ParityTestHelper.Questions(),
                cancellationToken: TestContext.Current.CancellationToken)).Model.ShouldBe("jev-test");
        else
            (await Should.ThrowAsync<TypeSafeApiException>(() => client.SystemOneAsync("state", ParityTestHelper.Questions(),
                cancellationToken: TestContext.Current.CancellationToken))).Status.ShouldBe(503);
        handler.Requests.Count.ShouldBe(2);
        handler.Requests.All(request => !request.Headers.ContainsKey("X-TypeSafe-Retry-Count")).ShouldBeTrue();
    }

    [Fact]
    public void Defaults_AllElevenFieldsAndStatusRange_Exact()
    {
        var policy = new RetryPolicy();
        policy.MaxRetries.ShouldBe(2);
        policy.BackoffInitial.ShouldBe(TimeSpan.FromSeconds(0.5));
        policy.BackoffMax.ShouldBe(TimeSpan.FromSeconds(5));
        policy.BackoffJitter.ShouldBe(0.25);
        policy.HttpStatuses.Order().ShouldBe(new[] { 408, 429 }.Concat(Enumerable.Range(500, 100)));
        policy.RespectRetryAfter.ShouldBeTrue();
        policy.ApiConnectionError.ShouldBeTrue();
        policy.ApiTimeoutError.ShouldBeTrue();
        policy.Exceptions.Count.ShouldBe(0);
        policy.Predicate.ShouldBeNull();
        policy.Timeout.ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Theory]
    [InlineData(1, 0, 0.5)]
    [InlineData(2, 0, 1)]
    [InlineData(3, 0, 2)]
    [InlineData(4, 0, 4)]
    [InlineData(5, 0, 5)]
    [InlineData(2000, 0, 5)]
    [InlineData(1, 1, 0.375)]
    [InlineData(2, 1, 0.75)]
    public void Backoff_ExponentCapJitterAndRounding_Exact(int attempt, double random, double seconds) =>
        new RetryPolicy().GetDelay(attempt, new Exception(), random).ShouldBe(TimeSpan.FromSeconds(seconds));

    [Theory]
    [InlineData(0, 5, 0)]
    [InlineData(0.5, 0, 0)]
    [InlineData(0.5, 0.0006, 0.0006)]
    [InlineData(0.12345, 5, 0.123)]
    public void Backoff_ZeroAndSubmillisecondCap_NoOvershoot(double initial, double maximum, double expected) =>
        new RetryPolicy { BackoffInitial = TimeSpan.FromSeconds(initial), BackoffMax = TimeSpan.FromSeconds(maximum), BackoffJitter = 0 }
            .GetDelay(1, new Exception(), 0).ShouldBe(TimeSpan.FromSeconds(expected));

    [Theory]
    [InlineData("retries", "max_retries must be a non-negative integer.")]
    [InlineData("initial", "backoff_initial must be a non-negative, finite number of seconds.")]
    [InlineData("max", "backoff_max must be a non-negative, finite number of seconds.")]
    [InlineData("jitter-negative", "backoff_jitter must be between zero and one.")]
    [InlineData("jitter-large", "backoff_jitter must be between zero and one.")]
    [InlineData("jitter-nan", "backoff_jitter must be between zero and one.")]
    [InlineData("jitter-infinite", "backoff_jitter must be between zero and one.")]
    [InlineData("budget-zero", "timeout must be a positive, finite number of seconds.")]
    [InlineData("budget-negative", "timeout must be a positive, finite number of seconds.")]
    public void Policy_InvalidValues_ExactErrorsBeforeNetwork(string field, string message)
    {
        var policy = field switch
        {
            "retries" => new RetryPolicy { MaxRetries = -1 },
            "initial" => new RetryPolicy { BackoffInitial = TimeSpan.FromSeconds(-1) },
            "max" => new RetryPolicy { BackoffMax = TimeSpan.FromSeconds(-1) },
            "jitter-negative" => new RetryPolicy { BackoffJitter = -0.1 },
            "jitter-large" => new RetryPolicy { BackoffJitter = 1.1 },
            "jitter-nan" => new RetryPolicy { BackoffJitter = double.NaN },
            "jitter-infinite" => new RetryPolicy { BackoffJitter = double.PositiveInfinity },
            "budget-zero" => new RetryPolicy { Timeout = TimeSpan.Zero },
            _ => new RetryPolicy { Timeout = TimeSpan.FromSeconds(-1) },
        };
        var handler = ParityTestHelper.Handler();
        var error = Should.Throw<TypeSafeException>(() => new TypeSafeClient(new TypeSafeClientOptions { ApiKey = "test", Handler = handler, Retry = policy }));
        error.Message.ShouldBe(message);
        handler.Requests.Count.ShouldBe(0);
        handler.DisposeCount.ShouldBe(0);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData(null, "bad", null)]
    [InlineData(null, "-1", null)]
    [InlineData("NaN", "1.5", 1500.0)]
    [InlineData("-1", "2", 2000.0)]
    [InlineData("bad", "2", 2000.0)]
    [InlineData("inf", null, null)]
    [InlineData(null, "1e308", null)]
    [InlineData(null, "", 0.0)]
    [InlineData(" ", "50", 0.0)]
    [InlineData("0", "50", 0.0)]
    [InlineData("125", "2", 125.0)]
    [InlineData(null, "Thu, 01 Jan 1970 00:00:00 GMT", 0.0)]
    public void RetryAfter_MillisecondsSecondsDatesAndInvalidFallback(string? milliseconds, string? seconds, double? expectedMs)
    {
        var headers = new Dictionary<string, string>();
        if (milliseconds is not null) headers["Retry-After-Ms"] = milliseconds;
        if (seconds is not null) headers["Retry-After"] = seconds;
        var error = new TypeSafeRateLimitException(new RawHttpResponse(429, "{}", headers));
        error.RetryAfter.ShouldBe(expectedMs is null ? null : TimeSpan.FromMilliseconds(expectedMs.Value));
        error.Headers.Count.ShouldBe(headers.Count);
    }

    [Theory]
    [InlineData("Retry-After", "61", 61)]
    [InlineData("retry-after-ms", "60001", 60.001)]
    public void ServerDelay_UncappedUnjittered_RespectFlagOnlyControlsWaiting(string header, string value, double seconds)
    {
        var error = new TypeSafeApiException(new RawHttpResponse(503, "{}", new Dictionary<string, string> { [header] = value }));
        new RetryPolicy().GetDelay(1, error, 1).ShouldBe(TimeSpan.FromSeconds(seconds));
        new RetryPolicy { RespectRetryAfter = false }.GetDelay(1, error, 0).ShouldBe(TimeSpan.FromSeconds(0.5));
        error.RetryAfter.ShouldBe(TimeSpan.FromSeconds(seconds));
    }

    [Fact]
    public void Policy_TimeoutConnectionExceptionsPredicate_AdditiveAndShortCircuited()
    {
        var timeout = new TypeSafeApiTimeoutException(TimeSpan.FromSeconds(2));
        var connection = new TypeSafeApiConnectionException("offline");
        new RetryPolicy { ApiTimeoutError = false }.ShouldRetry(timeout).ShouldBeFalse();
        new RetryPolicy { ApiConnectionError = false }.ShouldRetry(timeout).ShouldBeTrue();
        new RetryPolicy { ApiConnectionError = false }.ShouldRetry(connection).ShouldBeFalse();
        new RetryPolicy { ApiTimeoutError = false }.ShouldRetry(connection).ShouldBeTrue();
        var count = 0;
        var policy = new RetryPolicy { Predicate = _ => { count++; return false; } };
        policy.ShouldRetry(new TypeSafeApiException(new RawHttpResponse(429, "{}"))).ShouldBeTrue();
        count.ShouldBe(0);
        policy.ShouldRetry(new TypeSafeApiException(new RawHttpResponse(404, "{}"))).ShouldBeFalse();
        count.ShouldBe(1);
        new RetryPolicy { Exceptions = new[] { typeof(TypeSafeApiException) } }.ShouldRetry(new TypeSafeApiException(new RawHttpResponse(404, "{}"))).ShouldBeTrue();
        new RetryPolicy { Predicate = error => error is TypeSafeApiException { Status: 404 } }.ShouldRetry(new TypeSafeApiException(new RawHttpResponse(404, "{}"))).ShouldBeTrue();
        var validation = new TypeSafeApiResponseValidationException(new RawHttpResponse(200, "{}"), "model");
        new RetryPolicy().ShouldRetry(validation).ShouldBeFalse();
        new RetryPolicy { Exceptions = new[] { typeof(TypeSafeApiException) } }.ShouldRetry(validation).ShouldBeTrue();
        new RetryPolicy { HttpStatuses = new[] { 409 } }.ShouldRetry(new TypeSafeApiException(new RawHttpResponse(500, "{}"))).ShouldBeFalse();
        new RetryPolicy { HttpStatuses = new[] { 409 } }.ShouldRetry(new TypeSafeApiException(new RawHttpResponse(409, "{}"))).ShouldBeTrue();
    }

    [Theory]
    [InlineData(408, 3)]
    [InlineData(429, 3)]
    [InlineData(500, 3)]
    [InlineData(503, 3)]
    [InlineData(599, 3)]
    [InlineData(600, 1)]
    [InlineData(302, 1)]
    [InlineData(400, 1)]
    [InlineData(401, 1)]
    [InlineData(403, 1)]
    [InlineData(404, 1)]
    [InlineData(409, 1)]
    [InlineData(422, 1)]
    public async Task DefaultStatuses_AttemptCountsAndRetryHeaders(int status, int attempts)
    {
        var delays = new List<TimeSpan>();
        var handler = new ParityHandler((index, _, _) => Task.FromResult(ParityTestHelper.Response($"{{\"message\":\"attempt {index + 1}\"}}", status, $"request-{index + 1}", new Dictionary<string, string> { ["retry-after-ms"] = "0" })));
        using var client = Create(handler, new RetryPolicy(), () => 0, delays);
        var error = await Should.ThrowAsync<TypeSafeApiException>(() => client.Models.ListAsync());
        handler.Requests.Count.ShouldBe(attempts);
        RetryHeaders(handler).ShouldBe(Enumerable.Range(0, attempts).Select(index => index == 0 ? null : index.ToString()));
        delays.ShouldBe(Enumerable.Repeat(TimeSpan.Zero, attempts - 1));
        error.RequestId.ShouldBe($"request-{attempts}");
        error.Message.ShouldBe($"GET https://api.typesafe.ai/v1/models: {status} attempt {attempts} (request_id=request-{attempts})");
    }

    public static IEnumerable<object?[]> Budgets()
    {
        foreach (var models in new[] { false, true })
            foreach (var row in new (double? Budget, double Duration, double Delay, int Attempts)[] { (null, 1, 0.5, 3), (30, 10, 5, 2), (2.5, 0.75, 0.5, 2), (2, 1, 0, 2), (1, 0, 1, 1), (1, 0, 60, 1) })
                yield return [models, row.Budget, row.Duration, row.Delay, row.Attempts];
    }

    [Theory]
    [MemberData(nameof(Budgets))]
    public async Task Budget_ElapsedPlusDelayEquality_StopsWithFinalErrorAndFreshCall(bool models, double? budget, double duration, double delay, int attempts)
    {
        var now = 100.0;
        var delays = new List<TimeSpan>();
        var handler = new ParityHandler((index, _, _) =>
        {
            now += duration;
            return Task.FromResult(ParityTestHelper.Response($"{{\"message\":\"attempt {index + 1}\"}}", 429, null, new Dictionary<string, string> { ["Retry-After"] = delay.ToString(System.Globalization.CultureInfo.InvariantCulture) }));
        });
        using var client = new TypeSafeClient(new TypeSafeClientOptions
        {
            ApiKey = "test", BaseUrl = "https://api.typesafe.ai", Handler = handler,
            Retry = new RetryPolicy { Timeout = budget is null ? null : TimeSpan.FromSeconds(budget.Value) }, Clock = () => now,
            DelayAsync = (value, _) => { delays.Add(value); now += value.TotalSeconds; return Task.CompletedTask; },
        });
        for (var call = 0; call < 2; call++)
        {
            handler.Requests.Clear();
            delays.Clear();
            var error = await Should.ThrowAsync<TypeSafeRateLimitException>(async () =>
            {
                if (models) await client.Models.ListAsync();
                else await client.SystemOneAsync("x", ParityTestHelper.Questions());
            });
            handler.Requests.Count.ShouldBe(attempts);
            delays.ShouldBe(Enumerable.Repeat(TimeSpan.FromSeconds(delay), attempts - 1));
            error.ResponseBody.ShouldBe($"{{\"message\":\"attempt {attempts}\"}}");
            error.Message.ShouldBe($"{(models ? "GET https://api.typesafe.ai/v1/models" : "POST https://api.typesafe.ai/v1/systemone")}: 429 attempt {attempts}");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TimeoutThenRateLimitThenSuccess_ReplaysBytesAndAllOverrides(bool raw)
    {
        var delays = new List<TimeSpan>();
        var handler = new ParityHandler((index, _, _) => index switch
        {
            0 => throw new TaskCanceledException("read timed out"),
            1 => Task.FromResult(ParityTestHelper.Response("{\"message\":\"slow down\"}", 429, null, new Dictionary<string, string> { ["retry-after-ms"] = "125" })),
            _ => Task.FromResult(ParityTestHelper.Response()),
        });
        using var client = Create(handler, new RetryPolicy(), () => 0, delays);
        Question question = raw ? new RawQuestion(System.Text.Json.Nodes.JsonNode.Parse("{\"type\":\"noul\",\"instructions\":\"?\"}")!.AsObject()) : new Noul("?");
        var questions = new Dictionary<string, Question> { ["q"] = question };
        var headers = new Dictionary<string, string> { ["x-call"] = "override", ["authorization"] = "must-not-win", ["X-TypeSafe-Retry-Count"] = "spoof" };
        TimeSpan? observedTimeout = null;
        var perCall = new RetryPolicy
        {
            ApiTimeoutError = false,
            Predicate = error => { if (error is TypeSafeApiTimeoutException timeout) { observedTimeout = timeout.Timeout; return true; } return false; },
        };
        var result = await client.SystemOneAsync(new { document = "hello" }, questions, new SystemOneOptions
        {
            Model = "call-model", Timeout = TimeSpan.FromSeconds(2), ExtraHeaders = headers, Retry = perCall,
            ExtraBody = new Dictionary<string, object?> { ["nullable"] = null, ["beam_width"] = 4 },
        }, TestContext.Current.CancellationToken);
        observedTimeout.ShouldBe(TimeSpan.FromSeconds(2));
        result.Scores["s"].Score.ShouldBe(1.4);
        result.Choices["c"].Confidence.ShouldBe(0.8);
        handler.Requests.Count.ShouldBe(3);
        RetryHeaders(handler).ShouldBe(new string?[] { null, "1", "2" });
        foreach (var request in handler.Requests)
        {
            request.Body.ShouldBe(handler.Requests[0].Body);
            request.Json["state"]!["document"]!.GetValue<string>().ShouldBe("hello");
            request.Json["model"]!.GetValue<string>().ShouldBe("call-model");
            request.Json["beam_width"]!.GetValue<int>().ShouldBe(4);
            request.Json.ContainsKey("nullable").ShouldBeTrue();
            request.Json["nullable"].ShouldBeNull();
            request.Headers["Authorization"].ShouldBe("Bearer test");
            request.Headers["x-call"].ShouldBe("override");
        }
        delays[0].TotalSeconds.ShouldBeInRange(0.375, 0.5);
        delays[1].ShouldBe(TimeSpan.FromMilliseconds(125));
        headers["authorization"].ShouldBe("must-not-win");
        headers["X-TypeSafe-Retry-Count"].ShouldBe("spoof");
        await client.SystemOneAsync("next", questions, cancellationToken: TestContext.Current.CancellationToken);
        handler.Requests.Last().Json["model"]!.GetValue<string>().ShouldBe("client-model");
        handler.Requests.Last().Headers.ContainsKey("x-call").ShouldBeFalse();
        handler.Requests.Last().Headers.ContainsKey("X-TypeSafe-Retry-Count").ShouldBeFalse();
        handler.Requests.Last().Json.ContainsKey("beam_width").ShouldBeFalse();
    }

    [Fact]
    public async Task Budget_SuccessfulFirstAttemptBeyondBudget_IsNotDiscarded()
    {
        var now = 0.0;
        var delays = new List<TimeSpan>();
        var handler = new ParityHandler((_, _, _) => { now += 40; return Task.FromResult(ParityTestHelper.Response("{\"models\":[]}")); });
        using var client = Create(handler, new RetryPolicy { Timeout = TimeSpan.FromSeconds(1) }, () => now, delays);
        var response = await client.Models.ListAsync(cancellationToken: TestContext.Current.CancellationToken);
        response.Models.Count.ShouldBe(0);
        response.RequestId.ShouldBe("req-test");
        handler.Requests.Count.ShouldBe(1);
        delays.Count.ShouldBe(0);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(4, 5)]
    public async Task MaxRetriesAndZeroBackoff_AttemptsAfterInitial(int retries, int attempts)
    {
        var delays = new List<TimeSpan>();
        var handler = new ParityHandler((_, _, _) => Task.FromResult(ParityTestHelper.Response("{}", 503)));
        using var client = Create(handler, new RetryPolicy { MaxRetries = retries, BackoffInitial = TimeSpan.Zero }, () => 0, delays);
        await Should.ThrowAsync<TypeSafeInternalServerException>(() => client.Models.ListAsync());
        handler.Requests.Count.ShouldBe(attempts);
        delays.ShouldBe(Enumerable.Repeat(TimeSpan.Zero, retries));
    }

    [Fact]
    public async Task Exhaustion_PreservesFinalHttpAndTransportMetadata()
    {
        var delays = new List<TimeSpan>();
        var handler = new ParityHandler((index, _, _) => Task.FromResult(ParityTestHelper.Response($"{{\"message\":\"attempt {index + 1}\"}}", new[] { 429, 500, 503 }[index], $"request-{index + 1}", new Dictionary<string, string> { ["retry-after-ms"] = "0" })));
        using var client = Create(handler, new RetryPolicy(), () => 0, delays);
        var httpError = await Should.ThrowAsync<TypeSafeInternalServerException>(() => client.SystemOneAsync("x", ParityTestHelper.Questions()));
        httpError.Status.ShouldBe(503);
        httpError.RequestId.ShouldBe("request-3");
        httpError.Message.ShouldBe("POST https://api.typesafe.ai/v1/systemone: 503 attempt 3 (request_id=request-3)");
        handler.Requests.Count.ShouldBe(3);
        foreach (var timeout in new[] { false, true })
        {
            var transport = new ParityHandler((index, _, _) => timeout ? throw new TaskCanceledException($"attempt {index + 1}") : throw new HttpRequestException($"attempt {index + 1}"));
            using var other = Create(transport, new RetryPolicy { BackoffInitial = TimeSpan.Zero }, () => 0, []);
            var error = await Should.ThrowAsync<TypeSafeApiConnectionException>(() => other.Models.ListAsync());
            error.GetType().ShouldBe(timeout ? typeof(TypeSafeApiTimeoutException) : typeof(TypeSafeApiConnectionException));
            error.Message.ShouldBe(timeout ? "Request timed out (timeout=7.0)." : "Connection error: attempt 3");
            error.InnerException!.Message.ShouldBe("attempt 3");
            transport.Requests.Count.ShouldBe(3);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_RequestOrBackoff_NoRetryEvenWithBroadPolicy(bool duringBackoff)
    {
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new ParityHandler((_, _, token) =>
        {
            if (duringBackoff) return Task.FromResult(ParityTestHelper.Response("{}", 429));
            token.Register(() => pending.TrySetCanceled(token));
            entered.SetResult();
            return pending.Task;
        });
        using var client = new TypeSafeClient(new TypeSafeClientOptions
        {
            ApiKey = "test", Handler = handler, Retry = new RetryPolicy { Exceptions = new[] { typeof(Exception) } },
            DelayAsync = (_, token) => { var delay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); token.Register(() => delay.TrySetCanceled(token)); entered.SetResult(); return delay.Task; },
        });
        var task = client.Models.ListAsync(cancellationToken: cancellation.Token);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => task);
        handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ConcurrentCalls_IndependentCountsBodiesAndPerCallPolicy()
    {
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCount = 0;
        var handler = new ParityHandler(async (_, request, _) =>
        {
            if (!request.Headers.ContainsKey("X-TypeSafe-Retry-Count"))
            {
                if (Interlocked.Increment(ref firstCount) == 3) barrier.SetResult();
                await barrier.Task;
            }
            return ParityTestHelper.Response("{}", 429, null, new Dictionary<string, string> { ["retry-after-ms"] = "0" });
        });
        using var client = Create(handler, new RetryPolicy { MaxRetries = 1 }, () => 0, []);
        await Task.WhenAll(new[] { (Name: "one", Retries: (int?)0), (Name: "three", Retries: (int?)2), (Name: "default", Retries: (int?)null) }.Select(async call =>
        {
            await Should.ThrowAsync<TypeSafeRateLimitException>(() => client.SystemOneAsync(call.Name, ParityTestHelper.Questions(), new SystemOneOptions
            {
                Model = call.Name, ExtraHeaders = new Dictionary<string, string> { ["x-call"] = call.Name }, Retry = call.Retries is null ? null : new RetryPolicy { MaxRetries = call.Retries.Value },
            }));
        }));
        foreach (var (name, count) in new[] { ("one", 1), ("three", 3), ("default", 2) })
        {
            var requests = handler.Requests.Where(request => request.Headers["x-call"] == name).ToArray();
            requests.Length.ShouldBe(count);
            requests.Select(request => request.Headers.GetValueOrDefault("X-TypeSafe-Retry-Count")).ShouldBe(Enumerable.Range(0, count).Select(index => index == 0 ? null : index.ToString()));
            foreach (var request in requests)
            {
                request.Json["model"]!.GetValue<string>().ShouldBe(name);
                request.Json["state"]!.GetValue<string>().ShouldBe(name);
                request.Body.ShouldBe(requests[0].Body);
            }
        }
    }

    private static TypeSafeClient Create(ParityHandler handler, RetryPolicy policy, Func<double> clock, List<TimeSpan> delays) => new(new TypeSafeClientOptions
    {
        ApiKey = "test", BaseUrl = "https://api.typesafe.ai", Model = "client-model", Timeout = TimeSpan.FromSeconds(7), Handler = handler, Retry = policy, Clock = clock,
        DelayAsync = (delay, _) => { lock (delays) delays.Add(delay); return Task.CompletedTask; },
    });
    private static IEnumerable<string?> RetryHeaders(ParityHandler handler) => handler.Requests.Select(request => request.Headers.GetValueOrDefault("X-TypeSafe-Retry-Count"));
}