using System.Text.Json;
using System.Text.Json.Nodes;
using Jev.Typesafe.Sdk;

namespace Jev.Typesafe.Sdk.UnitTests;

public sealed class QuestionsTests
{
    [Fact]
    public async Task Constructors_MixedRawQuestions_PreserveTagsDescriptionsAndNestedNulls()
    {
        var raw = JsonNode.Parse("{\"type\":\"future\",\"nested\":{\"k\":null},\"weight\":3}")!.AsObject();
        var before = raw.DeepClone();
        Question future = raw;
        JsonContent description = new JsonObject { ["label"] = "calm", ["nested"] = null };
        var questions = new Dictionary<string, Question>
        {
            ["n"] = new Noul("", new NoulCriteria { True = "yes", False = null }),
            ["c"] = new Choice(new Dictionary<string, JsonContent?> { ["calm"] = description, ["angry"] = null }),
            ["s"] = new Score(new object?[] { "low", new JsonArray("high", null) }),
            ["future"] = future,
            ["labels"] = new Choice(new[] { "yes", "no" }, "Pick"),
        };
        var handler = ParityTestHelper.Handler();
        using var client = ParityTestHelper.Client(handler);
        await client.SystemOneAsync("hello", questions, cancellationToken: TestContext.Current.CancellationToken);
        var expected = JsonNode.Parse("""
            {"n":{"type":"noul","instructions":"","criteria":{"true":"yes","false":null}},"c":{"type":"choice","criteria":{"calm":{"label":"calm","nested":null},"angry":null}},"s":{"type":"score","criteria":["low",["high",null]]},"future":{"type":"future","nested":{"k":null},"weight":3},"labels":{"type":"choice","instructions":"Pick","criteria":{"yes":null,"no":null}}}
            """);
        JsonNode.DeepEquals(handler.Requests.Single().Json["questions"], expected).ShouldBeTrue();
        JsonNode.DeepEquals(raw, before).ShouldBeTrue();
        questions["future"].ShouldBeSameAs(future);
    }

    [Theory]
    [InlineData("{}", "Question \"invalid\" must be a question object or a dictionary with a nonempty string \"type\".")]
    [InlineData("{\"type\":\"\"}", "Question \"invalid\" must be a question object or a dictionary with a nonempty string \"type\".")]
    [InlineData("{\"type\":null}", "Question \"invalid\" must be a question object or a dictionary with a nonempty string \"type\".")]
    [InlineData("{\"type\":1}", "Question \"invalid\" must be a question object or a dictionary with a nonempty string \"type\".")]
    [InlineData("{\"type\":[\"future\"]}", "Question \"invalid\" must be a question object or a dictionary with a nonempty string \"type\".")]
    [InlineData("{\"type\":\"choice\"}", "Question \"invalid\" requires \"criteria\".")]
    [InlineData("{\"type\":\"score\"}", "Question \"invalid\" requires \"criteria\".")]
    [InlineData("{\"type\":\"score\",\"criteria\":[]}", "Score question \"invalid\" has no criteria; at least one score is required.")]
    [InlineData("{\"type\":\"choice\",\"criteria\":{}}", "Choice question \"invalid\" has no criteria; at least one choice is required.")]
    [InlineData("{\"type\":\"noul\",\"criteria\":\"spam\",\"instructions\":\"?\"}", "Noul question \"invalid\" criteria must be an object.")]
    [InlineData("{\"type\":\"noul\"}", "Noul question \"invalid\" has neither instructions nor criteria; at least one is required.")]
    [InlineData("{\"type\":\"noul\",\"criteria\":{\"true\":null,\"false\":null}}", "Noul question \"invalid\" has neither instructions nor criteria; at least one is required.")]
    public async Task RawQuestion_InvalidStructure_ExactErrorBeforeNetwork(string json, string message)
    {
        var handler = ParityTestHelper.Handler();
        using var client = ParityTestHelper.Client(handler);
        var error = await Should.ThrowAsync<TypeSafeException>(() => client.SystemOneAsync("x", new Dictionary<string, Question> { ["invalid"] = new RawQuestion(JsonNode.Parse(json)!.AsObject()) }));
        error.GetType().ShouldBe(typeof(TypeSafeException));
        error.Message.ShouldBe(message);
        handler.Requests.Count.ShouldBe(0);
    }

