using System.Globalization;
using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Integrations.Streaming;
using MacroDeckHost.Integrations.Twitch.Widgets;
using MacroDeckHost.Tests.UnitTests.Delegation;
using MacroDeckHost.Tests.UnitTests.Streaming;
using Serilog.Core;
using MacroDeckHost.Integrations.Streaming.Widgets;
using MacroDeckHost.Integrations.Twitch;

namespace MacroDeckHost.Tests.UnitTests.Twitch.Stats;

[TestFixture]
internal sealed class TwitchStatsWidgetTests
{
	private const string UserId = "111";
	private const string Key = "streamer";

	private readonly Dictionary<string, object?> _state = new(StringComparer.Ordinal);
	private FakeTimeProvider _time = null!;
	private StreamChatHub _chat = null!;
	private StreamStatsSource _accounts = null!;
	private FakeThumbnails _thumbnails = null!;
	private FakeStreamingContext _context = null!;
	private StreamWidgetsUiProvider _provider = null!;
	private CultureScope _culture = null!;

	[SetUp]
	public void SetUp()
	{
		_culture = new CultureScope("en-US");
		_state.Clear();
		_time = new FakeTimeProvider();
		_chat = new StreamChatHub(_time, Logger.None);
		_accounts = new StreamStatsSource((_, name) => _state.GetValueOrDefault(name));
		_thumbnails = new FakeThumbnails();
		_context = new FakeStreamingContext(TwitchIntegration.IntegrationId, typeof(TwitchIntegration));
		_provider = StreamPlatformTestSupport.Provider(TwitchStreamPlatform.Platform,
			_chat,
			stats: _accounts,
			thumbnails: _thumbnails,
			context: _context,
			time: _time);
	}

	[TearDown]
	public void TearDown()
	{
		_chat.Dispose();
		_culture.Dispose();
	}

	[TestCase("1234", "1,234")]
	[TestCase("9999", "9,999")]
	[TestCase("12400", "12.4K")]
	[TestCase("123456", "123K")]
	[TestCase("2500000", "2.5M")]
	[TestCase(null, "-")]
	[TestCase("abc", "-")]
	public void Counts_read_the_way_the_concept_shows_them(string? value, string expected)
	{
		Assert.That(StreamStatsResolver.Count(value), Is.EqualTo(expected));
	}

	[Test]
	public void Uptime_reads_as_hours_minutes_seconds()
	{
		Assert.That(StreamStatsResolver.Uptime("8066"), Is.EqualTo("02:14:26"));
	}

	[Test]
	public async Task Widget_data_without_a_style_shows_the_default_overview()
	{
		Connect();
		SetLive();

		await using var session = await OpenAsync(new { account = "" });
		var texts = Texts(session.BuildTree().Root);

		Assert.Multiple(() =>
		{
			Assert.That(texts, Has.Some.Contains("1,234"));
			Assert.That(texts, Has.Some.Contains("256"));
			Assert.That(texts, Has.Some.Contains("12.4K"));
			Assert.That(texts, Has.Some.Contains("Exploring Night City"));
			Assert.That(texts, Has.Some.Contains("Cyberpunk 2077"));
			Assert.That(texts, Has.Some.Contains("02:14:26"));
			Assert.That(texts, Has.None.Contains("842"), "subscribers are not a default tile");
		});
	}

	[TestCase("overview", new[] { "1,234", "256", "12.4K", "Exploring Night City" }, new[] { "842" })]
	[TestCase("statsRow", new[] { "1,234", "256", "12.4K" }, new[] { "Exploring Night City", "Cyberpunk 2077" })]
	[TestCase("liveRow", new[] { "1,234", "Cyberpunk 2077" }, new[] { "256", "12.4K", "Exploring Night City" })]
	[TestCase("valueGraph", new[] { "1,234" }, new[] { "256", "12.4K", "Cyberpunk 2077" })]
	[TestCase("value", new[] { "1,234" }, new[] { "256", "12.4K", "Cyberpunk 2077" })]
	public async Task The_chosen_style_alone_decides_what_is_shown(string style, string[] shown, string[] hidden)
	{
		Connect();
		SetLive();

		await using var session = await OpenAsync(new { account = "", style });
		var root = session.BuildTree().Root;
		var texts = Texts(root);

		Assert.Multiple(() =>
		{
			Assert.That(Flatten(root).Any(node => node.Type == UiComponents.Responsive),
				Is.False,
				"the content must not switch with the box size");

			foreach (var value in shown)
			{
				Assert.That(texts, Has.Some.Contains(value), value);
			}

			foreach (var value in hidden)
			{
				Assert.That(texts, Has.None.Contains(value), value);
			}
		});
	}

