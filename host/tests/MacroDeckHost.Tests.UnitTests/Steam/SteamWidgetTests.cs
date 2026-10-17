using System.Net;
using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Integrations.Steam.State;
using MacroDeckHost.Tests.UnitTests.Calendar;
using MacroDeckHost.Tests.UnitTests.Delegation;
using MacroDeckHost.Integrations.Steam;
using MacroDeckHost.Integrations.Steam.Widgets;

namespace MacroDeckHost.Tests.UnitTests.Steam;

[TestFixture]
internal sealed class SteamWidgetTests
{
	private FakeSource _source = null!;
	private FakeTimeProvider _time = null!;
	private SteamArtworkCache _artwork = null!;

	[SetUp]
	public void SetUp()
	{
		_source = new FakeSource();
		_time = new FakeTimeProvider();
		_artwork = new SteamArtworkCache(new HttpClient(new NotFoundHandler()),
			new BuiltInUiResources(SteamWidgetType.OwnerId, new UiResourceStore(), typeof(SteamIntegration)),
			_time);
	}

	[TearDown]
	public void TearDown()
	{
		_artwork.Dispose();
	}

	[Test]
	public async Task Currently_playing_shows_the_game_and_how_long_it_has_been_running()
	{
		_source.Snapshot = Playing(730, "Counter-Strike 2", _time.Now.AddSeconds(-83));

		await using var session = await OpenAsync(SteamWidgetType.CurrentlyPlayingQualifiedId, new { });
		var texts = CalendarTree.Texts(session.BuildTree().Root);

		Assert.Multiple(() =>
		{
			Assert.That(texts, Does.Contain("Counter-Strike 2"));
			Assert.That(texts, Has.Some.Contains("1:23"));
		});
	}

	[Test]
	public async Task The_session_time_keeps_counting_while_the_widget_is_open()
	{
		_source.Snapshot = Playing(730, "Counter-Strike 2", _time.Now);
		await using var session = await OpenAsync(SteamWidgetType.CurrentlyPlayingQualifiedId, new { });

		_time.Advance(TimeSpan.FromSeconds(61));

		Assert.That(CalendarTree.Texts(session.BuildTree().Root), Has.Some.Contains("1:01"));
	}

	[Test]
	public async Task The_session_time_can_be_hidden()
	{
		_source.Snapshot = Playing(730, "Counter-Strike 2", _time.Now.AddMinutes(-5));

		await using var session = await OpenAsync(SteamWidgetType.CurrentlyPlayingQualifiedId,
			new { showSessionTime = false });
		var texts = CalendarTree.Texts(session.BuildTree().Root);

		Assert.Multiple(() =>
		{
			Assert.That(texts, Does.Contain("Counter-Strike 2"));
			Assert.That(texts, Has.None.Contains("5:00"));
		});
	}

	[Test]
	public async Task When_nothing_is_played_the_widget_shows_an_idle_state()
	{
		_source.Snapshot = new SteamSnapshot { SteamFound = true };

		await using var session = await OpenAsync(SteamWidgetType.CurrentlyPlayingQualifiedId, new { });

		Assert.That(CalendarTree.Texts(session.BuildTree().Root), Does.Contain("Not playing"));
	}

	[Test]
	public async Task When_the_game_ends_the_widget_goes_idle()
	{
		_source.Snapshot = Playing(730, "Counter-Strike 2", _time.Now);
		await using var session = await OpenAsync(SteamWidgetType.CurrentlyPlayingQualifiedId, new { });

		_source.Snapshot = new SteamSnapshot { SteamFound = true };
		_source.RaiseChanged();

		var texts = CalendarTree.Texts(session.BuildTree().Root);
		Assert.Multiple(() =>
		{
			Assert.That(texts, Does.Contain("Not playing"));
			Assert.That(texts, Has.None.Contains("Counter-Strike 2"));
		});
	}

	[Test]
	public async Task Without_steam_the_widget_says_so()
	{
		_source.Snapshot = SteamSnapshot.Empty;

		await using var session = await OpenAsync(SteamWidgetType.CurrentlyPlayingQualifiedId, new { });

		Assert.That(CalendarTree.Texts(session.BuildTree().Root), Does.Contain("Steam was not found on this computer"));
	}

	[Test]
	public async Task While_the_integration_is_off_the_widget_says_steam_is_not_available()
	{
		_source.IsAvailable = false;

		await using var session = await OpenAsync(SteamWidgetType.CurrentlyPlayingQualifiedId, new { });

		Assert.That(CalendarTree.Texts(session.BuildTree().Root), Does.Contain("Steam is not available"));
	}

