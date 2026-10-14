using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Jev.Typesafe.Sdk.Extensions.AI;

public sealed class TypeSafeChatClient : IChatClient
{
    private readonly TypeSafeClient _client;
    private readonly IReadOnlyDictionary<string, TypeSafeQuestion> _questions;
    private readonly bool _ownsClient;
    private readonly ChatClientMetadata _metadata = new(providerName: "typesafe");
    private bool _disposed;

    public TypeSafeChatClient(
        TypeSafeClient client,
        IReadOnlyDictionary<string, TypeSafeQuestion> questions,
        bool ownsClient = false
    )
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _questions = CopyQuestions(questions);
        _ownsClient = ownsClient;
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TypeSafeChatClient));
        }
        if (messages is null)
        {
            throw new ArgumentNullException(nameof(messages));
        }
        cancellationToken.ThrowIfCancellationRequested();
        ValidateOptions(options);

        var history = messages.ToArray();
        if (history.Length == 0)
        {
            throw new ArgumentException("At least one message is required.", nameof(messages));
        }
        if (history.Any(message => message is null
            || (message.Role != ChatRole.User && message.Role != ChatRole.System
                && message.Role != ChatRole.Assistant)
            || message.Contents.Any(content => content is not TextContent)))
        {
            throw new NotSupportedException("Only user, system, and assistant text messages are supported.");
        }

        var typeSafeOptions = options as TypeSafeChatOptions;
        var state = typeSafeOptions?.State ?? history.Select(message => new
        {
            role = message.Role.Value,
            content = message.Text,
        }).ToArray();
        var questions = typeSafeOptions?.Questions is { } requestQuestions
            ? CopyQuestions(requestQuestions)
            : _questions;
        var response = await _client.SystemOneAsync(
            state, questions, options?.ModelId, cancellationToken
        ).ConfigureAwait(false);

        return new ChatResponse(new ChatMessage(
            ChatRole.Assistant, TypeSafeChatSerialization.SerializeAnswers(response.Answers)
        ))
        {
            ModelId = response.Model,
            FinishReason = ChatFinishReason.Stop,
            RawRepresentation = response,
            Usage = response.Usage is { } usage ? new UsageDetails
            {
                InputTokenCount = usage.InputTokens,
                OutputTokenCount = usage.OutputTokens,
                TotalTokenCount = usage.InputTokens is { } input && usage.OutputTokens is { } output
                    ? (long)input + output : null,
            } : null,
        };
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var response = await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        foreach (var update in response.ToChatResponseUpdates())
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType is null)
        {
            throw new ArgumentNullException(nameof(serviceType));
        }
        if (serviceKey is not null)
        {
            return null;
        }
        if (serviceType == typeof(ChatClientMetadata))
        {
            return _metadata;
        }
        if (serviceType == typeof(TypeSafeClient))
        {
            return _client;
        }
        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }

    private static IReadOnlyDictionary<string, TypeSafeQuestion> CopyQuestions(
        IReadOnlyDictionary<string, TypeSafeQuestion> questions
    )
    {
        if (questions is null)
        {
            throw new ArgumentNullException(nameof(questions));
        }
        if (questions.Count == 0 || questions.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null))
        {
            throw new ArgumentException("Provide at least one named, non-null question.", nameof(questions));
        }
        return new ReadOnlyDictionary<string, TypeSafeQuestion>(
            questions.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
        );
    }

    private static void ValidateOptions(ChatOptions? options)
    {
        if (options is null)
        {
            return;
        }
        if (options.Tools is { Count: > 0 } || options.ToolMode is not null
            || options.AllowMultipleToolCalls is not null || options.Temperature is not null
            || options.TopP is not null || options.TopK is not null || options.MaxOutputTokens is not null
            || options.FrequencyPenalty is not null || options.PresencePenalty is not null
            || options.Seed is not null || options.StopSequences is { Count: > 0 }
            || options.ConversationId is not null || options.Instructions is not null
            || options.AllowBackgroundResponses == true || options.ContinuationToken is not null
            || options.Reasoning is not null || options.RawRepresentationFactory is not null
            || options.AdditionalProperties is { Count: > 0 })
        {
            throw new NotSupportedException("TypeSafe supports ModelId, JSON response format, and TypeSafeChatOptions only.");
        }
        if (options.ResponseFormat is not null
            && (options.ResponseFormat is not ChatResponseFormatJson jsonFormat || jsonFormat.Schema is not null))
        {
            throw new NotSupportedException("TypeSafe returns fixed typed answer JSON; text and custom JSON schemas are unsupported.");
        }
    }
}