	[TestCase("viewers", "1,234")]
	[TestCase("chatters", "256")]
	[TestCase("followers", "12.4K")]
	[TestCase("subscribers", "842")]
	public async Task A_single_value_style_shows_the_chosen_metric(string metric, string expected)
	{
		Connect();
		SetLive();

		await using var session = await OpenAsync(new { account = "", style = "value", metric });
		var texts = Texts(session.BuildTree().Root);

		Assert.Multiple(() =>
		{
			Assert.That(texts, Has.Some.Contains(expected));
			Assert.That(texts, Has.None.Contains(expected == "1,234" ? "842" : "1,234"));
		});
	}

	[Test]
	public async Task The_graph_follows_the_chosen_metric()
	{
		Connect();
		SetLive();

		await using var session = await OpenAsync(new { account = "", style = "valueGraph", metric = "followers" });
		await WaitForAsync(() => _context.OwnVariables.Opened.Count > 0);

		Assert.That(_context.OwnVariables.Opened, Is.EqualTo(new[] { $"twitch_{Key}_follower_count" }));
	}

	[Test]
	public async Task Styles_without_a_graph_sample_no_history()
	{
		Connect();
		SetLive();

		await using var session = await OpenAsync(new { account = "", style = "value" });
		await Task.Delay(50);

		Assert.That(_context.OwnVariables.Opened, Is.Empty);
	}

	[Test]
	public async Task Tiles_follow_the_stored_order_and_ignore_unknown_ids()
	{
		Connect();
		SetLive();

		await using var session = await OpenAsync(new
		{
			account = "",
			style = "statsRow",
			tiles = new[] { "subscribers", "bits", "viewers" }
		});
		var texts = Texts(session.BuildTree().Root);

		Assert.Multiple(() =>
		{
			Assert.That(texts.FindIndex(text => text.Contains("842")),
				Is.LessThan(texts.FindIndex(text => text.Contains("1,234"))));
			Assert.That(texts, Has.None.Contains("256"));
		});
	}

	[Test]
	public async Task Subscribers_read_as_a_dash_while_their_count_is_not_known()
	{
		Connect();
		SetLive();
		_state["subscriber_count"] = null;

		await using var session = await OpenAsync(new { account = "", style = "value", metric = "subscribers" });

		Assert.That(Texts(session.BuildTree().Root), Has.Some.EqualTo("\"-\""));
	}

	[Test]
	public async Task The_widget_follows_a_change_of_the_accounts_state()
	{
		Connect();
		SetLive();
		await using var session = await OpenAsync();

		Set("viewer_count", 77);
		_time.Advance(StreamStatsWidgetSession.RefreshInterval);

		Assert.That(Texts(session.BuildTree().Root), Has.Some.Contains("77"));
	}

	[Test]
	public async Task Offline_shows_dashes_for_the_live_numbers_and_keeps_followers_and_subscribers()
	{
		Connect();
		SetLive();
		Set("is_live", false);

		await using var session = await OpenAsync(new
		{
			account = "",
			style = "statsRow",
			tiles = new[] { "viewers", "chatters", "followers", "subscribers" }
		});
		var texts = Texts(session.BuildTree().Root);

		Assert.Multiple(() =>
		{
			Assert.That(texts, Has.None.Contains("1,234"));
			Assert.That(texts, Has.None.Contains("256"));
			Assert.That(texts, Has.Some.EqualTo("\"-\""));
			Assert.That(texts, Has.Some.Contains("12.4K"));
			Assert.That(texts, Has.Some.Contains("842"));
		});
	}

	[Test]
	public async Task The_thumbnail_is_tracked_only_while_the_stream_is_live()
	{
		Connect();
		SetLive();
		await using var session = await OpenAsync();

		Assert.That(_thumbnails.Tracked[^1],
			Is.EqualTo((UserId, "https://static-cdn.jtvnw.net/previews-ttv/live_user_streamer-440x248.jpg")));

		Set("is_live", false);
		_time.Advance(StreamStatsWidgetSession.RefreshInterval);

		Assert.That(_thumbnails.Tracked[^1], Is.EqualTo((UserId, (string?)null)));
	}

	[Test]
	public async Task Losing_the_account_leaves_a_widget_without_values()
	{
		Connect();
		SetLive();
		await using var session = await OpenAsync();

		_accounts.SetAccounts([]);

		Assert.That(Texts(session.BuildTree().Root), Has.None.Contains("1,234"));
	}

	[Test]
	public async Task A_sample_needs_no_account_and_no_variables()
	{
		await using var session = (await _provider.CreateSessionAsync(
			new UiSessionRequest { Surface = Surface(UiSurfaceKinds.Preview, true), UiModelVersion = 1 },
			CancellationToken.None))!;

		Assert.That(Texts(session.BuildTree().Root), Has.Some.Contains("Cyberpunk 2077"));
	}

