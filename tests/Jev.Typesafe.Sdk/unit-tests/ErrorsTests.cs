using System.Text.Json;
using Jev.Typesafe.Sdk;

namespace Jev.Typesafe.Sdk.UnitTests;

public sealed class ErrorsTests
{
    [Theory]
    [InlineData(400, typeof(TypeSafeBadRequestException))]
    [InlineData(401, typeof(TypeSafeAuthenticationException))]
    [InlineData(403, typeof(TypeSafePermissionDeniedException))]
    [InlineData(404, typeof(TypeSafeNotFoundException))]
    [InlineData(422, typeof(TypeSafeUnprocessableEntityException))]
    [InlineData(429, typeof(TypeSafeRateLimitException))]
    [InlineData(500, typeof(TypeSafeInternalServerException))]
    [InlineData(503, typeof(TypeSafeInternalServerException))]
    [InlineData(599, typeof(TypeSafeInternalServerException))]
    [InlineData(600, typeof(TypeSafeInternalServerException))]
    [InlineData(302, typeof(TypeSafeApiException))]
    [InlineData(408, typeof(TypeSafeApiException))]
    [InlineData(409, typeof(TypeSafeApiException))]
    public async Task StatusMapping_ExactSubtypeMessageAndAllMetadata(int status, Type expected)
    {
        var headers = new Dictionary<string, string> { ["retry-after-ms"] = "125", ["X-Extra"] = "retained" };
        var handler = new ParityHandler((_, _, _) => Task.FromResult(ParityTestHelper.Response("{\"message\":\"Server explanation\"}", status, "req_123", headers)));
        using var client = ParityTestHelper.Client(handler);
        var error = await Should.ThrowAsync<TypeSafeApiException>(() => client.Models.ListAsync());
        error.GetType().ShouldBe(expected);
        error.ShouldBeAssignableTo<TypeSafeException>();
        error.Status.ShouldBe(status);
        ((int)error.StatusCode).ShouldBe(status);
        error.RequestId.ShouldBe("req_123");
        error.Endpoint.ShouldBe("GET https://api.typesafe.ai/v1/models");
        error.Headers["x-extra"].ShouldBe("retained");
        error.Headers["Retry-After-Ms"].ShouldBe("125");
        error.RetryAfter.ShouldBe(TimeSpan.FromMilliseconds(125));
        error.ResponseBody.ShouldBe("{\"message\":\"Server explanation\"}");
        ((JsonElement)error.Body!).GetProperty("message").GetString().ShouldBe("Server explanation");
        error.Message.ShouldBe($"GET https://api.typesafe.ai/v1/models: {status} Server explanation (request_id=req_123)");
        handler.Requests.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("", "status code (no body)")]
    [InlineData("null", "status code (no body)")]
    [InlineData("[]", "[]")]
    [InlineData("42", "42")]
    [InlineData("not JSON", "not JSON")]
    [InlineData("\"plain JSON string\"", "plain JSON string")]
    [InlineData("{\"error\":\"first\",\"message\":\"ignored\"}", "first")]
    [InlineData("{\"error\":{\"message\":\"nested\"},\"message\":\"ignored\"}", "nested")]
    [InlineData("{\"message\":\"message\",\"detail\":\"ignored\"}", "message")]
    [InlineData("{\"detail\":\"detail\"}", "detail")]
    [InlineData("{\"detail\":{\"message\":\"nested detail\"}}", "nested detail")]
    [InlineData("{\"error\":\"\",\"message\":\"ignored\"}", "{\"error\":\"\",\"message\":\"ignored\"}")]
    [InlineData("{\"detail\":[null,42,{\"msg\":4}]}", "{\"detail\":[null,42,{\"msg\":4}]}")]
    [InlineData("{\"detail\":[{\"loc\":[\"body\",\"questions\",\"body\",\"q\",\"score\",\"criteria\",0],\"msg\":\"Invalid\"},{\"msg\":\"Missing\"},null]}", "questions.q.score.criteria.0: Invalid; Missing")]
    public void ErrorMessage_BodyPrecedenceAndEdges_Exact(string body, string detail)
    {
        var error = new TypeSafeApiException(new RawHttpResponse(400, body));
        error.Message.ShouldBe("400 " + detail);
        error.ResponseBody.ShouldBe(body);
        error.RequestId.ShouldBeNull();
        error.Endpoint.ShouldBeNull();
    }

    [Fact]
    public void ErrorMessage_TruncatesOnlyFallbackAndExplicitOverrideWins()
    {
        var longText = new string('x', 201);
        new TypeSafeApiException(new RawHttpResponse(400, longText)).Message.ShouldBe("400 " + longText);
        new TypeSafeApiException(new RawHttpResponse(400, "{\"message\":\"" + longText + "\"}")).Message.ShouldBe("400 " + longText);
        var fallback = "{\"unknown\":\"" + longText + "\"}";
        new TypeSafeApiException(new RawHttpResponse(400, fallback)).Message.ShouldBe("400 " + fallback[..200] + "\u2026");
        var raw = new RawHttpResponse(429, "{\"message\":\"ignored\"}", new Dictionary<string, string> { ["retry-after-ms"] = "125" });
        var custom = new TypeSafeApiException(raw, "Custom");
        custom.Message.ShouldBe("429 Custom");
        custom.ResponseBody.ShouldBe(raw.Content);
        custom.RetryAfter.ShouldBe(TimeSpan.FromMilliseconds(125));
        new TypeSafeApiException(raw, "").Message.ShouldBe("429");
    }

    [Fact]
    public async Task RawResponse_SanitizesEndpointAndSnapshotsHeaders_AndErrorReconstruction()
    {
        using var response = ParityTestHelper.Response("{\"message\":\"Bad request\"}", 400, null);
        response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://user:password@example.test/v1/models?token=secret#fragment");
        var raw = await RawHttpResponse.FromAsync(response, TestContext.Current.CancellationToken);
        var error = Should.Throw<TypeSafeBadRequestException>(() => ListModelsResponse.FromHttpResponse(raw));
        error.Endpoint.ShouldBe("GET https://example.test/v1/models");
        error.Message.ShouldBe("GET https://example.test/v1/models: 400 Bad request");
        error.ToString().ShouldNotContain("password");
        error.ToString().ShouldNotContain("token=secret");
        var restored = new TypeSafeBadRequestException(raw);
        restored.GetType().ShouldBe(error.GetType());
        restored.Message.ShouldBe(error.Message);
        restored.ResponseBody.ShouldBe(error.ResponseBody);
        var headers = new Dictionary<string, string> { ["x-typesafe-request-id"] = "original" };
        var snapshot = new RawHttpResponse(429, "{}", headers);
        headers["x-typesafe-request-id"] = "changed";
        snapshot.Headers["X-TypeSafe-Request-ID"].ShouldBe("original");
    }
}