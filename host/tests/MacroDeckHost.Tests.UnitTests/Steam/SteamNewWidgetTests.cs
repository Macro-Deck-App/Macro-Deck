using System.Globalization;
using System.Net;
using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Integrations.Steam.State;
using MacroDeckHost.Integrations.Steam.Widgets;
using MacroDeckHost.Tests.UnitTests.Calendar;
using MacroDeckHost.Tests.UnitTests.Delegation;

namespace MacroDeckHost.Tests.UnitTests.Steam;

[TestFixture]
internal sealed class SteamNewWidgetTests
{
	private Source _source = null!;
	private FakeTimeProvider _time = null!;
	private SteamArtworkCache _artwork = null!;

	[SetUp]
	public void SetUp()
	{
		_source = new Source();
		_time = new FakeTimeProvider();
		_artwork = new SteamArtworkCache(new HttpClient(new NotFoundHandler()),
			new BuiltInUiResources(SteamWidgetType.OwnerId, new UiResourceStore()),
			_time);
	}

	[TearDown]
	public void TearDown()
	{
		_artwork.Dispose();
	}

	[Test]
	public async Task The_friends_widget_counts_who_is_online_and_playing_and_lists_them()
	{
		_source.Snapshot = WithAccount(Connected("e1") with
		{
			Friends = SteamFriendsState.Available,
			FriendsOnline = 2,
			FriendsInGame = 1,
			FriendList =
			[
				new SteamFriend("1", "Bob", SteamPersona.Online, 730, "Counter-Strike 2", null),
				new SteamFriend("2", "Amy", SteamPersona.Away, null, null, null),
				new SteamFriend("3", "Zed", SteamPersona.Offline, null, null, null)
			]
		});

		await using var session = await OpenAsync(SteamWidgetType.FriendsQualifiedId, new { });
		var texts = CalendarTree.Texts(session.BuildTree().Root);

		Assert.Multiple(() =>
		{
			Assert.That(texts, Does.Contain("2"));
			Assert.That(texts, Does.Contain("1 playing"));
			Assert.That(texts, Does.Contain("Bob"));
			Assert.That(texts, Does.Contain("Counter-Strike 2"));
			Assert.That(texts, Does.Contain("Away"));
			Assert.That(texts, Does.Not.Contain("Zed"), "offline friends are not listed");
		});
	}

	[Test]
	public async Task The_friends_widget_asks_for_a_key_when_no_account_has_one()
	{
		_source.Snapshot = WithAccount(new SteamAccountSnapshot("e1", "Main", "main"));

		await using var session = await OpenAsync(SteamWidgetType.FriendsQualifiedId, new { });

		Assert.That(CalendarTree.Texts(session.BuildTree().Root),
			Does.Contain("Add a Steam account with a Web API key to see your friends."));
	}

	[Test]
	public async Task The_friends_widget_says_when_the_friends_list_is_private()
	{
		_source.Snapshot = WithAccount(Connected("e1") with { Friends = SteamFriendsState.Private });

		await using var session = await OpenAsync(SteamWidgetType.FriendsQualifiedId, new { account = "e1" });

		Assert.That(CalendarTree.Texts(session.BuildTree().Root),
			Does.Contain("The friends list of this account is private."));
	}

	[Test]
	public async Task The_library_widget_shows_playtime_and_the_games_played_last()
	{
		_source.Snapshot = WithAccount(Connected("e1") with
		{
			Owned = SteamOwnedState.Available,
			OwnedGames =
			[
				new SteamOwnedGame(730, "Counter-Strike 2", 6000, 90, DateTimeOffset.FromUnixTimeSeconds(200)),
				new SteamOwnedGame(220, "Half-Life 2", 630, 0, DateTimeOffset.FromUnixTimeSeconds(100)),
				new SteamOwnedGame(440, "Never played", 0, 0, null)
			]
		});

		await using var session = await OpenAsync(SteamWidgetType.LibraryQualifiedId, new { });
		var texts = CalendarTree.Texts(session.BuildTree().Root);

		Assert.Multiple(() =>
		{
			Assert.That(texts, Does.Contain("111"), "a large total is rounded to whole hours");
			Assert.That(texts, Does.Contain("3 games"));
			Assert.That(texts, Does.Contain("Counter-Strike 2"));
			Assert.That(texts, Does.Contain("1.5 h in the last 2 weeks"));
			Assert.That(texts, Does.Contain("10.5 h in total"));
			Assert.That(texts, Does.Not.Contain("Never played"));
		});
	}

