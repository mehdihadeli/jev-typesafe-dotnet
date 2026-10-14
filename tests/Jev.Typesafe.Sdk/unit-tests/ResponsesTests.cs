using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Jev.Typesafe.Sdk;

namespace Jev.Typesafe.Sdk.UnitTests;

public sealed class ResponsesTests
{
    [Fact]
    public async Task AllAnswerKinds_MetadataCachedViewsAndSchemaExport_AreConsistent()
    {
        var handler = ParityTestHelper.Handler();
        using var client = ParityTestHelper.Client(handler);
        var result = await client.SystemOneAsync("x", ParityTestHelper.Questions(), cancellationToken: TestContext.Current.CancellationToken);
        result.Model.ShouldBe("jev-test");
        result.Usage!.InputTokens.ShouldBe(10);
        result.Usage.OutputTokens.ShouldBe(8);
        result.RequestId.ShouldBe("req-test");
        result.RawHttpResponse.Status.ShouldBe(200);
        result.RawHttpResponse.Headers["X-TypeSafe-Request-ID"].ShouldBe("req-test");
        result.RawHttpResponse.Content.ShouldBe(ParityTestHelper.Result);
        result.Answers.Keys.Order().ShouldBe(new[] { "c", "n", "s" });
        result.Nouls["n"].Noul.ShouldBe(0.93);
        result.Choices["c"].Choice.ShouldBe("calm");
        result.Choices["c"].Confidence.ShouldBe(0.8);
        result.Choices["c"].Probabilities.ShouldBe(new Dictionary<string, double> { ["calm"] = 0.8, ["angry"] = 0.2 });
        result.Scores["s"].Score.ShouldBe(1.4);
        result.Scores["s"].Confidence.ShouldBe(0.6);
        result.Scores["s"].Probabilities.ShouldBe(new Dictionary<int, double> { [0] = 0.4, [1] = 0.6 });
        JsonNode.DeepEquals(JsonSerializer.SerializeToNode(result.Scores["s"].Legend[1]), JsonNode.Parse("{\"label\":\"high\",\"nested\":null}")).ShouldBeTrue();
        result.Nouls.ShouldBeSameAs(result.Nouls);
        result.Choices.ShouldBeSameAs(result.Choices);
        result.Scores.ShouldBeSameAs(result.Scores);
        result.Nouls["n"].ShouldBeSameAs(result.Answers["n"]);
        result.Choices["c"].ShouldBeSameAs(result.Answers["c"]);
        result.Scores["s"].ShouldBeSameAs(result.Answers["s"]);
        var reconstructed = SystemOneResponse.FromHttpResponse(result.RawHttpResponse);
        reconstructed.RequestId.ShouldBe("req-test");
        reconstructed.Scores["s"].Score.ShouldBe(1.4);
        reconstructed.Scores["s"].ShouldBeSameAs(reconstructed.Answers["s"]);
    }

    [Fact]
    public void SchemaExport_AllAnswerFieldsAndIndependentNestedValues_NoTransportState()
    {
        var result = SystemOneResponse.FromHttpResponse(new RawHttpResponse(200, ParityTestHelper.Result));
        _ = result.Nouls;
        _ = result.Choices;
        _ = result.Scores;
        var exported = JsonSerializer.SerializeToNode(result)!;
        JsonNode.DeepEquals(exported, JsonNode.Parse(ParityTestHelper.Result)).ShouldBeTrue("Actual export: " + exported.ToJsonString());
        exported["answers"]!["s"]!["probabilities"]!["0"] = 0.99;
        result.Scores["s"].Probabilities[0].ShouldBe(0.4);
        var restored = JsonSerializer.Deserialize<SystemOneResponse>(JsonSerializer.Serialize(result))!;
        restored.Nouls["n"].Noul.ShouldBe(0.93);
        Should.Throw<TypeSafeException>(() => restored.RawHttpResponse).Message.ShouldBe("The response was not created from a raw HTTP response.");
    }