	[Test]
	public async Task The_graph_draws_a_chart_only_once_there_are_two_samples()
	{
		Connect();
		SetLive();

		await using var empty = await OpenAsync(new { account = "", style = "valueGraph" });
		await WaitForAsync(() => _context.OwnVariables.Opened.Count > 0);

		Assert.That(Flatten(empty.BuildTree().Root).Any(node => node.Type == UiComponents.Chart), Is.False);

		_context.OwnVariables.Sample($"twitch_{Key}_viewer_count", 10);
		_context.OwnVariables.Sample($"twitch_{Key}_viewer_count", 20);

		await WaitForAsync(() => Flatten(empty.BuildTree().Root).Any(node => node.Type == UiComponents.Chart));
	}

	[Test]
	public async Task The_stats_widget_is_served_for_its_own_surfaces_only()
	{
		await using var widget = await _provider.CreateSessionAsync(
			new UiSessionRequest { Surface = Surface(UiSurfaceKinds.Widget), UiModelVersion = 1 },
			CancellationToken.None);
		var dialog = await _provider.CreateSessionAsync(
			new UiSessionRequest { Surface = Surface(UiSurfaceKinds.Dialog), UiModelVersion = 1 },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(widget, Is.Not.Null);
			Assert.That(dialog, Is.Null);
		});
	}

	[Test]
	public async Task The_widget_shows_the_integrations_own_logo()
	{
		Connect();
		SetLive();

		await using var session = await OpenAsync();
		var logo = Flatten(session.BuildTree().Root)
			.First(node => node.Type == UiComponents.Image
				&& node.Properties.ContainsKey(UiComponentProperties.Source));
		var resourceId = logo.Properties[UiComponentProperties.Source].GetProperty("resourceId").GetString()!;

		Assert.That(_context.Store.TryGet(resourceId, out var content) && content.MediaType == "image/svg+xml",
			Is.True);
	}

	private void Connect()
	{
		_accounts.SetAccounts([new StreamStatsAccount(UserId, "Streamer", $"twitch_{Key}_")]);
	}

	private void SetLive()
	{
		Set("is_live", true);
		Set("viewer_count", 1234);
		Set("chatter_count", 256);
		Set("follower_count", 12400);
		Set("subscriber_count", 842);
		Set("stream_title", "Exploring Night City");
		Set("stream_category", "Cyberpunk 2077");
		Set("uptime_seconds", 8066);
		Set("stream_thumbnail_url", "https://static-cdn.jtvnw.net/previews-ttv/live_user_streamer-440x248.jpg");
	}

	private void Set(string name, object? value)
	{
		_state[name] = value;
	}

	private static async Task WaitForAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(5);

		while (!condition())
		{
			Assert.That(DateTime.UtcNow, Is.LessThan(deadline), "the widget never reached the expected state");
			await Task.Delay(10);
		}
	}

	private async Task<IUiSession> OpenAsync(object? data = null)
	{
		return (await _provider.CreateSessionAsync(new UiSessionRequest
				{ Surface = Surface(UiSurfaceKinds.Widget, data: data), UiModelVersion = 1 },
			CancellationToken.None))!;
	}

	private static UiSurface Surface(string kind, bool sample = false, object? data = null)
	{
		var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
		{
			[UiWidgetSurfaceAttributes.WidgetType]
				= JsonSerializer.SerializeToElement(TwitchStreamPlatform.Platform.StatsWidgetTypeId),
			[UiWidgetSurfaceAttributes.Data] = JsonSerializer.SerializeToElement(data ?? new { account = "" })
		};

		if (sample)
		{
			attributes[UiWidgetSurfaceAttributes.Sample] = JsonSerializer.SerializeToElement(true);
		}

		return new UiSurface { Kind = kind, SessionMode = UiSessionModes.Shared, Attributes = attributes };
	}

	private static IEnumerable<UiNode> Flatten(UiNode node)
	{
		yield return node;

		foreach (var child in node.Children.SelectMany(Flatten))
		{
			yield return child;
		}
	}

	private static List<string> Texts(UiNode root)
	{
		return
		[
			.. Flatten(root)
				.Where(node => node.Properties.ContainsKey(UiComponentProperties.Text))
				.Select(node => node.Properties[UiComponentProperties.Text].GetRawText())
		];
	}

	private sealed class CultureScope : IDisposable
	{
		private readonly CultureInfo _previous
			= CultureInfo.CurrentCulture;

		public CultureScope(string name)
		{
			CultureInfo.CurrentCulture = new CultureInfo(name);
		}

		public void Dispose()
		{
			CultureInfo.CurrentCulture = _previous;
		}
	}

	private sealed class FakeThumbnails : IStreamThumbnails
	{
		public List<(string UserId, string? Url)> Tracked { get; } = [];

		public event EventHandler<string>? Changed
		{
			add { }
			remove { }
		}

		public MacroDeck.Ui.Model.Resources.UiResource? Find(string userId)
		{
			return null;
		}

		public void Track(string userId, string? url)
		{
			Tracked.Add((userId, url));
		}
	}
}
