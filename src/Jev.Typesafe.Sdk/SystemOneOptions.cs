namespace Jev.Typesafe.Sdk;

public class RequestOptions
{
    public RetryPolicy? Retry { get; init; }
    public TimeSpan? Timeout { get; init; }
    public IReadOnlyDictionary<string, string>? ExtraHeaders { get; init; }
}

public sealed class SystemOneOptions : RequestOptions
{
    public string? Model { get; init; }
    public IReadOnlyDictionary<string, object?>? ExtraBody { get; init; }
}

public sealed class ModelsResource
{
    private readonly TypeSafeClient _client;
    internal ModelsResource(TypeSafeClient client) => _client = client;
    public Task<ListModelsResponse> ListAsync(RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        _client.SendAsync(HttpMethod.Get, "/v1/models", null, options, raw => ResponseDecoder.Decode<ListModelsResponse>(raw), cancellationToken);
}