    [Theory]
    [InlineData("[]", "")]
    [InlineData("null", "")]
    [InlineData("not-json", "")]
    [InlineData("{\"usage\":{}}", "model")]
    [InlineData("{\"model\":null,\"usage\":{}}", "model")]
    [InlineData("{\"model\":17,\"usage\":{}}", "model")]
    [InlineData("{\"model\":\"test\"}", "usage")]
    [InlineData("{\"model\":\"test\",\"usage\":null}", "usage")]
    [InlineData("{\"model\":\"test\",\"usage\":{\"input_tokens\":\"1\"}}", "usage.input_tokens")]
    [InlineData("{\"model\":\"test\",\"usage\":{\"output_tokens\":true}}", "usage.output_tokens")]
    [InlineData("{\"model\":\"test\",\"usage\":{\"input_tokens\":1.5}}", "usage.input_tokens")]
    [InlineData("{\"model\":\"test\",\"usage\":{},\"answers\":null}", "answers")]
    [InlineData("{\"model\":\"test\",\"usage\":{},\"answers\":{\"n\":{\"type\":\"noul\"}}}", "answers.n.noul")]
    [InlineData("{\"model\":\"test\",\"usage\":{},\"answers\":{\"n\":{\"type\":\"noul\",\"noul\":\"0.5\"}}}", "answers.n.noul")]
    [InlineData("{\"model\":\"test\",\"usage\":{},\"answers\":{\"c\":{\"type\":\"choice\",\"choice\":\"a\",\"probabilities\":{}}}}", "answers.c.confidence")]
    [InlineData("{\"model\":\"test\",\"usage\":{},\"answers\":{\"c\":{\"type\":\"choice\",\"confidence\":0.5,\"probabilities\":{}}}}", "answers.c.choice")]
    [InlineData("{\"model\":\"test\",\"usage\":{},\"answers\":{\"s\":{\"type\":\"score\",\"score\":1,\"confidence\":1,\"legend\":[],\"probabilities\":{}}}}", "answers.s.legend")]
    [InlineData("{\"model\":\"test\",\"usage\":{},\"answers\":{\"s\":{\"type\":\"score\",\"score\":1,\"confidence\":1,\"legend\":{\"x\":\"bad\"},\"probabilities\":{}}}}", "answers.s.legend.x")]
    [InlineData("{\"model\":\"test\",\"usage\":{},\"answers\":{\"c\":{\"type\":\"choice\",\"choice\":\"a\",\"confidence\":1,\"probabilities\":{\"a\":\"1\"}}}}", "answers.c.probabilities.a")]
    [InlineData("{\"answers\":{\"c\":\"not-a-mapping\"}}", "answers.c.type")]
    [InlineData("{\"answers\":{\"c\":null}}", "answers.c.type")]
    [InlineData("{\"answers\":{\"c\":{}}}", "answers.c.type")]
    [InlineData("{\"answers\":{\"c\":{\"type\":1}}}", "answers.c.type")]
    public void InvalidResponse_ExactFieldPathMessageAndOriginalMetadata(string body, string path)
    {
        var headers = new Dictionary<string, string> { ["x-typesafe-request-id"] = "req-123", ["X-Extra"] = "kept" };
        var raw = new RawHttpResponse(200, body, headers, "POST https://api.typesafe.ai/v1/systemone");
        var error = Should.Throw<TypeSafeApiResponseValidationException>(() => SystemOneResponse.FromHttpResponse(raw));
        error.GetType().ShouldBe(typeof(TypeSafeApiResponseValidationException));
        error.FieldPath.ShouldBe(path);
        error.Status.ShouldBe(200);
        error.RequestId.ShouldBe("req-123");
        error.Endpoint.ShouldBe(raw.Endpoint);
        error.ResponseBody.ShouldBe(body);
        error.Headers["x-extra"].ShouldBe("kept");
        error.Message.ShouldBe($"POST https://api.typesafe.ai/v1/systemone: 200 Invalid response data at '{path}'. (request_id=req-123)");
    }

    [Theory]
    [InlineData("{\"model\":\"test\",\"usage\":{}}")]
    [InlineData("{\"model\":\"test\",\"usage\":{\"input_tokens\":null,\"output_tokens\":-1},\"answers\":{}}")]
    public void Usage_OptionalNullableAndNegativeIntegers_AndMissingId(string body)
    {
        var response = SystemOneResponse.FromHttpResponse(new RawHttpResponse(200, body));
        response.Model.ShouldBe("test");
        response.Usage!.InputTokens.ShouldBeNull();
        response.Usage.OutputTokens.ShouldBe(body.Contains("-1") ? -1 : null);
        response.Answers.Count.ShouldBe(0);
        response.RawHttpResponse.Content.ShouldBe(body);
        Should.Throw<TypeSafeException>(() => response.RequestId).Message.ShouldBe("The response did not include a request ID.");
    }