	[Test]
	public async Task A_widget_set_to_an_account_shows_that_accounts_game_instead_of_the_one_running_here()
	{
		_source.Snapshot = new SteamSnapshot
		{
			SteamFound = true,
			CurrentGame = new SteamCurrentGame(730, "Counter-Strike 2", _time.Now),
			Accounts =
			[
				new SteamAccountSnapshot("e1", "Main", "main")
				{
					Web = SteamWebState.Connected,
					CurrentGame = new SteamCurrentGame(440, "Team Fortress 2", _time.Now.AddMinutes(-10))
				}
			]
		};

		await using var session = await OpenAsync(SteamWidgetType.CurrentlyPlayingQualifiedId, new { account = "e1" });
		var texts = CalendarTree.Texts(session.BuildTree().Root);

		Assert.Multiple(() =>
		{
			Assert.That(texts, Does.Contain("Team Fortress 2"));
			Assert.That(texts, Has.None.Contains("Counter-Strike 2"));
			Assert.That(texts, Has.Some.Contains("10:00"));
		});
	}

	[TestCase("gone", SteamWebState.Connected)]
	[TestCase("e1", SteamWebState.InvalidKey)]
	[TestCase("e1", SteamWebState.Unavailable)]
	public async Task A_widget_whose_account_is_missing_or_not_connected_says_so(string accountId, SteamWebState web)
	{
		_source.Snapshot = new SteamSnapshot
		{
			SteamFound = true,
			Accounts = [new SteamAccountSnapshot("e1", "Main", "main") { Web = web }]
		};

		await using var session
			= await OpenAsync(SteamWidgetType.CurrentlyPlayingQualifiedId, new { account = accountId });

		Assert.That(CalendarTree.Texts(session.BuildTree().Root), Does.Contain("This account is not available"));
	}

	[Test]
	public async Task The_launcher_shows_the_most_recently_played_games_first_up_to_the_configured_count()
	{
		_source.Snapshot = Library(new SteamGame(1, "Oldest", DateTimeOffset.FromUnixTimeSeconds(100)),
			new SteamGame(2, "Newest", DateTimeOffset.FromUnixTimeSeconds(300)),
			new SteamGame(3, "Middle", DateTimeOffset.FromUnixTimeSeconds(200)),
			new SteamGame(4, "Never played", null));

		await using var session = await OpenAsync(SteamWidgetType.GameLauncherQualifiedId,
			new { source = "recent", count = 2 });

		var tiles = WideTiles(session);
		Assert.That(tiles.Select(tile => tile.Id.Split('.')[^1]), Is.EqualTo(new[] { "game-2", "game-3" }));
	}

	[Test]
	public async Task The_launcher_can_show_one_chosen_game()
	{
		_source.Snapshot = Library(new SteamGame(730, "Counter-Strike 2", null),
			new SteamGame(220, "Half-Life 2", null));

		await using var session = await OpenAsync(SteamWidgetType.GameLauncherQualifiedId,
			new { source = "game", game = "220" });

		Assert.That(WideTiles(session).Select(tile => tile.Id.Split('.')[^1]), Is.EqualTo(new[] { "game-220" }));
	}

	[Test]
	public async Task A_launcher_without_a_chosen_game_asks_for_one()
	{
		_source.Snapshot = Library(new SteamGame(730, "Counter-Strike 2", null));

		await using var session = await OpenAsync(SteamWidgetType.GameLauncherQualifiedId, new { source = "game" });

		Assert.That(CalendarTree.Texts(session.BuildTree().Root), Does.Contain("No game selected"));
	}

	[Test]
	public async Task A_launcher_without_installed_games_says_so()
	{
		_source.Snapshot = new SteamSnapshot { SteamFound = true };

		await using var session = await OpenAsync(SteamWidgetType.GameLauncherQualifiedId, new { source = "recent" });

		Assert.That(CalendarTree.Texts(session.BuildTree().Root), Does.Contain("No installed games found"));
	}

	[Test]
	public async Task Tapping_a_tile_launches_that_game()
	{
		_source.Snapshot = Library(new SteamGame(730, "Counter-Strike 2", DateTimeOffset.FromUnixTimeSeconds(1)));
		await using var session = await OpenAsync(SteamWidgetType.GameLauncherQualifiedId, new { source = "recent" });

		CalendarTree.Press(session, Tiles(session).First());

		await CalendarTree.WaitForAsync(() => _source.Commands.Count == 1, "no command was sent");
		Assert.That(_source.Commands.Single(), Is.EqualTo(new SteamCommand(SteamCommandKind.LaunchGame, 730)));
	}

