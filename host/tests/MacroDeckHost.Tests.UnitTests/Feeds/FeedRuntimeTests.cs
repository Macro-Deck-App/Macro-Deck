using System.Text;
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

	[Test]
	public async Task A_feed_with_hostile_markup_refreshes_promptly_and_can_be_removed()
	{
		var markup = new StringBuilder().Insert(0, "<script>a</script><style>", 60_000).ToString();
		var xml = FeedXml.Rss([
			FeedXml.Item("1",
				"Title",
				"https://news.example.com/1",
				"Mon, 01 Jun 2026 10:00:00 GMT",
				$"<description><![CDATA[{markup}]]></description>")
		]);
		var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var settings = new FeedSettings(Guid.NewGuid(), "News", new Uri("https://news.example.com/feed.xml"), "news");
		var runtime = new FeedRuntime(settings,
			new ServingFetcher(Encoding.UTF8.GetBytes(xml)),
			new MemoryStorage(),
			TimeProvider.System,
			(_, _, _) => refreshed.TrySetResult());
		runtime.Start();

		await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(10));

		Assert.That(async () => await runtime.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)),
			Throws.Nothing);
	}

	private sealed class ServingFetcher(byte[] content) : IFeedFetcher
	{
		public Task<FeedFetchResult> FetchAsync(FeedRequest request, CancellationToken cancellationToken)
		{
			return Task.FromResult(
				new FeedFetchResult(FeedFetchStatus.Ok) { Content = content, FinalUrl = request.Url });
		}
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
