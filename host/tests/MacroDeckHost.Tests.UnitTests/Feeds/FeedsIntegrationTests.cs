using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeckHost.Integrations.Feeds;
using MacroDeckHost.Integrations.Feeds.Configuration;
using MacroDeckHost.Integrations.Feeds.Events;
using MacroDeckHost.Integrations.Feeds.State;
using MacroDeckHost.Tests.UnitTests.Delegation;

namespace MacroDeckHost.Tests.UnitTests.Feeds;

[TestFixture]
internal sealed class FeedsIntegrationTests
{
	private const string NewsUrl = "https://news.example.com/feed.xml";
	private const string BlogUrl = "https://blog.example.com/atom.xml";

	private static readonly Guid _newsId = Guid.Parse("0b6f4b7e-0000-0000-0000-000000000001");
	private static readonly Guid _blogId = Guid.Parse("0b6f4b7e-0000-0000-0000-000000000002");

	private readonly List<FeedsIntegration> _integrations = [];

	private FakeFeedServer _server = null!;
	private FakeTimeProvider _time = null!;
	private MemoryStorage _storage = null!;

	[SetUp]
	public void SetUp()
	{
		_server = new FakeFeedServer();
		_time = new FakeTimeProvider();
		_storage = new MemoryStorage();
	}

	[TearDown]
	public async Task TearDown()
	{
		foreach (var integration in _integrations)
		{
			await integration.ShutdownAsync();
		}

		_server.Dispose();
	}

	[Test]
	public async Task Adding_a_feed_publishes_nothing_for_the_articles_it_already_has()
	{
		_server.ServeXml(NewsUrl, News(First, Second));
		var (integration, context) = await StartAsync(feeds => feeds.AddFeed("News", NewsUrl, "news", id: _newsId));

		await integration.RefreshAsync(_newsId);

		var feed = integration.Feeds.Find(_newsId)!;
		await Assert.MultipleAsync(async () =>
		{
			Assert.That(context.RecordedEvents.Published, Is.Empty);
			Assert.That(feed.Items.Select(item => item.Title), Is.EqualTo(new[] { "Second", "First" }));
			Assert.That(feed.Status, Is.EqualTo(FeedStatus.Ok));
			Assert.That(await ReadAsync(integration, "feeds_news_unread_count"), Is.EqualTo(0));
		});
	}

