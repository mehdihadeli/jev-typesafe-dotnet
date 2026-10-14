using System.Net;
using System.Text.Json.Nodes;
using Jev.Typesafe.Sdk;

namespace Jev.Typesafe.Sdk.UnitTests;

internal sealed record RequestSnapshot(HttpMethod Method, string Url, Dictionary<string, string> Headers, byte[] Body)
{
    public JsonObject Json => JsonNode.Parse(Body)!.AsObject();
}

internal sealed class ParityHandler(Func<int, RequestSnapshot, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    public List<RequestSnapshot> Requests { get; } = [];
    public int DisposeCount { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.ToDictionary(pair => pair.Key, pair => string.Join(", ", pair.Value), StringComparer.OrdinalIgnoreCase);
        if (request.Content is not null)
            foreach (var pair in request.Content.Headers) headers[pair.Key] = string.Join(", ", pair.Value);
        var snapshot = new RequestSnapshot(request.Method, request.RequestUri!.AbsoluteUri, headers,
            request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken));
        int index;
        lock (Requests)
        {
            index = Requests.Count;
            Requests.Add(snapshot);
        }
        return await send(index, snapshot, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) DisposeCount++;
        base.Dispose(disposing);
    }
}

internal static class ParityTestHelper
{
    public const string Result = """
        {"model":"jev-test","usage":{"input_tokens":10,"output_tokens":8},"answers":{"n":{"type":"noul","noul":0.93},"c":{"type":"choice","choice":"calm","confidence":0.8,"probabilities":{"calm":0.8,"angry":0.2}},"s":{"type":"score","score":1.4,"confidence":0.6,"legend":{"0":"low","1":{"label":"high","nested":null}},"probabilities":{"0":0.4,"1":0.6}}}}
        """;
    public static Dictionary<string, TypeSafeQuestion> Questions() => new() { ["q"] = new Noul("?") };
    public static HttpResponseMessage Response(string body = Result, int status = 200, string? requestId = "req-test", IReadOnlyDictionary<string, string>? headers = null)
    {
        var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body) };
        if (requestId is not null) response.Headers.TryAddWithoutValidation("x-typesafe-request-id", requestId);
        if (headers is not null)
            foreach (var pair in headers) response.Headers.TryAddWithoutValidation(pair.Key, pair.Value);
        return response;
    }
    public static ParityHandler Handler(string body = Result) => new((_, _, _) => Task.FromResult(Response(body)));
    public static TypeSafeClient Client(ParityHandler handler) => new(new TypeSafeClientOptions
    {
        ApiKey = "test-key", BaseUrl = "https://api.typesafe.ai", Model = "jev-latest", Handler = handler,
        LogLevel = TypeSafeLogLevel.Off, Retry = new RetryPolicy { MaxRetries = 0 },
    });
}