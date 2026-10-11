using Typesafe.Sdk;

namespace Typesafe.Sdk.IntegrationTests;

public abstract class TypeSafeIntegrationTestBase
{
    public static bool LiveTestsEnabled =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TYPESAFE_API_KEY"));

    protected static (TypeSafeClient Client, CancellationToken CancellationToken) CreateLiveClient()
    {
        var apiKey = Environment.GetEnvironmentVariable("TYPESAFE_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "TYPESAFE_API_KEY was not found in the environment."
            );
        }

        return (
            new TypeSafeClient(options: new TypeSafeClientOptions { ApiKey = apiKey }),
            TestContext.Current.CancellationToken
        );
    }
}
