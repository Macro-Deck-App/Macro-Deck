using System.Text.Json;
using System.Text.RegularExpressions;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Integrations.Jellyfin;
using MacroDeckHost.Integrations.Jellyfin.Widgets;
using MacroDeckHost.Tests.UnitTests.Calendar;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using static MacroDeckHost.Tests.UnitTests.Jellyfin.JellyfinSessions;

namespace MacroDeckHost.Tests.UnitTests.Jellyfin;

[TestFixture]
internal sealed partial class JellyfinSessionsWidgetTests
{
	private static readonly byte[] _cover256 = [2, 5, 6];

	private FakeJellyfinClient _client = null!;
	private JellyfinIntegration _integration = null!;
	private UiResourceStore _store = null!;
	private JellyfinTestContext _context = null!;

	[SetUp]
	public void SetUp()
	{
		_client = new FakeJellyfinClient();
		_integration = new JellyfinIntegration(_ => _client, timings: FastTimings);
		_store = new UiResourceStore();
		var players = new MusicPlayerRegistry(new ConfigurableIntegrationRegistry([_integration]),
			Serilog.Core.Logger.None);
		var artwork = new MusicPlayerArtworkService(players, new VariantProcessor(), Serilog.Core.Logger.None);
		_context = new JellyfinTestContext
		{
			UiResources = new BuiltInUiResources(JellyfinIntegration.IntegrationId,
				_store,
				typeof(JellyfinIntegration),
				artwork)
		};
	}

	[TearDown]
	public void TearDown()
	{
		_integration.Dispose();
	}

	[Test]
	public async Task The_cover_of_what_plays_is_registered_through_the_music_player_registry_at_256_pixels()
	{
		_context.AddServer("Home", variableKey: "home");
		_client.Sessions = [Session("tv", "Living Room TV", Movie())];
		await _integration.InitializeAsync(_context);

		await using var session = (await OpenAsync())!;
		await CalendarTree.WaitForAsync(() => CoverResourceId(session) is not null,
			"the cover never reached the widget");

		Assert.Multiple(() =>
		{
			Assert.That(_store.TryGet(CoverResourceId(session)!, out var content), Is.True);
			Assert.That(content.Content.ToArray(), Is.EqualTo(_cover256));
		});
	}

	[Test]
	public async Task A_cover_no_longer_shown_is_removed_from_the_integrations_resources()
	{
		_context.AddServer("Home", variableKey: "home");
		_client.Sessions = [Session("tv", "Living Room TV", Movie())];
		await _integration.InitializeAsync(_context);
		await using var session = (await OpenAsync())!;
		await CalendarTree.WaitForAsync(() => CoverResourceId(session) is not null,
			"the cover never reached the widget");
		var cover = CoverResourceId(session)!;

		_client.Sessions = [Session("tv", "Living Room TV")];

		await CalendarTree.WaitForAsync(() => !_store.TryGet(cover, out _), "the stale cover was never removed");
	}

	[Test]
	public async Task The_widget_shows_the_integrations_own_logo()
	{
		await _integration.InitializeAsync(_context);
		var logo = await _context.UiResources.GetPluginIconAsync(JellyfinSessionsUiProvider.IconFolder,
			JellyfinSessionsUiProvider.GlyphIcon);

		await using var session = (await OpenAsync())!;

		Assert.That(Json(session), Does.Contain(logo.ResourceId));
	}

	[Test]
	public async Task A_widget_opened_before_the_integration_initialized_gets_the_logo_once_it_has()
	{
		await using var session = (await OpenAsync())!;
		var logo = await _context.UiResources.GetPluginIconAsync(JellyfinSessionsUiProvider.IconFolder,
			JellyfinSessionsUiProvider.GlyphIcon);
		Assert.That(Json(session), Does.Not.Contain(logo.ResourceId));

		await _integration.InitializeAsync(_context);

		await CalendarTree.WaitForAsync(() => Json(session).Contains(logo.ResourceId, StringComparison.Ordinal),
			"the logo never reached the widget");
	}

	[Test]
	public void Without_a_server_the_widget_asks_for_one()
	{
		Assert.That(JellyfinSessionsState.Compute([], null).Status, Is.EqualTo(JellyfinSessionsStatus.NoServer));
	}

	[Test]
	public void An_unreachable_server_is_not_shown_as_idle()
	{
		var state = JellyfinSessionsState.Compute([new JellyfinServerSummary("a", "Home", false, [])], null);

		Assert.That(state.Status, Is.EqualTo(JellyfinSessionsStatus.NotConnected));
	}

