using System.Net.Http.Headers;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace Jev.Typesafe.Sdk;

public sealed class TypeSafeClient : IDisposable, IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly TypeSafeClientOptions _options;
    private readonly string _defaultModel;
    private readonly string _baseUrl;
    private readonly string _apiKey;
    private readonly TimeSpan _timeout;
    private readonly RetryPolicy _retry;
    private readonly TypeSafeLogLevel _logLevel;
    private readonly ILogger<TypeSafeClient>? _telemetryLogger;
    private readonly Random _random = new();
    private bool _disposed;

    public ModelsResource Models { get; }

    public TypeSafeClient(TypeSafeClientOptions options) : this(null, options) { }

    public TypeSafeClient(HttpClient? httpClient = null, TypeSafeClientOptions? options = null)
    {
        _options = options ?? new TypeSafeClientOptions();
        if ((httpClient is not null && _options.HttpClient is not null)
            || _options.Handler is not null && (httpClient is not null || _options.HttpClient is not null))
            throw new TypeSafeException("transport and http_client are mutually exclusive.");
        var supplied = httpClient ?? _options.HttpClient;
        _apiKey = (_options.ApiKey ?? ResolveEnvironment("TYPESAFE_API_KEY") ?? "").Trim();
        if (_apiKey.Length == 0)
            throw new TypeSafeException("No API key was provided. Pass api_key or set the TYPESAFE_API_KEY environment variable.");
        if (_apiKey.Any(character => character < 33 || character > 126))
            throw new TypeSafeException("API key must contain only printable ASCII characters without whitespace.");
        _defaultModel = _options.ConfiguredModel ?? ResolveEnvironment("TYPESAFE_DEFAULT_MODEL") ?? "jev-latest";
        _baseUrl = (_options.BaseUrl ?? ResolveEnvironment("TYPESAFE_BASE_URL") ?? "https://api.typesafe.ai").TrimEnd('/');
        _timeout = _options.Timeout ?? supplied?.Timeout ?? TimeSpan.FromSeconds(10);
        ValidateTimeout(_timeout);
        _retry = _options.Retry ?? new RetryPolicy { MaxRetries = _options.MaxRetries, BackoffInitial = _options.RetryDelay };
        _retry.Validate();
        _logLevel = _options.LogLevel ?? TypeSafeLogging.Resolve(ResolveEnvironment("TYPESAFE_LOG_LEVEL"));
        _telemetryLogger = _options.LoggerFactory?.CreateLogger<TypeSafeClient>();
        _httpClient = supplied ?? (_options.Handler is { } handler ? new HttpClient(handler) : new HttpClient());
        if (supplied is null) _httpClient.Timeout = Timeout.InfiniteTimeSpan;
        _ownsHttpClient = supplied is null || !_options.LeaveOpen;
        Models = new ModelsResource(this);
    }

    public Task<SystemOneResponse> SystemOneAsync(object state, IReadOnlyDictionary<string, TypeSafeQuestion> questions,
        string? model = null, CancellationToken cancellationToken = default) =>
        SystemOneAsync(state, questions, new SystemOneOptions { Model = model }, cancellationToken);

    public Task<SystemOneResponse> SystemOneAsync(object state, IReadOnlyDictionary<string, TypeSafeQuestion> questions,
        SystemOneOptions options, CancellationToken cancellationToken = default) =>
        SystemOneAsync<SystemOneResponse>(state, questions, options, cancellationToken);

    public Task<SystemOneResponse> SystemOneAsync(object state, IReadOnlyDictionary<string, Question> questions,
        SystemOneOptions? options = null, CancellationToken cancellationToken = default) =>
        SystemOneAsync<SystemOneResponse>(state, ConvertQuestions(questions), options, cancellationToken);

    public Task<T> SystemOneAsync<T>(object state, IReadOnlyDictionary<string, TypeSafeQuestion> questions,
        SystemOneOptions? options = null, CancellationToken cancellationToken = default) =>
        SendSystemOne(state, questions, options, raw => ResponseDecoder.Decode<T>(raw, warning: Warn), cancellationToken);

    public Task<T> SystemOneAsync<T>(object state, IReadOnlyDictionary<string, Question> questions,
        SystemOneOptions? options = null, CancellationToken cancellationToken = default) =>
        SystemOneAsync<T>(state, ConvertQuestions(questions), options, cancellationToken);

    public Task<T> SystemOneAsync<T>(object state, IReadOnlyDictionary<string, TypeSafeQuestion> questions,
        SystemOneOptions? options, JsonSerializerOptions serializerOptions, CancellationToken cancellationToken = default) =>
        SendSystemOne(state, questions, options, raw => ResponseDecoder.Decode<T>(raw, serializerOptions, warning: Warn), cancellationToken);

    public Task<T> SystemOneAsync<T>(object state, IReadOnlyDictionary<string, Question> questions,
        SystemOneOptions? options, JsonSerializerOptions serializerOptions, CancellationToken cancellationToken = default) =>
        SystemOneAsync<T>(state, ConvertQuestions(questions), options, serializerOptions, cancellationToken);

    public Task<T> SystemOneAsync<T>(object state, IReadOnlyDictionary<string, TypeSafeQuestion> questions,
        SystemOneOptions? options, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken = default) =>
        SendSystemOne(state, questions, options, raw => ResponseDecoder.Decode(raw, typeInfo: typeInfo, warning: Warn), cancellationToken);

    public Task<T> SystemOneAsync<T>(object state, IReadOnlyDictionary<string, Question> questions,
        SystemOneOptions? options, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken = default) =>
        SystemOneAsync(state, ConvertQuestions(questions), options, typeInfo, cancellationToken);

    public async Task<IReadOnlyList<ModelMetadata>> ListModelsAsync(CancellationToken cancellationToken = default) =>
        (await Models.ListAsync(cancellationToken: cancellationToken).ConfigureAwait(false)).Models;

    private Task<T> SendSystemOne<T>(object state, IReadOnlyDictionary<string, TypeSafeQuestion> questions,
        SystemOneOptions? options, Func<RawHttpResponse, T> decode, CancellationToken cancellationToken)
    {
        if (state is null) throw new TypeSafeException("State is required.");
        var normalized = QuestionPreparation.Normalize(questions);
        byte[] content;
        string? requestedModel;
        try
        {
            var body = new JsonObject
            {
                ["state"] = QuestionPreparation.ToNode(state),
                ["model"] = options?.Model ?? _defaultModel,
                ["questions"] = normalized,
            };
            if (options?.ExtraBody is { } extra)
                foreach (var pair in extra) body[pair.Key] = QuestionPreparation.ToNode(pair.Value);
            requestedModel = body["model"] is JsonValue value && value.TryGetValue<string>(out var name) ? name : null;
            content = Encoding.UTF8.GetBytes(body.ToJsonString());
        }
        catch (Exception error) when (error is JsonException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            throw new TypeSafeException("The request body could not be encoded as JSON", new Exception("JSON serialization failed."));
        }
        return SendAsync(HttpMethod.Post, "/v1/systemone", content, options, decode, cancellationToken, requestedModel);
    }

    internal async Task<T> SendAsync<T>(HttpMethod method, string path, byte[]? content, RequestOptions? options,
        Func<RawHttpResponse, T> decode, CancellationToken cancellationToken, string? requestedModel = null)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TypeSafeClient));
        var timeout = options?.Timeout ?? _timeout;
        ValidateTimeout(timeout);
        var policy = options?.Retry ?? _retry;
        policy.Validate();
        var headers = PrepareHeaders(options?.ExtraHeaders, content is not null);
        var uri = new Uri(_baseUrl + path, UriKind.Absolute);
        var endpoint = RawHttpResponse.FormatEndpoint(method, uri);
        var start = _options.Clock();
        var operation = path == "/v1/systemone" ? "systemone" : "models.list";
        var measurementStart = TypeSafeDiagnostics.StartMeasurement();
        int? responseStatus = null;
        Exception? operationFailure = null;
        using var activity = TypeSafeDiagnostics.Source.StartActivity(method.Method + " " + path, ActivityKind.Client);
        activity?.SetTag("typesafe.operation.name", operation);
        activity?.SetTag("http.request.method", method.Method);
        activity?.SetTag("server.address", uri.IdnHost);
        activity?.SetTag("server.port", uri.Port);
        if (requestedModel is not null) activity?.SetTag("gen_ai.request.model", requestedModel);
        var attempt = 0;
        var retryDelay = TimeSpan.Zero;
        var builder = new ResiliencePipelineBuilder();
        if (policy.MaxRetries > 0)
        {
            builder.AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = policy.MaxRetries,
                ShouldHandle = args =>
                {
                    if (args.Context.CancellationToken.IsCancellationRequested
                        || args.Outcome.Exception is not { } failure || !policy.ShouldRetry(failure))
                        return new ValueTask<bool>(false);
                    double random;
                    lock (_random) random = _random.NextDouble();
                    retryDelay = policy.GetDelay(args.AttemptNumber + 1, failure, random);
                    return new ValueTask<bool>(policy.Timeout is not { } budget
                        || _options.Clock() - start + retryDelay.TotalSeconds < budget.TotalSeconds);
                },
                DelayGenerator = _ => new ValueTask<TimeSpan?>(TimeSpan.Zero),
                OnRetry = async args =>
                {
                    attempt = args.AttemptNumber + 1;
                    activity?.AddEvent(new ActivityEvent("typesafe.retry", tags: new ActivityTagsCollection
                    {
                        ["typesafe.retry.attempt"] = attempt,
                        ["typesafe.retry.delay_ms"] = retryDelay.TotalMilliseconds,
                    }));
                    if (_logLevel <= TypeSafeLogLevel.Info)
                        _telemetryLogger?.LogInformation("TypeSafe {HttpMethod} {EndpointPath} retry {RetryCount} after {RetryDelayMs} ms",
                            method.Method, path, attempt, retryDelay.TotalMilliseconds);
                    await _options.DelayAsync(retryDelay, args.Context.CancellationToken).ConfigureAwait(false);
                },
            });
        }
        var pipeline = builder.Build();
        try
        {
            return await pipeline.ExecuteAsync<T>(async token =>
            {
                token.ThrowIfCancellationRequested();
                responseStatus = null;
                activity?.SetTag("http.response.status_code", null);
                activity?.SetTag("typesafe.retry.count", attempt);
                activity?.SetTag("http.request.resend_count", attempt);
                if (attempt > 0) TypeSafeDiagnostics.RecordRetry(operation);
                using var request = new HttpRequestMessage(method, uri);
                if (content is not null)
                {
                    request.Content = new ByteArrayContent(content);
                    request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                }
                foreach (var pair in headers)
                    if (!pair.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) request.Headers.TryAddWithoutValidation(pair.Key, pair.Value);
                if (attempt > 0)
                {
                    request.Headers.TryAddWithoutValidation("X-TypeSafe-Retry-Count", attempt.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    Log(TypeSafeLogLevel.Info, $"{endpoint} retry {attempt}");
                }
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
                if (timeout != Timeout.InfiniteTimeSpan) linked.CancelAfter(timeout);
                try
                {
                    LogWire(endpoint, "->", headers, content is null ? "" : Encoding.UTF8.GetString(content));
                    using var response = await _httpClient.SendAsync(request, linked.Token).ConfigureAwait(false);
                    response.RequestMessage ??= request;
                    var raw = await RawHttpResponse.FromAsync(response, linked.Token).ConfigureAwait(false);
                    responseStatus = raw.Status;
                    activity?.SetTag("http.response.status_code", raw.Status);
                    Log(TypeSafeLogLevel.Info, $"{endpoint} <- {raw.Status} (request {(raw.Headers.TryGetValue("x-typesafe-request-id", out var id) ? id : "-")})");
                    LogWire(endpoint, "<-", raw.Headers, raw.Content);
                    var result = decode(raw);
                    TypeSafeDiagnostics.RecordResponse(activity, operation, raw);
                    if (_logLevel <= TypeSafeLogLevel.Info)
                        _telemetryLogger?.LogInformation("TypeSafe {HttpMethod} {EndpointPath} completed with HTTP {StatusCode} after {RetryCount} retries",
                            method.Method, path, raw.Status, attempt);
                    return result;
                }
                catch (OperationCanceledException error) when (!token.IsCancellationRequested)
                {
                    throw new TypeSafeApiTimeoutException(timeout, TypeSafeLogging.SafeException(error, headers));
                }
                catch (HttpRequestException error) when (!token.IsCancellationRequested)
                {
                    throw new TypeSafeApiConnectionException("Connection error: " + TypeSafeLogging.Redact(error.Message, headers), TypeSafeLogging.SafeException(error, headers));
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            operationFailure = failure;
            if (cancellationToken.IsCancellationRequested)
            {
                activity?.SetStatus(ActivityStatusCode.Error);
                activity?.SetTag("error.type", typeof(OperationCanceledException).FullName);
                throw;
            }
            RecordFailure(activity, failure, method.Method, path);
            throw;
        }
        finally
        {
            TypeSafeDiagnostics.RecordCompletion(operation, responseStatus, operationFailure,
                cancellationToken.IsCancellationRequested && operationFailure is not null, measurementStart);
        }
    }

    private void RecordFailure(Activity? activity, Exception failure, string method, string path)
    {
        activity?.SetStatus(ActivityStatusCode.Error);
        activity?.SetTag("error.type", failure.GetType().FullName);
        if (_logLevel <= TypeSafeLogLevel.Error)
            _telemetryLogger?.LogError("TypeSafe {HttpMethod} {EndpointPath} failed with {ErrorType}",
                method, path, failure.GetType().FullName);
    }

    private Dictionary<string, string> PrepareHeaders(IReadOnlyDictionary<string, string>? extra, bool hasBody)
    {
        var result = _httpClient.DefaultRequestHeaders.ToDictionary(pair => pair.Key, pair => string.Join(", ", pair.Value), StringComparer.OrdinalIgnoreCase);
        if (_options.Headers is { } defaults) foreach (var pair in defaults) result[pair.Key] = pair.Value;
        if (extra is not null) foreach (var pair in extra) result[pair.Key] = pair.Value;
        result.Remove("X-TypeSafe-Retry-Count");
        var version = typeof(TypeSafeClient).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        result["Authorization"] = "Bearer " + _apiKey;
        result["Accept"] = "application/json";
        result["User-Agent"] = "jev-typesafe-dotnet/" + version;
        result["X-TypeSafe-SDK"] = "jev-typesafe-dotnet/" + version;
        result["X-TypeSafe-Runtime"] = "dotnet/" + Environment.Version;
        if (hasBody) result["Content-Type"] = "application/json";
        return result;
    }

    private void LogWire(string endpoint, string arrow, IReadOnlyDictionary<string, string> headers, string body)
    {
        if (_logLevel != TypeSafeLogLevel.Debug) return;
        var safeHeaders = headers.ToDictionary(pair => pair.Key, pair => TypeSafeLogging.IsSecret(pair.Key) ? "***" : pair.Value);
        Log(TypeSafeLogLevel.Debug, $"{endpoint} {arrow} headers={JsonSerializer.Serialize(safeHeaders)} body={body}");
    }

    private void Warn(string message)
    {
        Log(TypeSafeLogLevel.Warning, message);
        if (_logLevel <= TypeSafeLogLevel.Warning)
            _telemetryLogger?.LogWarning("TypeSafe response skipped an unsupported answer type");
    }
    private void Log(TypeSafeLogLevel level, string message)
    {
        if (level < _logLevel || _logLevel == TypeSafeLogLevel.Off) return;
        if (_options.Logger is { } logger) logger(new TypeSafeLogEntry(level, message));
        else System.Diagnostics.Trace.WriteLine(message, "typesafe_sdk");
    }

    private static void ValidateTimeout(TimeSpan timeout)
    {
        if (timeout != Timeout.InfiniteTimeSpan && timeout <= TimeSpan.Zero)
            throw new TypeSafeException("timeout must be a positive, finite number of seconds.");
    }

    private static IReadOnlyDictionary<string, TypeSafeQuestion> ConvertQuestions(IReadOnlyDictionary<string, Question> questions) =>
        questions is null ? throw new TypeSafeException("At least one question is required.")
            : questions.ToDictionary(pair => pair.Key, pair => (TypeSafeQuestion)pair.Value);

    private static string? ResolveEnvironment(string name)
    {
        var value = Environment.GetEnvironmentVariable(name)?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsHttpClient) _httpClient.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return default;
    }
}