	[Test]
	public async Task A_failed_launch_is_shown_on_the_widget()
	{
		_source.Outcome = SteamCommandOutcome.Failed;
		_source.Snapshot = Library(new SteamGame(730, "Counter-Strike 2", DateTimeOffset.FromUnixTimeSeconds(1)));
		await using var session = await OpenAsync(SteamWidgetType.GameLauncherQualifiedId, new { source = "recent" });

		CalendarTree.Press(session, Tiles(session).First());

		await CalendarTree.WaitForAsync(()
				=> CalendarTree.Texts(session.BuildTree().Root)
					.Any(text => text.Contains("could not be opened", StringComparison.Ordinal)),
			"the failure was not shown");
	}

	[Test]
	public async Task Nothing_is_drawn_over_the_launcher_tiles()
	{
		_source.Snapshot = Library(new SteamGame(730, "Counter-Strike 2", DateTimeOffset.FromUnixTimeSeconds(1)),
			new SteamGame(440, "Team Fortress 2", DateTimeOffset.FromUnixTimeSeconds(2)));
		await using var session = await OpenAsync(SteamWidgetType.GameLauncherQualifiedId, new { source = "recent" });

		Assert.That(Covered(session), Is.Empty);
	}

	[Test]
	public async Task Nothing_covers_the_launcher_tiles_once_a_failure_is_no_longer_shown()
	{
		_source.Outcome = SteamCommandOutcome.Failed;
		_source.Snapshot = Library(new SteamGame(730, "Counter-Strike 2", DateTimeOffset.FromUnixTimeSeconds(1)));
		await using var session = await OpenAsync(SteamWidgetType.GameLauncherQualifiedId, new { source = "recent" });

		CalendarTree.Press(session, Tiles(session).First());
		await CalendarTree.WaitForAsync(()
				=> CalendarTree.Texts(session.BuildTree().Root)
					.Any(text => text.Contains("could not be opened", StringComparison.Ordinal)),
			"the failure was not shown");

		_time.Advance(SteamLauncherSession.FailureDisplay + TimeSpan.FromSeconds(1));

		await CalendarTree.WaitForAsync(() => Covered(session).Count == 0, "the failure still covers the tiles");
	}

	[Test]
	public async Task A_failed_launch_is_reported_to_the_client_that_pressed()
	{
		_source.Outcome = SteamCommandOutcome.InvalidAppId;
		_source.Snapshot = Library(new SteamGame(730, "Counter-Strike 2", DateTimeOffset.FromUnixTimeSeconds(1)));
		await using var session = await OpenAsync(SteamWidgetType.GameLauncherQualifiedId, new { source = "recent" });
		var origin = new RecordingOrigin();

		session.Dispatch(new UiEvent { NodeId = Tiles(session).First().Id, Name = UiComponentEvents.Press }, origin);

		await CalendarTree.WaitForAsync(() => origin.Failures.Count == 1, "the failure was not reported to the client");
		Assert.That(CalendarTree.Resolve(origin.Failures.Single()), Does.Contain("valid Steam app ID"));
	}

	[Test]
	public async Task A_successful_launch_reports_nothing_to_the_client()
	{
		_source.Snapshot = Library(new SteamGame(730, "Counter-Strike 2", DateTimeOffset.FromUnixTimeSeconds(1)));
		await using var session = await OpenAsync(SteamWidgetType.GameLauncherQualifiedId, new { source = "recent" });
		var origin = new RecordingOrigin();

		session.Dispatch(new UiEvent { NodeId = Tiles(session).First().Id, Name = UiComponentEvents.Press }, origin);

		await CalendarTree.WaitForAsync(() => _source.Commands.Count == 1, "no command was sent");
		Assert.That(origin.Failures, Is.Empty);
	}