    [Theory]
    [InlineData("{\"type\":\"noul\",\"instructions\":\"\",\"criteria\":{}}")]
    [InlineData("{\"type\":\"noul\",\"instructions\":[],\"criteria\":null}")]
    [InlineData("{\"type\":\"noul\",\"criteria\":{\"future\":{}}}")]
    [InlineData("{\"type\":\"noul\",\"criteria\":{\"true\":[]}}")]
    [InlineData("{\"type\":\"noul\",\"instructions\":17}")]
    [InlineData("{\"type\":\"choice\",\"criteria\":[\"defer shape to server\"]}")]
    [InlineData("{\"type\":\" \" ,\"instructions\":null}")]
    public async Task RawQuestion_PartialValidation_PreservesOriginalPayload(string json)
    {
        var raw = JsonNode.Parse(json)!.AsObject();
        var handler = new ParityHandler((_, _, _) => Task.FromResult(ParityTestHelper.Response("{\"detail\":\"server validation\"}", 422)));
        using var client = ParityTestHelper.Client(handler);
        var error = await Should.ThrowAsync<TypeSafeUnprocessableEntityException>(() => client.SystemOneAsync("x", new Dictionary<string, Question> { ["q"] = raw }));
        error.Status.ShouldBe(422);
        error.Message.ShouldBe("POST https://api.typesafe.ai/v1/systemone: 422 server validation (request_id=req-test)");
        JsonNode.DeepEquals(handler.Requests.Single().Json["questions"]!["q"], raw).ShouldBeTrue();
    }

    [Fact]
    public async Task TypedCriteria_EmptyAndNullDescriptions_ValidateAtExpectedBoundary()
    {
        Should.Throw<TypeSafeException>(() => new Score(Array.Empty<object?>()));
        Should.Throw<TypeSafeException>(() => new Score(new object?[] { null }));
        var handler = ParityTestHelper.Handler();
        using var client = ParityTestHelper.Client(handler);
        var choice = new Choice(Array.Empty<string>());
        var choiceError = await Should.ThrowAsync<TypeSafeException>(() => client.SystemOneAsync("x", new Dictionary<string, TypeSafeQuestion> { ["label"] = choice }));
        choiceError.Message.ShouldBe("Choice question \"label\" has no criteria; at least one choice is required.");
        var noulError = await Should.ThrowAsync<TypeSafeException>(() => client.SystemOneAsync("x", new Dictionary<string, TypeSafeQuestion> { ["spam"] = new Noul { Criteria = new NoulCriteria { True = null, False = null } } }));
        noulError.Message.ShouldBe("Noul question \"spam\" has neither instructions nor criteria; at least one is required.");
        handler.Requests.Count.ShouldBe(0);
    }

