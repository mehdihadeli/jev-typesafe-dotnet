using Jev.Typesafe.Sdk;
using Jev.Typesafe.Sdk.Exceptions;
using Jev.Typesafe.Sdk.Models;

if (args.Contains("--help"))
{
    Console.WriteLine("Core SDK sample: classify a support ticket, or use --list-models for model discovery.");
    Console.WriteLine("Set TYPESAFE_API_KEY before making API calls.");
    return 0;
}

if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TYPESAFE_API_KEY")))
{
    Console.Error.WriteLine("Set TYPESAFE_API_KEY to run this sample. No request was sent.");
    return 1;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

try
{
    using var client = new TypeSafeClient(new TypeSafeClientOptions
    {
        Retry = new RetryPolicy { MaxRetries = 2 },
    });
    if (args.Contains("--list-models"))
    {
        var catalog = await client.Models.ListAsync(cancellationToken: cancellation.Token);
        foreach (var model in catalog.Models)
            Console.WriteLine($"{model.Name}: {model.Description}");
        return 0;
    }

    var questions = new Dictionary<string, TypeSafeQuestion>
    {
        ["billing"] = new Noul { Instructions = "Does this ticket concern billing?" },
        ["team"] = new Choice
        {
            Instructions = "Which team should receive this ticket?",
            Criteria = new Dictionary<string, object?>
            {
                ["billing"] = "Payments, subscriptions, or invoices.",
                ["technical"] = "Product errors or integration failures.",
                ["other"] = "Any other request.",
            },
        },
        ["urgency"] = new Score
        {
            Instructions = "How urgently does this ticket need attention?",
            Criteria = new object?[] { "Routine", "Needs attention this week", "Needs attention today" },
        },
    };
    var response = await client.SystemOneAsync(
        new { Subject = "Duplicate invoice", Message = "My card was charged twice. Please resolve this today." },
        questions, cancellationToken: cancellation.Token);

    Console.WriteLine($"Model: {response.Model}");
    Console.WriteLine($"Billing probability: {response.Nouls["billing"].Noul:F3}");
    Console.WriteLine($"Team: {response.Choices["team"].Choice}");
    Console.WriteLine($"Urgency: {response.Scores["urgency"].Score:F2}");
    Console.WriteLine($"Tokens: {response.Usage?.InputTokens} input, {response.Usage?.OutputTokens} output");
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