	[Test]
	public async Task The_config_surface_offers_the_installed_games()
	{
		_source.Snapshot = Library(new SteamGame(730, "Counter-Strike 2", null));

		await using var session = SteamWidgetSessions.Create(new UiSessionRequest
			{
				Surface = new UiSurface
				{
					Kind = UiSurfaceKinds.Config,
					SessionMode = UiSessionModes.Exclusive,
					Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
					{
						[UiConfigSurfaceAttributes.EntryPoint] =
							JsonSerializer.SerializeToElement(UiConfigEntryPoints.WidgetConfig),
						[UiConfigSurfaceAttributes.WidgetType] =
							JsonSerializer.SerializeToElement(SteamWidgetType.GameLauncherQualifiedId)
					}
				},
				UiModelVersion = 1
			},
			_source,
			_artwork,
			null,
			_time)!;

		Assert.That(JsonSerializer.Serialize(session.BuildTree().Root), Does.Contain("Counter-Strike 2"));
	}

	[TestCase(0, "0:00")]
	[TestCase(59, "0:59")]
	[TestCase(61, "1:01")]
	[TestCase(3599, "59:59")]
	[TestCase(3600, "1:00:00")]
	[TestCase(36000 + 125, "10:02:05")]
	[TestCase(-5, "0:00")]
	public void Session_time_is_shown_as_a_clock(int seconds, string expected)
	{
		Assert.That(SteamWidgetStates.Elapsed(TimeSpan.FromSeconds(seconds)), Is.EqualTo(expected));
	}

	private static SteamSnapshot Playing(int appId, string name, DateTimeOffset since)
	{
		return new SteamSnapshot { SteamFound = true, CurrentGame = new SteamCurrentGame(appId, name, since) };
	}

	private static SteamSnapshot Library(params SteamGame[] games)
	{
		return new SteamSnapshot { SteamFound = true, Games = games };
	}

	private static List<UiNode> Tiles(IUiSession session)
	{
		return
		[
			.. CalendarTree.Flatten(session.BuildTree().Root)
				.Where(node => CalendarTree.IsPressable(node)
					&& node.Id.Split('.')[^1].StartsWith("game-", StringComparison.Ordinal))
		];
	}

	private static List<string> Covered(IUiSession session)
	{
		var covered = new List<string>();
		var tiles = Tiles(session).Select(tile => tile.Id).ToHashSet(StringComparer.Ordinal);
		Collect(session.BuildTree().Root, tiles, covered);
		return covered;
	}

	private static bool Collect(UiNode node, HashSet<string> tiles, List<string> covered)
	{
		var holdsTile = tiles.Contains(node.Id);
		for (var index = 0; index < node.Children.Count; index++)
		{
			if (!Collect(node.Children[index], tiles, covered))
			{
				continue;
			}

			holdsTile = true;
			if (node.Type == UiComponents.Layer && index < node.Children.Count - 1)
			{
				covered.Add($"{node.Id}[{index}] under {node.Children[^1].Id}");
			}
		}

		return holdsTile;
	}

	private static List<UiNode> WideTiles(IUiSession session)
	{
		return [.. Tiles(session).Where(node => node.Id.Contains(".wide", StringComparison.Ordinal))];
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

	private sealed class NotFoundHandler : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
		}
	}


	private sealed class FakeSource : ISteamSource
	{
		public event EventHandler? Changed;

		public bool IsAvailable { get; set; } = true;

		public SteamSnapshot Snapshot { get; set; } = SteamSnapshot.Empty;

		public SteamCommandOutcome Outcome { get; set; } = SteamCommandOutcome.Succeeded;

		public List<SteamCommand> Commands { get; } = [];

		public void RaiseChanged()
		{
			Changed?.Invoke(this, EventArgs.Empty);
		}

		public Task<SteamCommandOutcome> ExecuteAsync(SteamCommand command, CancellationToken cancellationToken)
		{
			Commands.Add(command);
			return Task.FromResult(Outcome);
		}
	}

	private sealed class RecordingOrigin : IUiEventOrigin
	{
		private readonly Lock _gate = new();
		private readonly List<LocalizedText> _failures = [];

		public string? ClientId => "client-1";

		public IReadOnlyList<LocalizedText> Failures
		{
			get
			{
				lock (_gate)
				{
					return [.. _failures];
				}
			}
		}

		public Task<bool> ReportFailureAsync(LocalizedText message, CancellationToken cancellationToken = default)
		{
			lock (_gate)
			{
				_failures.Add(message);
			}

			return Task.FromResult(true);
		}

		public Task<bool> ShowModalAsync(ModalDefinition modal, CancellationToken cancellationToken = default)
		{
			return Task.FromResult(false);
		}

		public Task<ModalResult<T>> ShowModalAsync<T>(
			ModalDefinition modal,
			CancellationToken cancellationToken = default)
		{
			return Task.FromResult(ModalResult.FromCancellation<T>());
		}
	}
}
