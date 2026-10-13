using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Typesafe.Sdk;

public sealed class TypeSafeClient : IDisposable
{
    private const string DefaultBaseUrl = "https://api.typesafe.ai/";
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new TypeSafeQuestionConverter(), new TypeSafeAnswerConverter() },
        };

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly TypeSafeClientOptions _options;

    public TypeSafeClient(HttpClient? httpClient = null, TypeSafeClientOptions? options = null)
    {
        _options = options ?? new TypeSafeClientOptions();
        var apiKey = (
            _options.ApiKey ?? Environment.GetEnvironmentVariable("TYPESAFE_API_KEY")
        )?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "No API key was provided. Set ApiKey or TYPESAFE_API_KEY."
            );
        }

        if (_options.MaxRetries < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "MaxRetries must be non-negative."
            );
        }

        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
        _httpClient.BaseAddress ??= new Uri(
            _options.BaseUrl
                ?? Environment.GetEnvironmentVariable("TYPESAFE_BASE_URL")
                ?? DefaultBaseUrl
        );
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            apiKey
        );
    }

    public async Task<SystemOneResponse> SystemOneAsync(
        object state,
        IReadOnlyDictionary<string, TypeSafeQuestion> questions,
        string? model = null,
        CancellationToken cancellationToken = default
    )
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }
        if (questions is null)
        {
            throw new ArgumentNullException(nameof(questions));
        }
        if (questions.Count == 0)
        {
            throw new ArgumentException("At least one question is required.", nameof(questions));
        }

        return await SendAsync<SystemOneRequest, SystemOneResponse>(
                HttpMethod.Post,
                "v1/systemone",
                new SystemOneRequest
                {
                    State = state,
                    Model = model ?? _options.DefaultModel,
                    Questions = questions,
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ModelMetadata>> ListModelsAsync(
        CancellationToken cancellationToken = default
    )
    {
        var response = await SendAsync<object, ModelMetadataList>(
                HttpMethod.Get,
                "v1/models",
                null,
                cancellationToken
            )
            .ConfigureAwait(false);
        return response.Models;
    }

    private async Task<TResponse> SendAsync<TRequest, TResponse>(
        HttpMethod method,
        string path,
        TRequest? body,
        CancellationToken cancellationToken
    )
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body, options: JsonOptions);
            }

            using var response = await _httpClient
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                var result = await response
                    .Content.ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                return result
                    ?? throw new JsonException("TypeSafe API returned an empty response.");
            }

            var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (attempt >= _options.MaxRetries || !IsRetryable(response.StatusCode))
            {
                throw new TypeSafeApiException(response.StatusCode, responseBody);
            }

            await Task.Delay(GetRetryDelay(response, attempt), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter?.Delta is { } retryAfter)
        {
            return retryAfter;
        }

        return TimeSpan.FromMilliseconds(
            _options.RetryDelay.TotalMilliseconds * Math.Pow(2, attempt)
        );
    }

    private static bool IsRetryable(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.RequestTimeout
        || statusCode == (HttpStatusCode)429
        || (int)statusCode >= 500;

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