	[Test]
	public async Task Without_a_key_the_library_widget_shows_the_games_installed_here()
	{
		_source.Snapshot = new SteamSnapshot
		{
			SteamFound = true,
			Games =
			[
				new SteamGame(730, "Counter-Strike 2", DateTimeOffset.FromUnixTimeSeconds(200)),
				new SteamGame(220, "Half-Life 2", null)
			]
		};

		await using var session = await OpenAsync(SteamWidgetType.LibraryQualifiedId, new { });
		var texts = CalendarTree.Texts(session.BuildTree().Root);

		Assert.Multiple(() =>
		{
			Assert.That(texts, Does.Contain("2"));
			Assert.That(texts, Does.Contain("Installed"));
			Assert.That(texts, Does.Contain("Counter-Strike 2"));
		});
	}

	[Test]
	public async Task The_library_widget_says_when_game_details_are_private()
	{
		_source.Snapshot = WithAccount(Connected("e1") with { Owned = SteamOwnedState.Private });

		await using var session = await OpenAsync(SteamWidgetType.LibraryQualifiedId, new { });

		Assert.That(CalendarTree.Texts(session.BuildTree().Root),
			Does.Contain("The game details of this account are private."));
	}

	[Test]
	public async Task The_launcher_shows_the_favorites_in_the_chosen_order()
	{
		_source.Snapshot = new SteamSnapshot
		{
			SteamFound = true,
			Games = [new SteamGame(730, "Counter-Strike 2", null), new SteamGame(220, "Half-Life 2", null)]
		};

		await using var session = await OpenAsync(SteamWidgetType.GameLauncherQualifiedId,
			new { source = "favorites", favorites = new[] { "220", "not-a-game", "730", "220" } });

		Assert.That(Tiles(session, ".wide"), Is.EqualTo(new[] { "game-220", "game-730" }));
	}

	[Test]
	public async Task The_launcher_shows_at_most_six_favorites()
	{
		_source.Snapshot = new SteamSnapshot { SteamFound = true };

		await using var session = await OpenAsync(SteamWidgetType.GameLauncherQualifiedId,
			new
			{
				source = "favorites",
				favorites = Enumerable.Range(1, 9).Select(id => id.ToString(CultureInfo.InvariantCulture)).ToArray()
			});

		Assert.That(Tiles(session, ".grid"), Has.Count.EqualTo(6));
	}

	[Test]
	public async Task A_launcher_without_favorites_asks_for_them()
	{
		_source.Snapshot = new SteamSnapshot { SteamFound = true };

		await using var session
			= await OpenAsync(SteamWidgetType.GameLauncherQualifiedId, new { source = "favorites" });

		Assert.That(CalendarTree.Texts(session.BuildTree().Root),
			Does.Contain("Choose your favorite games in the widget settings."));
	}

	[Test]
	public void An_unknown_launcher_source_still_shows_recent_games()
	{
		var options = SteamWidgetSettings.ParseLauncher(JsonSerializer.SerializeToElement(new { source = "bogus" }));

		Assert.That((options.Source, options.Count),
			Is.EqualTo((SteamWidgetType.RecentSource, SteamWidgetType.DefaultCount)));
	}

	[Test]
	public async Task The_launcher_settings_offer_a_favorites_source_with_a_choice_of_games()
	{
		_source.Snapshot = new SteamSnapshot
			{ SteamFound = true, Games = [new SteamGame(730, "Counter-Strike 2", null)] };

		await using var session = OpenConfig(SteamWidgetType.GameLauncherQualifiedId);
		var tree = JsonSerializer.Serialize(session.BuildTree().Root);

		Assert.Multiple(() =>
		{
			Assert.That(tree, Does.Contain("Widgets.Steam.Config.SourceFavorites"), "the favorites source");
			Assert.That(tree, Does.Contain("Widgets.Steam.Config.FavoritesDescription"), "the favorite games picker");
		});
	}