    [Theory]
    [InlineData("aurora")]
    [InlineData("")]
    public async Task UnknownAnswer_WarnsAndRetainsFullJsonWithoutPollutingViews(string tag)
    {
        var body = JsonNode.Parse(ParityTestHelper.Result)!.AsObject();
        body["answers"]!["mystery"] = new JsonObject { ["type"] = tag, ["value"] = 3, ["nested"] = new JsonArray(null, "future") };
        var logs = new List<TypeSafeLogEntry>();
        var handler = ParityTestHelper.Handler(body.ToJsonString());
        using var client = new TypeSafeClient(new TypeSafeClientOptions { ApiKey = "test-key", BaseUrl = "https://api.typesafe.ai", Handler = handler, Logger = logs.Add, LogLevel = TypeSafeLogLevel.Warning });
        var response = await client.SystemOneAsync("x", ParityTestHelper.Questions(), cancellationToken: TestContext.Current.CancellationToken);
        response.Answers.Keys.Order().ShouldBe(new[] { "c", "n", "s" });
        response.Nouls.Keys.ShouldBe(new[] { "n" });
        response.Choices.Keys.ShouldBe(new[] { "c" });
        response.Scores.Keys.ShouldBe(new[] { "s" });
        response.UnmodeledAnswers["mystery"].GetProperty("value").GetInt32().ShouldBe(3);
        JsonNode.DeepEquals(JsonNode.Parse(response.UnmodeledAnswers["mystery"].GetRawText()), body["answers"]!["mystery"]).ShouldBeTrue();
        response.RawHttpResponse.Content.ShouldBe(body.ToJsonString());
        logs.Single().Level.ShouldBe(TypeSafeLogLevel.Warning);
        logs.Single().Message.ShouldBe($"Ignoring answer 'mystery' with unrecognized type '{tag}'");
        JsonSerializer.SerializeToNode(response)!.AsObject().Select(pair => pair.Key).Order().ShouldBe(new[] { "answers", "model", "usage" });
    }

    [Theory]
    [InlineData("name")]
    [InlineData("description")]
    [InlineData("release_date")]
    public void Models_RequiredStrings_MissingNullAndWrongTypeHaveIndexedPath(string field)
    {
        foreach (var invalid in new[] { "missing", "null", "number" })
        {
            var valid = JsonNode.Parse("{\"name\":\"test\",\"description\":\"Test model\",\"release_date\":\"not a date\"}")!.AsObject();
            var bad = valid.DeepClone().AsObject();
            if (invalid == "missing") bad.Remove(field);
            else bad[field] = invalid == "null" ? null : JsonValue.Create(1);
            var body = new JsonObject { ["models"] = new JsonArray(valid, bad) }.ToJsonString();
            var error = Should.Throw<TypeSafeApiResponseValidationException>(() => ListModelsResponse.FromHttpResponse(new RawHttpResponse(200, body)));
            error.FieldPath.ShouldBe("models[1]." + field);
            error.Message.ShouldBe($"200 Invalid response data at 'models[1].{field}'.");
            error.ResponseBody.ShouldBe(body);
        }
    }

    [Theory]
    [InlineData("[]", "")]
    [InlineData("null", "")]
    [InlineData("{}", "models")]
    [InlineData("{\"models\":null}", "models")]
    [InlineData("{\"models\":\"wrong\"}", "models")]
    public void Models_RootAndArrayShape_ExactPath(string body, string path)
    {
        var error = Should.Throw<TypeSafeApiResponseValidationException>(() => ListModelsResponse.FromHttpResponse(new RawHttpResponse(200, body)));
        error.FieldPath.ShouldBe(path);
        error.Message.ShouldBe($"200 Invalid response data at '{path}'.");
        error.ResponseBody.ShouldBe(body);
    }

