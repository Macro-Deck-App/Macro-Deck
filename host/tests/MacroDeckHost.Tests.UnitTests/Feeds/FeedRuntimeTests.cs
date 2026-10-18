using MacroDeckHost.Integrations.Feeds.Configuration;
using MacroDeckHost.Integrations.Feeds.Connections;
using MacroDeckHost.Integrations.Feeds.Protocol;

namespace MacroDeckHost.Tests.UnitTests.Feeds;

[TestFixture]
public class FeedRuntimeTests
{
	[Test]
	public async Task A_tap_waiting_on_a_feed_that_is_being_replaced_does_not_hang()
	{
		var fetcher = new HangingFetcher();
		var settings = new FeedSettings(Guid.NewGuid(), "News", new Uri("https://news.example.com/feed.xml"), "news");
		var runtime = new FeedRuntime(settings,
			fetcher,
			new MemoryStorage(),
			TimeProvider.System,
			(_, _, _) =>
			{
			});
		var refresh = runtime.RefreshAsync(CancellationToken.None);
		await fetcher.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

		var tap = runtime.MarkReadAsync("item", CancellationToken.None);
		await runtime.DisposeAsync();

		Assert.That(async () => await tap.WaitAsync(TimeSpan.FromSeconds(10)),
			Throws.InstanceOf<OperationCanceledException>());
		Assert.That(async () => await refresh.WaitAsync(TimeSpan.FromSeconds(10)),
			Throws.InstanceOf<OperationCanceledException>());
	}

	private sealed class HangingFetcher : IFeedFetcher
	{
		public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public async Task<FeedFetchResult> FetchAsync(FeedRequest request, CancellationToken cancellationToken)
		{
			Started.TrySetResult();
			await Task.Delay(Timeout.Infinite, cancellationToken);
			throw new InvalidOperationException();
		}
	}
}
