using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Twitch.Stats;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Tests.UnitTests.Delegation;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Widgets.TwitchStats;
using DomainEnums = MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Tests.UnitTests.Twitch.Stats;

[TestFixture]
internal sealed class TwitchStatsWidgetTests
{
	private const string UserId = "111";
	private const string Key = "streamer";

	private UiResourceStore _store = null!;
	private VariableRegistry _variables = null!;
	private VariableChangeNotifier _notifier = null!;
	private TwitchStatsAccountsHub _accounts = null!;
	private FakeThumbnails _thumbnails = null!;
	private FakeHistory _history = null!;
	private TwitchStatsWidgetUiProvider _provider = null!;
	private CultureScope _culture = null!;

	[SetUp]
	public void SetUp()
	{
		_culture = new CultureScope("en-US");
		_store = new UiResourceStore();
		_variables = new VariableRegistry();
		_notifier = new VariableChangeNotifier();
		_accounts = new TwitchStatsAccountsHub();
		_thumbnails = new FakeThumbnails();
		_history = new FakeHistory();
		var integrations = new FakeIntegrationRegistry();
		integrations.Add(new IconIntegration());
		_provider = new TwitchStatsWidgetUiProvider(_accounts,
			_thumbnails,
			_variables,
			_history,
			_notifier,
			TestLocalization.SampleText,
			integrations,
			_store);
	}

	[TearDown]
	public void TearDown() => _culture.Dispose();

	[TestCase("1234", "1,234")]
	[TestCase("9999", "9,999")]
	[TestCase("12400", "12.4K")]
	[TestCase("123456", "123K")]
	[TestCase("2500000", "2.5M")]
	[TestCase(null, "-")]
	[TestCase("abc", "-")]
	public void Counts_read_the_way_the_concept_shows_them(string? value, string expected)
	{
		Assert.That(TwitchStatsResolver.Count(value), Is.EqualTo(expected));
	}

	[Test]
	public void Uptime_reads_as_hours_minutes_seconds()
		=> Assert.That(TwitchStatsResolver.Uptime("8066"), Is.EqualTo("02:14:26"));

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
			Assert.That(Flatten(root).Any(node => node.Type == UiComponents.Responsive), Is.False,
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

		Assert.That(_history.Opened, Is.EqualTo(new[] { $"twitch_{Key}_follower_count" }));
	}