    [Fact]
    public void Models_EmptyListAndArbitraryReleaseDate_UnknownFieldsRetainedOnlyRaw()
    {
        ListModelsResponse.FromHttpResponse(new RawHttpResponse(200, "{\"models\":[]}")).Models.Count.ShouldBe(0);
        const string body = "{\"models\":[{\"name\":\"first\",\"description\":\"One\",\"release_date\":\"not a date\",\"extra\":3},{\"name\":\"second\",\"description\":\"Two\",\"release_date\":\"\"}],\"future\":null}";
        var response = ListModelsResponse.FromHttpResponse(new RawHttpResponse(200, body));
        response.Models.Select(model => model.Name).ShouldBe(new[] { "first", "second" });
        response.Models[0].Description.ShouldBe("One");
        response.Models[0].ReleaseDate.ShouldBe("not a date");
        response.Models[1].ReleaseDate.ShouldBe("");
        response.RawHttpResponse.Content.ShouldBe(body);
        JsonSerializer.Serialize(response).ShouldNotContain("extra");
        JsonSerializer.Serialize(response).ShouldNotContain("future");
    }
}

public sealed class TypedResponseModelsTests
{
    [Fact]
    public async Task Generic_RawJsonObject_PreservesFutureAndNullWithoutSdkSchema()
    {
        const string body = "{\"future\":{\"type\":\"aurora\",\"nested\":[null,3]},\"nullable\":null}";
        var handler = ParityTestHelper.Handler(body);
        using var client = ParityTestHelper.Client(handler);
        var response = await client.SystemOneAsync<JsonObject>("x", ParityTestHelper.Questions(), cancellationToken: TestContext.Current.CancellationToken);
        JsonNode.DeepEquals(response, JsonNode.Parse(body)).ShouldBeTrue();
        response["future"]!["type"]!.GetValue<string>().ShouldBe("aurora");
        response.ContainsKey("nullable").ShouldBeTrue();
        handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Generic_StandaloneSerializerNumberSettings_AreHonored()
    {
        const string body = "{\"model\":\"custom\",\"count\":\"7\",\"answers\":{}}";
        var handler = ParityTestHelper.Handler(body);
        using var client = ParityTestHelper.Client(handler);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { NumberHandling = JsonNumberHandling.AllowReadingFromString };
        var response = await client.SystemOneAsync<StandaloneResponse>("x", ParityTestHelper.Questions(), null, options, TestContext.Current.CancellationToken);
        response.Count.ShouldBe(7);
        response.Model.ShouldBe("custom");
        response.Answers.Count.ShouldBe(0);
        options.NumberHandling.ShouldBe(JsonNumberHandling.AllowReadingFromString);
        handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Generic_CustomResponseSubclass_LiftsMatchingDtoAndKeepsBaseViews()
    {
        const string body = "{\"model\":\"test\",\"usage\":{},\"tone\":{\"choice\":\"hostile\",\"probabilities\":{\"friendly\":0}},\"answers\":{\"tone\":{\"type\":\"choice\",\"choice\":\"friendly\",\"confidence\":0.9,\"probabilities\":{\"friendly\":0.9,\"hostile\":0.1}},\"future\":{\"type\":\"aurora\",\"value\":1}}}";
        var handler = ParityTestHelper.Handler(body);
        using var client = ParityTestHelper.Client(handler);
        var response = await client.SystemOneAsync<CustomLiftedResponse>("x", ParityTestHelper.Questions(), cancellationToken: TestContext.Current.CancellationToken);
        response.Tone.Choice.ShouldBe("friendly");
        response.Tone.Probabilities["friendly"].ShouldBe(0.9);
        response.Choices["tone"].Choice.ShouldBe("friendly");
        response.Choices["tone"].ShouldBeSameAs(response.Answers["tone"]);
        response.UnmodeledAnswers["future"].GetProperty("value").GetInt32().ShouldBe(1);
        response.RequestId.ShouldBe("req-test");
        response.RawHttpResponse.Content.ShouldBe(body);
    }

    [Fact]
    public async Task Generic_CustomOptionsAndSourceGeneratedMetadata_UseOriginalStandaloneJson()
    {
        const string body = "{\"MODEL\":\"custom\",\"COUNT\":7,\"answers\":{\"future\":{\"type\":\"aurora\",\"value\":3}}}";
        var handler = ParityTestHelper.Handler(body);
        using var client = ParityTestHelper.Client(handler);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var dto = await client.SystemOneAsync<StandaloneResponse>("x", ParityTestHelper.Questions(), null, options, TestContext.Current.CancellationToken);
        var generated = await client.SystemOneAsync("x", ParityTestHelper.Questions(), null, ParityJsonContext.Default.StandaloneResponse, TestContext.Current.CancellationToken);
        foreach (var response in new[] { dto, generated })
        {
            response.Model.ShouldBe("custom");
            response.Count.ShouldBe(7);
            response.Answers["future"].GetProperty("value").GetInt32().ShouldBe(3);
        }
        handler.Requests.Count.ShouldBe(2);
        options.PropertyNameCaseInsensitive.ShouldBeTrue();
        options.Converters.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Generic_ResponseSubclass_LiftsAnswerOverTopLevelAndRetainsViews()
    {
        var body = JsonNode.Parse(ParityTestHelper.Result)!.AsObject();
        body["n"] = new JsonObject { ["type"] = "noul", ["noul"] = 0.01 };
        var handler = ParityTestHelper.Handler(body.ToJsonString());
        using var client = ParityTestHelper.Client(handler);
        var response = await client.SystemOneAsync<LiftedResponse>("x", ParityTestHelper.Questions(), cancellationToken: TestContext.Current.CancellationToken);
        response.N.Noul.ShouldBe(0.93);
        response.Missing.ShouldBeNull();
        response.Nouls["n"].Noul.ShouldBe(0.93);
        response.Nouls["n"].ShouldBeSameAs(response.Answers["n"]);
        response.RequestId.ShouldBe("req-test");
        response.RawHttpResponse.Content.ShouldBe(body.ToJsonString());
    }

    [Theory]
    [InlineData(false, "answers.spam.noul")]
    [InlineData(true, "tone.choice")]
    public async Task Generic_InvalidStandaloneAndCustomLiftedDto_QualifiedPathAndOriginalBody(bool lifted, string path)
    {
        var body = lifted ? "{\"model\":\"test\",\"usage\":{},\"answers\":{\"tone\":{\"type\":\"choice\",\"choice\":\"unknown\",\"confidence\":1,\"probabilities\":{\"friendly\":1}}}}"
            : "{\"model\":\"test\",\"answers\":{\"spam\":{\"type\":\"noul\"}}}";
        var handler = ParityTestHelper.Handler(body);
        using var client = ParityTestHelper.Client(handler);
        var error = await Should.ThrowAsync<TypeSafeApiResponseValidationException>(async () =>
        {
            if (lifted) await client.SystemOneAsync<CustomLiftedResponse>("x", ParityTestHelper.Questions());
            else await client.SystemOneAsync<KnownResponse>("x", ParityTestHelper.Questions());
        });
        error.FieldPath.ShouldBe(path);
        error.ResponseBody.ShouldBe(body);
        error.RequestId.ShouldBe("req-test");
        error.Status.ShouldBe(200);
    }

    [Fact]
    public async Task Generic_HttpFailure_PreservesErrorHierarchy()
    {
        var handler = new ParityHandler((_, _, _) => Task.FromResult(ParityTestHelper.Response("{\"detail\":\"Invalid request\"}", 400)));
        using var client = ParityTestHelper.Client(handler);
        var error = await Should.ThrowAsync<TypeSafeBadRequestException>(() => client.SystemOneAsync<KnownResponse>("x", ParityTestHelper.Questions()));
        error.Status.ShouldBe(400);
        error.RequestId.ShouldBe("req-test");
        error.Message.ShouldBe("POST https://api.typesafe.ai/v1/systemone: 400 Invalid request (request_id=req-test)");
        handler.Requests.Count.ShouldBe(1);
    }
}

public sealed class StandaloneResponse
{
    public required string Model { get; init; }
    public required int Count { get; init; }
    public required Dictionary<string, JsonElement> Answers { get; init; }
}

public sealed class KnownResponse
{
    public required string Model { get; init; }
    public required KnownAnswers Answers { get; init; }
}

public sealed class KnownAnswers
{
    public required NoulAnswer Spam { get; init; }
}

public sealed class LiftedResponse : SystemOneResponse
{
    [JsonPropertyName("n")]
    public required NoulAnswer N { get; init; }
    public NoulAnswer? Missing { get; init; }
}

public sealed class CustomLiftedResponse : SystemOneResponse
{
    public required ToneDto Tone { get; init; }
}

public sealed class ToneDto
{
    [JsonConverter(typeof(ToneChoiceConverter))]
    public required string Choice { get; init; }
    public required Dictionary<string, double> Probabilities { get; init; }
}

public sealed class ToneChoiceConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetString() is "friendly" or "hostile" ? reader.GetString()! : throw new JsonException("Unknown tone.");
    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value);
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(StandaloneResponse))]
public partial class ParityJsonContext : JsonSerializerContext;