	[TestCase(SteamWidgetType.FriendsQualifiedId)]
	[TestCase(SteamWidgetType.LibraryQualifiedId)]
	public async Task The_new_widgets_offer_the_accounts_with_a_key(string widgetType)
	{
		_source.Snapshot = WithAccount(Connected("e1"), new SteamAccountSnapshot("e2", "Local only", "local"));

		await using var session = OpenConfig(widgetType);
		var tree = JsonSerializer.Serialize(session.BuildTree().Root);

		Assert.Multiple(() =>
		{
			Assert.That(tree, Does.Contain("Main"));
			Assert.That(tree, Does.Not.Contain("Local only"));
		});
	}

	[TestCase(null, true)]
	[TestCase("#336699", false)]
	[TestCase("transparent", false)]
	public async Task The_default_face_is_drawn_only_without_a_background_colour(string? background, bool face)
	{
		_source.Snapshot = WithAccount(Connected("e1") with { Friends = SteamFriendsState.Available });

		await using var session
			= await OpenAsync(SteamWidgetType.FriendsQualifiedId, new { backgroundColor = background });

		Assert.That(JsonSerializer.Serialize(session.BuildTree().Root).Contains("linear", StringComparison.Ordinal),
			Is.EqualTo(face));
	}

	private static List<string> Tiles(IUiSession session, string layout)
	{
		return
		[
			.. CalendarTree.Flatten(session.BuildTree().Root)
				.Where(node => CalendarTree.IsPressable(node)
					&& node.Id.Contains(layout, StringComparison.Ordinal)
					&& node.Id.Split('.')[^1].StartsWith("game-", StringComparison.Ordinal))
				.Select(node => node.Id.Split('.')[^1])
		];
	}

	private static SteamAccountSnapshot Connected(string entryId)
	{
		return new SteamAccountSnapshot(entryId, "Main", "main") { Web = SteamWebState.Connected };
	}

	private static SteamSnapshot WithAccount(params SteamAccountSnapshot[] accounts)
	{
		return new SteamSnapshot { SteamFound = true, Accounts = accounts };
	}

	private Task<IUiSession> OpenAsync(string widgetType, object data)
	{
		var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
		{
			[UiWidgetSurfaceAttributes.WidgetType] = JsonSerializer.SerializeToElement(widgetType),
			[UiWidgetSurfaceAttributes.Data] = JsonSerializer.SerializeToElement(data)
		};

		return Task.FromResult(SteamWidgetSessions.Create(new UiSessionRequest
			{
				Surface = new UiSurface
					{ Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared, Attributes = attributes },
				UiModelVersion = 1
			},
			_source,
			_artwork,
			null,
			_time)!);
	}

	private IUiSession OpenConfig(string widgetType)
	{
		return SteamWidgetSessions.Create(new UiSessionRequest
			{
				Surface = new UiSurface
				{
					Kind = UiSurfaceKinds.Config,
					SessionMode = UiSessionModes.Exclusive,
					Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
					{
						[UiConfigSurfaceAttributes.EntryPoint] =
							JsonSerializer.SerializeToElement(UiConfigEntryPoints.WidgetConfig),
						[UiConfigSurfaceAttributes.WidgetType] = JsonSerializer.SerializeToElement(widgetType)
					}
				},
				UiModelVersion = 1
			},
			_source,
			_artwork,
			null,
			_time)!;
	}

	private sealed class NotFoundHandler : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
		}
	}

	private sealed class Source : ISteamSource
	{
		public event EventHandler? Changed
		{
			add { }
			remove { }
		}

		public bool IsAvailable => true;

		public SteamSnapshot Snapshot { get; set; } = SteamSnapshot.Empty;

		public Task<SteamCommandOutcome> ExecuteAsync(SteamCommand command, CancellationToken cancellationToken)
		{
			return Task.FromResult(SteamCommandOutcome.Succeeded);
		}
	}
}
