using Jev.Typesafe.Sdk;
using Jev.Typesafe.Sdk.Exceptions;
using Jev.Typesafe.Sdk.Extensions.AI;
using Jev.Typesafe.Sdk.Extensions.AI.Evaluation;
using Jev.Typesafe.Sdk.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

if (args.Contains("--help"))
{
    Console.WriteLine("AI sample: classify a message through IChatClient, then evaluate the answer locally.");
    Console.WriteLine("Use --offline to evaluate a fixed answer without credentials or an API call.");
    return 0;
}

var offline = args.Contains("--offline");
if (!offline && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TYPESAFE_API_KEY")))
{
    Console.Error.WriteLine("Set TYPESAFE_API_KEY for chat classification, or use --offline for local evaluation.");
    return 1;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

ChatMessage[] messages = [new(ChatRole.User, "The new dashboard is exactly what I needed. Thank you!")];
try
{
    ChatResponse response;
    if (offline)
    {
        response = new ChatResponse(new ChatMessage(ChatRole.Assistant,
            """{"sentiment":{"type":"choice","choice":"positive","confidence":1,"probabilities":{"positive":1,"negative":0,"neutral":0}}}"""));
    }
    else
    {
        using var core = new TypeSafeClient();
        using IChatClient chat = core.AsChatClient(new Dictionary<string, TypeSafeQuestion>
        {
            ["sentiment"] = new Choice
            {
                Instructions = "Classify the sentiment expressed by the customer.",
                Criteria = new Dictionary<string, object?>
                {
                    ["positive"] = "Satisfied or appreciative.",
                    ["negative"] = "Dissatisfied or critical.",
                    ["neutral"] = "No clear positive or negative sentiment.",
                },
            },
        });
        response = await chat.GetResponseAsync(messages, new TypeSafeChatOptions
        {
            State = new { Message = messages[0].Text, Channel = "email" },
            ResponseFormat = ChatResponseFormat.Json,
        }, cancellation.Token);
        if (response.RawRepresentation is SystemOneResponse classified)
            Console.WriteLine($"Model: {classified.Model}");
    }

    Console.WriteLine($"Answer: {response.Text}");
    var evaluator = new TypeSafeAnswerEvaluator();
    var evaluation = await evaluator.EvaluateAsync(messages, response, additionalContext:
    [
        new TypeSafeAnswerEvaluationContext(new Dictionary<string, TypeSafeAnswer>
        {
            ["sentiment"] = new ChoiceAnswer { Type = "choice", Choice = "positive" },
        }),
    ], cancellationToken: cancellation.Token);
    var accuracy = evaluation.Get<NumericMetric>(TypeSafeAnswerEvaluator.AccuracyMetricName).Value;
    Console.WriteLine($"{TypeSafeAnswerEvaluator.AccuracyMetricName}: {accuracy:P0}");
    Console.WriteLine(offline ? "Offline evaluation completed; no API request was made." : "Evaluation completed locally without another API request.");
    return 0;
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.Error.WriteLine("Cancelled.");
    return 2;
}
catch (TypeSafeApiException failure)
{
    Console.Error.WriteLine($"API failure: HTTP {failure.Status} ({failure.GetType().Name}).");
    return 1;
}
catch (TypeSafeException failure)
{
    Console.Error.WriteLine($"SDK failure: {failure.GetType().Name}. Check credentials, configuration, and connectivity.");
    return 1;
}