    [Fact]
    public async Task JsonContent_ConversionsAndNonFiniteState_PreserveStructuralJson()
    {
        JsonContent text = "hello";
        JsonContent mapping = new JsonObject { ["message"] = "\u00e9", ["nested"] = null };
        JsonContent array = new JsonArray("item", null);
        using var element = JsonDocument.Parse("{\"element\":[null,3]}");
        var content = JsonContent.From(element.RootElement);
        JsonSerializer.Serialize(text).ShouldBe("\"hello\"");
        JsonNode.DeepEquals(JsonNode.Parse(JsonSerializer.Serialize(mapping)), JsonNode.Parse("{\"message\":\"\u00e9\",\"nested\":null}")).ShouldBeTrue();
        JsonSerializer.Serialize(array).ShouldBe("[\"item\",null]");
        JsonSerializer.Serialize(content).ShouldBe("{\"element\":[null,3]}");
        var handler = ParityTestHelper.Handler();
        using var client = ParityTestHelper.Client(handler);
        await client.SystemOneAsync(new Dictionary<string, object?> { ["nested"] = new object?[] { null, double.NaN, double.PositiveInfinity, double.NegativeInfinity } }, ParityTestHelper.Questions(),
            new SystemOneOptions { ExtraBody = new Dictionary<string, object?> { ["extra"] = double.NaN } }, TestContext.Current.CancellationToken);
        JsonNode.DeepEquals(handler.Requests.Single().Json["state"], JsonNode.Parse("{\"nested\":[null,\"NaN\",\"Infinity\",\"-Infinity\"]}")).ShouldBeTrue();
        handler.Requests[0].Json["extra"]!.GetValue<string>().ShouldBe("NaN");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RichArrays_StateInstructionsCriteria_TypedAndRawPreserveNestedNull(bool raw)
    {
        var instructions = new JsonArray("Read the message", new JsonObject { ["context"] = null });
        var description = new JsonArray("Example", null);
        var typed = new Dictionary<string, TypeSafeQuestion>
        {
            ["yes"] = new Noul(instructions, new NoulCriteria { True = description, False = null }),
            ["label"] = new Choice { Instructions = instructions, Criteria = new Dictionary<string, object?> { ["a"] = description, ["b"] = null } },
            ["rating"] = new Score(new object?[] { description }, instructions),
        };
        var expected = JsonNode.Parse("""
            {"yes":{"type":"noul","instructions":["Read the message",{"context":null}],"criteria":{"true":["Example",null],"false":null}},"label":{"type":"choice","instructions":["Read the message",{"context":null}],"criteria":{"a":["Example",null],"b":null}},"rating":{"type":"score","instructions":["Read the message",{"context":null}],"criteria":[["Example",null]]}}
            """)!.AsObject();
        var questions = raw ? expected.ToDictionary(pair => pair.Key, pair => (TypeSafeQuestion)new RawQuestion(pair.Value!.DeepClone().AsObject())) : typed;
        var handler = ParityTestHelper.Handler();
        using var client = ParityTestHelper.Client(handler);
        await client.SystemOneAsync(new JsonArray(new JsonObject { ["message"] = "Classify" }, null), questions, cancellationToken: TestContext.Current.CancellationToken);
        JsonNode.DeepEquals(handler.Requests.Single().Json["state"], JsonNode.Parse("[{\"message\":\"Classify\"},null]")).ShouldBeTrue();
        JsonNode.DeepEquals(handler.Requests[0].Json["questions"], expected).ShouldBeTrue();
        instructions.Count.ShouldBe(2);
        description.Count.ShouldBe(2);
        description[1].ShouldBeNull();
    }

    [Fact]
    public async Task StateQuestionsAndEncodingErrors_ExactMessagesBeforeNetwork()
    {
        var handler = ParityTestHelper.Handler();
        using var client = ParityTestHelper.Client(handler);
        var stateError = await Should.ThrowAsync<TypeSafeException>(() => client.SystemOneAsync(null!, new Dictionary<string, TypeSafeQuestion>()));
        stateError.Message.ShouldBe("State is required.");
        var questionsError = await Should.ThrowAsync<TypeSafeException>(() => client.SystemOneAsync("x", new Dictionary<string, TypeSafeQuestion>()));
        questionsError.Message.ShouldBe("At least one question is required.");
        var cycle = new Dictionary<string, object?>();
        cycle["self"] = cycle;
        var encoding = await Should.ThrowAsync<TypeSafeException>(() => client.SystemOneAsync(cycle, ParityTestHelper.Questions()));
        encoding.Message.ShouldBe("The request body could not be encoded as JSON");
        handler.Requests.Count.ShouldBe(0);
    }
}