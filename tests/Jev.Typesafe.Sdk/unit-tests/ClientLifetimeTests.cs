using Jev.Typesafe.Sdk;

namespace Jev.Typesafe.Sdk.UnitTests;

public sealed class ClientLifetimeTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SuppliedHttpClient_DefaultClosesAndLeaveOpenRetains_Idempotent(bool leaveOpen, bool asyncFirst)
    {
        var handler = ParityTestHelper.Handler("{\"models\":[]}");
        using var http = new HttpClient(handler);
        var client = new TypeSafeClient(new TypeSafeClientOptions { ApiKey = "test", HttpClient = http, LeaveOpen = leaveOpen });
        (await client.Models.ListAsync(cancellationToken: TestContext.Current.CancellationToken)).Models.Count.ShouldBe(0);
        if (asyncFirst) await client.DisposeAsync();
        else client.Dispose();
        client.Dispose();
        await client.DisposeAsync();
        handler.DisposeCount.ShouldBe(leaveOpen ? 0 : 1);
        await Should.ThrowAsync<ObjectDisposedException>(() => client.Models.ListAsync());
        if (leaveOpen)
        {
            using var response = await http.GetAsync("https://example.test", TestContext.Current.CancellationToken);
            ((int)response.StatusCode).ShouldBe(200);
            handler.Requests.Count.ShouldBe(2);
        }
        else
        {
            await Should.ThrowAsync<ObjectDisposedException>(() => http.GetAsync("https://example.test"));
            handler.Requests.Count.ShouldBe(1);
        }
    }

    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("cancellation")]
    public async Task AwaitUsing_OwnedHandler_DisposesOnceAndPreservesFailure(string outcome)
    {
        using var cancellation = new CancellationTokenSource();
        var failure = new InvalidOperationException("original failure");
        var cancelled = new OperationCanceledException(cancellation.Token);
        var handler = new ParityHandler((_, _, _) =>
        {
            if (outcome == "failure") throw failure;
            if (outcome == "cancellation") { cancellation.Cancel(); throw cancelled; }
            return Task.FromResult(ParityTestHelper.Response("{\"models\":[]}"));
        });
        var client = ParityTestHelper.Client(handler);
        try
        {
            await using (client)
            {
                if (outcome == "failure") (await Should.ThrowAsync<InvalidOperationException>(() => client.Models.ListAsync())).ShouldBeSameAs(failure);
                else if (outcome == "cancellation")
                {
                    var error = await Should.ThrowAsync<OperationCanceledException>(() => client.Models.ListAsync(cancellationToken: cancellation.Token));
                    error.ShouldNotBeAssignableTo<TypeSafeException>();
                    error.CancellationToken.IsCancellationRequested.ShouldBeTrue();
                    cancellation.IsCancellationRequested.ShouldBeTrue();
                }
                else (await client.Models.ListAsync(cancellationToken: TestContext.Current.CancellationToken)).Models.Count.ShouldBe(0);
            }
        }
        finally { await client.DisposeAsync(); }
        handler.Requests.Count.ShouldBe(1);
        handler.DisposeCount.ShouldBe(1);
        await Should.ThrowAsync<ObjectDisposedException>(() => client.Models.ListAsync());
    }
}