	[Test]
	public async Task A_new_article_fires_one_event_with_its_context()
	{
		_server.ServeXml(NewsUrl, News(First, Second));
		var (integration, context) = await StartAsync(feeds => feeds.AddFeed("News", NewsUrl, "news", id: _newsId));
		await integration.RefreshAsync(_newsId);

		_server.ServeXml(NewsUrl, News(First, Second, Third));
		await integration.RefreshAsync(_newsId);
		await integration.RefreshAsync(_newsId);

		var created = context.RecordedEvents.Of(FeedEventIds.NewItem);
		Assert.Multiple(() =>
		{
			Assert.That(created, Has.Count.EqualTo(1));
			Assert.That(created[0][FeedEventPayload.Feed], Is.EqualTo(_newsId.ToString("D")));
			Assert.That(created[0][FeedEventPayload.FeedTitle], Is.EqualTo("News"));
			Assert.That(created[0][FeedEventPayload.Title], Is.EqualTo("Third"));
			Assert.That(created[0][FeedEventPayload.Url], Is.EqualTo("https://news.example.com/3"));
			Assert.That(created[0][FeedEventPayload.Author], Is.EqualTo("Ada"));
			Assert.That(created[0][FeedEventPayload.Categories], Is.EqualTo("Space, Science"));
			Assert.That(created[0][FeedEventPayload.Published], Is.EqualTo("2026-06-03T10:00:00.0000000+00:00"));
			Assert.That(context.RecordedEvents.Of(FeedEventIds.Updated), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task A_restart_fires_nothing_for_articles_seen_before()
	{
		_server.ServeXml(NewsUrl, News(First, Second));
		var (before, _) = await StartAsync(feeds => feeds.AddFeed("News", NewsUrl, "news", id: _newsId));
		await before.RefreshAsync(_newsId);
		_server.ServeXml(NewsUrl, News(First, Second, Third));
		await before.RefreshAsync(_newsId);
		await before.ShutdownAsync();

		var (after, context) = await StartAsync(feeds => feeds.AddFeed("News", NewsUrl, "news", id: _newsId));
		var restored = after.Feeds.Find(_newsId)!;
		_server.ServeXml(NewsUrl, News(First, Second, Third, Fourth));
		await after.RefreshAsync(_newsId);

		Assert.Multiple(() =>
		{
			Assert.That(restored.Items, Has.Count.EqualTo(3), "the cached articles were not restored");
			Assert.That(NewTitles(context), Is.EqualTo(new[] { "Fourth" }));
		});
	}

	[Test]
	public async Task Changing_the_address_starts_over_with_a_new_baseline()
	{
		_server.ServeXml(NewsUrl, News(First));
		_server.ServeXml(BlogUrl, News(Second, Third));
		var (integration, context) = await StartAsync(feeds => feeds.AddFeed("News", NewsUrl, "news", id: _newsId));
		await integration.RefreshAsync(_newsId);

		context.Set(_newsId, FeedConfigKeys.Url, BlogUrl);
		await integration.ReloadConfigurationsAsync(CancellationToken.None);
		await integration.RefreshAsync(_newsId);

		Assert.Multiple(() =>
		{
			Assert.That(context.RecordedEvents.Of(FeedEventIds.NewItem), Is.Empty);
			Assert.That(integration.Feeds.Find(_newsId)!.Items.Select(item => item.Title),
				Is.EqualTo(new[] { "Third", "Second" }));
		});
	}

	[Test]
	public async Task Filtered_articles_count_as_seen_so_widening_the_filter_fires_nothing()
	{
		_server.ServeXml(NewsUrl, News(Item("1", "Rocket one", 1), Item("2", "Cooking two", 2)));
		var (integration, context) = await StartAsync(feeds => feeds.AddFeed("News",
			NewsUrl,
			"news",
			values => values[FeedConfigKeys.IncludeKeywords] = "rocket",
			_newsId));
		await integration.RefreshAsync(_newsId);

		_server.ServeXml(NewsUrl,
			News(Item("1", "Rocket one", 1),
				Item("2", "Cooking two", 2),
				Item("3", "Cooking three", 3),
				Item("4", "Rocket four", 4)));
		await integration.RefreshAsync(_newsId);
		var filteredTitles = integration.Feeds.Find(_newsId)!.Items.Select(item => item.Title).ToList();

		context.Set(_newsId, FeedConfigKeys.IncludeKeywords, null);
		await integration.ReloadConfigurationsAsync(CancellationToken.None);
		await integration.RefreshAsync(_newsId);

		Assert.Multiple(() =>
		{
			Assert.That(filteredTitles, Is.EqualTo(new[] { "Rocket four", "Rocket one" }));
			Assert.That(NewTitles(context), Is.EqualTo(new[] { "Rocket four" }));
			Assert.That(integration.Feeds.Find(_newsId)!.Items, Has.Count.EqualTo(4));
		});
	}

	[Test]
	public async Task Variables_cover_each_feed_and_every_feed_together()
	{
		_server.ServeXml(NewsUrl, News(First, Second));
		_server.ServeXml(BlogUrl, News(Third));
		var (integration, _) = await StartAsync(feeds =>
		{
			feeds.AddFeed("News", NewsUrl, "news", id: _newsId);
			feeds.AddFeed("Blog", BlogUrl, "blog", id: _blogId);
		});
		await integration.RefreshAsync(_newsId);
		await integration.RefreshAsync(_blogId);
		_server.ServeXml(BlogUrl, News(Third, Fourth));
		await integration.RefreshAsync(_blogId);

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(await ReadAsync(integration, "feeds_news_latest_title"), Is.EqualTo("Second"));
			Assert.That(await ReadAsync(integration, "feeds_blog_latest_url"),
				Is.EqualTo("https://news.example.com/4"));
			Assert.That(await ReadAsync(integration, "feeds_blog_item_count"), Is.EqualTo(2));
			Assert.That(await ReadAsync(integration, "feeds_blog_unread_count"), Is.EqualTo(1));
			Assert.That(await ReadAsync(integration, "feeds_news_update_status"), Is.EqualTo(FeedStatus.Ok));
			Assert.That(await ReadAsync(integration, "feeds_all_item_count"), Is.EqualTo(4));
			Assert.That(await ReadAsync(integration, "feeds_all_unread_count"), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task No_feed_can_take_the_key_the_aggregates_use()
	{
		var integration = new FeedsIntegration(FeedsTestClients.Create(_server), _time, false);

		var prepared = await integration.PrepareEntryAsync(new ConfigEntryPreparation
		{
			EntryId = Guid.NewGuid(),
			Title = "All",
			Values = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
		});

		Assert.That(prepared.Values![FeedConfigKeys.VariableKey].GetString(), Is.Not.EqualTo("all"));
	}

	[Test]
	public async Task Opening_an_article_marks_it_read_and_tells_which_widget_opened_it()
	{
		_server.ServeXml(NewsUrl, News(First));
		var (integration, context) = await StartAsync(feeds => feeds.AddFeed("News", NewsUrl, "news", id: _newsId));
		await integration.RefreshAsync(_newsId);
		_server.ServeXml(NewsUrl, News(First, Second));
		await integration.RefreshAsync(_newsId);
		var unreadBefore = await ReadAsync(integration, "feeds_news_unread_count");
		var widgetId = Guid.NewGuid();

		await integration.OpenAsync(_newsId, integration.Feeds.Find(_newsId)!.Items[0].Key, widgetId);

		var opened = context.RecordedEvents.Of(FeedEventIds.ItemOpened).Single();
		await Assert.MultipleAsync(async () =>
		{
			Assert.That(unreadBefore, Is.EqualTo(1));
			Assert.That(await ReadAsync(integration, "feeds_news_unread_count"), Is.EqualTo(0));
			Assert.That(opened[FeedEventPayload.Title], Is.EqualTo("Second"));
			Assert.That(opened[FeedEventPayload.Widget], Is.EqualTo(widgetId.ToString("D")));
		});
	}

	[Test]
	public async Task A_failing_feed_reports_a_stable_code_once_and_backs_off()
	{
		_server.Serve(NewsUrl, _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
		var (integration, context) = await StartAsync(feeds => feeds.AddFeed("News", NewsUrl, "news", id: _newsId));
		var runtime = integration.Runtimes.Single();

		await integration.RefreshAsync(_newsId);
		var firstDelay = runtime.NextDelay();
		await integration.RefreshAsync(_newsId);
		var secondDelay = runtime.NextDelay();

		var failures = context.RecordedEvents.Of(FeedEventIds.UpdateFailed);
		await Assert.MultipleAsync(async () =>
		{
			Assert.That(failures, Has.Count.EqualTo(1));
			Assert.That(failures[0][FeedEventPayload.Error], Is.EqualTo("http-500"));
			Assert.That(await ReadAsync(integration, "feeds_news_update_status"), Is.EqualTo(FeedStatus.Error));
			Assert.That(firstDelay, Is.EqualTo(TimeSpan.FromMinutes(1)));
			Assert.That(secondDelay, Is.EqualTo(TimeSpan.FromMinutes(2)));
		});
	}

	[Test]
	public async Task A_retry_after_answer_waits_as_long_as_the_server_asks()
	{
		_server.Serve(NewsUrl,
			_ =>
			{
				var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
				response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(45));
				return response;
			});
		var (integration, _) = await StartAsync(feeds => feeds.AddFeed("News", NewsUrl, "news", id: _newsId));

		await integration.RefreshAsync(_newsId);

		Assert.That(integration.Runtimes.Single().NextDelay(), Is.EqualTo(TimeSpan.FromMinutes(45)));
	}

	[Test]
	public async Task A_successful_refresh_waits_for_the_feeds_own_interval()
	{
		_server.ServeXml(NewsUrl, News(First));
		var (integration, _) = await StartAsync(feeds => feeds.AddFeed("News",
			NewsUrl,
			"news",
			values => values[FeedConfigKeys.RefreshMinutes] = "2",
			_newsId));

		await integration.RefreshAsync(_newsId);

		Assert.That(integration.Runtimes.Single().NextDelay(),
			Is.EqualTo(TimeSpan.FromMinutes(FeedSettings.MinRefreshMinutes)));
	}

	internal static string First => Item("1", "First", 1);

	internal static string Second => Item("2", "Second", 2);

	internal static string Third => Item("3",
		"Third",
		3,
		"<dc:creator>Ada</dc:creator><category>Space</category><category>Science</category>");

	internal static string Fourth => Item("4", "Fourth", 4);

	internal static string Item(string id, string title, int day, string extra = "")
	{
		return FeedXml.Item(id, title, $"https://news.example.com/{id}", $"0{day} Jun 2026 10:00:00 GMT", extra);
	}

	internal static string News(params string[] items)
	{
		return FeedXml.Rss(items);
	}

	private static IEnumerable<object?> NewTitles(FeedsTestContext context)
	{
		return context.RecordedEvents.Of(FeedEventIds.NewItem).Select(payload => payload[FeedEventPayload.Title]);
	}

	private static async Task<object?> ReadAsync(FeedsIntegration integration, string name)
	{
		var definition = integration.Variables.Single(variable => variable.Name == name);
		return (await integration.ReadAsync(definition.Id!)).Value;
	}

	private async Task<(FeedsIntegration Integration, FeedsTestContext Context)> StartAsync(
		Action<FeedsTestContext> configure)
	{
		var context = new FeedsTestContext(_storage);
		configure(context);
		var integration = new FeedsIntegration(FeedsTestClients.Create(_server, null, _time), _time, false);
		_integrations.Add(integration);
		await integration.InitializeAsync(context);
		return (integration, context);
	}
}