	[Test]
	public void Playing_sessions_come_before_paused_ones_and_a_server_filter_applies()
	{
		JellyfinServerSummary[] servers =
		[
			new("a",
				"Home",
				true,
				[
					Summary("1", "Sintel", JellyfinSessionPlayback.Paused),
					Summary("2", "Big Buck Bunny", JellyfinSessionPlayback.Playing)
				]),
			new("b", "Cabin", true, [Summary("3", "Elephants Dream", JellyfinSessionPlayback.Playing)])
		];

		var all = JellyfinSessionsState.Compute(servers, null);
		var home = JellyfinSessionsState.Compute(servers, "a");

		Assert.Multiple(() =>
		{
			Assert.That(all.Rows.Select(row => row.Title),
				Is.EqualTo(new[] { "Big Buck Bunny", "Elephants Dream", "Sintel" }));
			Assert.That(home.Rows.Select(row => row.Title), Is.EqualTo(new[] { "Big Buck Bunny", "Sintel" }));
			Assert.That(home.Rows[0].Detail, Is.EqualTo("alex · TV"));
		});
	}

	[Test]
	public void The_widget_view_builds_for_every_state_and_the_sample()
	{
		JellyfinSessionsState[] states =
		[
			JellyfinSessionsState.Empty,
			new(JellyfinSessionsStatus.NotConnected, []),
			new(JellyfinSessionsStatus.Ready, []),
			JellyfinSessionsSample.State,
			JellyfinSessionsSample.State with { Rows = [JellyfinSessionsSample.State.Rows[0]] },
			JellyfinSessionsSample.State with
			{
				Rows = [JellyfinSessionsSample.State.Rows[0] with { Artwork = new UiResource { ResourceId = "r1" } }],
				Glyph = new UiResource { ResourceId = "g1" }
			}
		];

		foreach (var state in states)
		{
			Assert.DoesNotThrow(() =>
				{
					using var view = new UiView(new UiSurface
							{ Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
						JellyfinSessionsView.Build(new UiState<JellyfinSessionsState>(state)));
					_ = view.Tree;
				},
				state.Status.ToString());
		}
	}

	[Test]
	public void The_widget_settings_build_with_and_without_servers()
	{
		var data = JsonDocument.Parse("""{"server":""}""").RootElement;

		Assert.DoesNotThrow(() =>
		{
			using var empty = new UiView(new UiSurface
					{ Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive },
				JellyfinSessionsUiProvider.BuildConfig(data, []));
			using var listed = new UiView(new UiSurface
					{ Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive },
				JellyfinSessionsUiProvider.BuildConfig(data, [new JellyfinServerSummary("a", "Home", true, [])]));
			_ = empty.Tree;
			_ = listed.Tree;
		});
	}

	private Task<IUiSession?> OpenAsync()
	{
		return _integration.CreateSessionAsync(new UiSessionRequest
			{
				Surface = new UiSurface
				{
					Kind = UiSurfaceKinds.Widget,
					SessionMode = UiSessionModes.Shared,
					Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
					{
						[UiWidgetSurfaceAttributes.WidgetId]
							= JsonSerializer.SerializeToElement(Guid.NewGuid().ToString()),
						[UiWidgetSurfaceAttributes.WidgetType] =
							JsonSerializer.SerializeToElement(JellyfinWidgetTypes.SessionsQualifiedId),
						[UiWidgetSurfaceAttributes.Data] = JsonSerializer.SerializeToElement(new { server = "" })
					}
				},
				UiModelVersion = 1
			},
			CancellationToken.None);
	}

	private static string Json(IUiSession session)
	{
		return JsonSerializer.Serialize(session.BuildTree().Root);
	}

	private static string? CoverResourceId(IUiSession session)
	{
		return CoverPattern().Match(Json(session)) is { Success: true } match ? match.Value : null;
	}

	[GeneratedRegex(@"app\.macro-deck\.jellyfin\.sessions-[0-9a-f]{32}-[0-9a-f]{16}")]
	private static partial Regex CoverPattern();

	private static JellyfinSessionSummary Summary(string key, string title, JellyfinSessionPlayback playback)
	{
		return new JellyfinSessionSummary(key, "alex", "TV", "Jellyfin Web", title, null, playback, 50);
	}

	private sealed class VariantProcessor : IArtworkProcessor
	{
		public Task<ProcessedArtworkResult?> Process(byte[] original, CancellationToken cancellationToken)
		{
			return Task.FromResult<ProcessedArtworkResult?>(new ProcessedArtworkResult([9, 9, 9, 9],
				new Dictionary<int, byte[]> { [128] = [1], [256] = _cover256 }));
		}
	}
}