	[Test]
	public async Task Styles_without_a_graph_sample_no_history()
	{
		Connect();
		SetLive();

		await using var session = await OpenAsync(new { account = "", style = "value" });

		Assert.That(_history.Opened, Is.Empty);
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
			tiles = new[] { "subscribers", "bits", "viewers" },
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
	public async Task Subscribers_read_as_a_dash_while_their_variable_is_unavailable()
	{
		Connect();
		SetLive();
		_variables.SetAvailable(_variables.FindByName(DomainEnums.VariableScope.Global, null,
			$"twitch_{Key}_subscriber_count")!.Id, false);

		await using var session = await OpenAsync(new { account = "", style = "value", metric = "subscribers" });

		Assert.That(Texts(session.BuildTree().Root), Has.Some.EqualTo("\"-\""));
	}

	[Test]
	public async Task The_widget_follows_a_variable_change()
	{
		Connect();
		SetLive();
		await using var session = await OpenAsync();

		Set("viewer_count", "77");
		_notifier.Publish($"twitch_{Key}_viewer_count");

		Assert.That(Texts(session.BuildTree().Root), Has.Some.Contains("77"));
	}

	[Test]
	public async Task Offline_shows_dashes_for_the_live_numbers_and_keeps_followers_and_subscribers()
	{
		Connect();
		SetLive();
		Set("is_live", "false");

		await using var session = await OpenAsync(new
		{
			account = "",
			style = "statsRow",
			tiles = new[] { "viewers", "chatters", "followers", "subscribers" },
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

		Assert.That(_thumbnails.Tracked[^1], Is.EqualTo((UserId, "https://static-cdn.jtvnw.net/previews-ttv/live_user_streamer-440x248.jpg")));

		Set("is_live", "false");
		_notifier.Publish($"twitch_{Key}_is_live");

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
			new UiSessionRequest { Surface = Surface(UiSurfaceKinds.Preview, sample: true), UiModelVersion = 1 },
			CancellationToken.None))!;

		Assert.That(Texts(session.BuildTree().Root), Has.Some.Contains("Cyberpunk 2077"));
	}

	[Test]
	public async Task The_graph_draws_a_chart_only_once_there_are_two_samples()
	{
		Connect();
		SetLive();

		await using var empty = await OpenAsync(new { account = "", style = "valueGraph" });

		Assert.That(Flatten(empty.BuildTree().Root).Any(node => node.Type == UiComponents.Chart), Is.False);

		_history.Values = [10, 20];
		_history.Raise();

		Assert.That(Flatten(empty.BuildTree().Root).Any(node => node.Type == UiComponents.Chart), Is.True);
	}

	[Test]
	public void The_stats_widget_is_served_for_its_own_surfaces_only()
		=> Assert.Multiple(() =>
		{
			Assert.That(TwitchStatsWidgetUiProvider.Serves(Surface(UiSurfaceKinds.Widget)), Is.True);
			Assert.That(TwitchStatsWidgetUiProvider.Serves(Surface(UiSurfaceKinds.Dialog)), Is.False);
		});

	private void Connect() => _accounts.SetAccounts([new TwitchStatsAccount(UserId, "Streamer", Key)]);

	private void SetLive()
	{
		Set("is_live", "true");
		Set("viewer_count", "1234");
		Set("chatter_count", "256");
		Set("follower_count", "12400");
		Set("subscriber_count", "842");
		Set("stream_title", "Exploring Night City");
		Set("stream_category", "Cyberpunk 2077");
		Set("uptime_seconds", "8066");
		Set("stream_thumbnail_url", "https://static-cdn.jtvnw.net/previews-ttv/live_user_streamer-440x248.jpg");
	}

	private void Set(string name, string value)
	{
		var full = $"twitch_{Key}_{name}";
		var existing = _variables.FindByName(DomainEnums.VariableScope.Global, null, full);

		_variables.Upsert(existing is null
			? new VariableEntity
			{
				Id = Guid.NewGuid(),
				Name = full,
				Scope = DomainEnums.VariableScope.Global,
				Type = DomainEnums.VariableType.Text,
				Classification = DomainEnums.VariableClassification.Integration,
				OwnerIntegrationId = TwitchStatsWidgetType.OwnerId,
				Value = value,
				UpdatedAt = DateTime.UtcNow,
			}
			: existing.CopyWithValue(value));
	}

	private async Task<IUiSession> OpenAsync(object? data = null)
		=> (await _provider.CreateSessionAsync(
			new UiSessionRequest { Surface = Surface(UiSurfaceKinds.Widget, data: data), UiModelVersion = 1 },
			CancellationToken.None))!;

	private static UiSurface Surface(string kind, bool sample = false, object? data = null)
	{
		var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
		{
			[UiWidgetSurfaceAttributes.WidgetType] = JsonSerializer.SerializeToElement(TwitchStatsWidgetType.QualifiedId),
			[UiWidgetSurfaceAttributes.Data] = JsonSerializer.SerializeToElement(data ?? new { account = "" }),
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
		=> [.. Flatten(root).Where(node => node.Properties.ContainsKey(UiComponentProperties.Text))
			.Select(node => node.Properties[UiComponentProperties.Text].GetRawText())];

	private sealed class CultureScope : IDisposable
	{
		private readonly global::System.Globalization.CultureInfo _previous = global::System.Globalization.CultureInfo.CurrentCulture;

		public CultureScope(string name) => global::System.Globalization.CultureInfo.CurrentCulture = new(name);

		public void Dispose() => global::System.Globalization.CultureInfo.CurrentCulture = _previous;
	}

	private sealed class FakeThumbnails : ITwitchStreamThumbnails
	{
		public List<(string UserId, string? Url)> Tracked { get; } = [];

		public event EventHandler<string>? Changed
		{
			add { }
			remove { }
		}

		public MacroDeck.Ui.Model.Resources.UiResource? Find(string userId) => null;

		public void Track(string userId, string? url) => Tracked.Add((userId, url));
	}

	private sealed class FakeHistory : IVariableHistory
	{
		private readonly List<Window> _windows = [];

		public List<string> Opened { get; } = [];

		public IReadOnlyList<double> Values { get; set; } = [];

		public IVariableHistoryWindow Open(string variableName, int capacity, string? scopeRefId = null)
		{
			Opened.Add(variableName);
			var window = new Window(this);
			_windows.Add(window);

			return window;
		}

		public void Raise()
		{
			foreach (var window in _windows)
			{
				window.Raise();
			}
		}

		private sealed class Window(FakeHistory owner) : IVariableHistoryWindow
		{
			public string? ScopeRefId => null;

			public IReadOnlyList<double> Values => owner.Values;

			public event EventHandler? Changed;

			public void Raise() => Changed?.Invoke(this, EventArgs.Empty);

			public void Dispose()
			{
			}
		}
	}

	private sealed class IconIntegration : IIntegration, IIntegrationIconProvider
	{
		public string Id => TwitchStatsWidgetType.OwnerId;

		public LocalizedText Name => "Twitch";

		public string Version => "1.0.0";

		public IReadOnlyList<IActionDefinition> Actions { get; } = [];

		public bool IsInitialized => true;

		public string IconMimeType => "image/svg+xml";

		public byte[] GetIcon() => "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray();

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;
	}
}
