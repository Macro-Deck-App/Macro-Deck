using MacroDeck.Sdk;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Widgets;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Infrastructure.SystemIntegrations;
using MacroDeckHost.Integrations.Steam;
using MacroDeckHost.Integrations.Steam.Native;
using MacroDeckHost.Integrations.Steam.Protocol;
using MacroDeckHost.Integrations.Steam.State;
using MacroDeckHost.Integrations.Steam.Variables;
using MacroDeckHost.Integrations.Steam.Widgets;
using MacroDeckHost.Tests.UnitTests.Calendar;
using MacroDeckHost.Tests.UnitTests.Delegation;
using MacroDeckHost.Tests.UnitTests.Integrations;
using MacroDeckHost.Tests.UnitTests.System;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Steam;

[TestFixture]
internal sealed class SteamIntegrationTests
{
	private SteamFolders _folders = null!;
	private FakeSteamPlatform _platform = null!;
	private FakeApplicationService _applications = null!;
	private List<string> _clientKeys = null!;

	[SetUp]
	public void SetUp()
	{
		_folders = new SteamFolders();
		var steam = _folders.Make("Steam");
		SteamFolders.WriteManifest(steam, 730, "Counter-Strike 2");
		_platform = new FakeSteamPlatform
			{ Os = SteamOs.Windows, HomeDirectory = _folders.Root, RegistrySteamPath = steam };
		_applications = new FakeApplicationService();
		_clientKeys = [];
	}

	[TearDown]
	public void TearDown()
	{
		_folders.Dispose();
	}

	[Test]
	public void It_is_shipped_with_a_stable_id_and_is_found_by_discovery()
	{
		var ids = BuiltInIntegrations.Discover(Log.Logger).Select(integration => integration.Id);

		Assert.That(ids, Does.Contain("app.macro-deck.steam"));
	}

	[Test]
	public async Task It_starts_disabled_because_it_cannot_work_unless_steam_is_installed()
	{
		var registry = new IntegrationRegistry(
			new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
			new IntegrationLifecycleTests.FakeIntegrationStateStore(),
			Log.Logger);

		await registry.RegisterAsync(Integration());

		Assert.That(registry.IsEnabled(SteamIntegration.IntegrationId), Is.False);
	}

	[Test]
	public void Setup_is_optional_and_several_accounts_are_allowed()
	{
		var integration = Integration();

		Assert.Multiple(() =>
		{
			Assert.That(integration.RequiresConfiguration, Is.False);
			Assert.That(integration.AllowsMultipleConfigurations, Is.True);
		});
	}

	[Test]
	public async Task Without_any_configuration_the_installed_games_work_and_no_web_client_is_created()
	{
		var integration = Integration();

		await integration.InitializeAsync(new FakeContext());

		Assert.Multiple(() =>
		{
			Assert.That(integration.IsAvailable, Is.True);
			Assert.That(integration.Snapshot.SteamFound, Is.True);
			Assert.That(integration.Snapshot.Games, Has.Count.EqualTo(1));
			Assert.That(integration.Snapshot.Accounts, Is.Empty);
			Assert.That(integration.Variables,
				Is.Empty,
				"variables exist per configuration, so there are none without one");
			Assert.That(_clientKeys, Is.Empty);
		});

		await integration.ShutdownAsync();
	}

	[Test]
	public async Task A_configured_profile_creates_a_web_client_with_the_stored_key_and_extra_folders_are_used()
	{
		var extra = _folders.Make("Extra");
		SteamFolders.WriteManifest(extra, 220, "Half-Life 2");
		var context = new FakeContext();
		context.Add(new Dictionary<string, string?>
			{
				["steamId"] = "76561198000000001",
				["libraryFolders"] = extra
			},
			"KEY123");
		var integration = Integration();

		await integration.InitializeAsync(context);

		Assert.Multiple(() =>
		{
			Assert.That(_clientKeys, Is.EqualTo(new[] { "KEY123" }));
			Assert.That(integration.Snapshot.Games.Select(game => game.AppId), Is.EquivalentTo(new[] { 730, 220 }));
		});

		await integration.ShutdownAsync();
	}

	[Test]
	public async Task Several_accounts_get_their_own_variables_named_after_their_stored_keys()
	{
		var context = new FakeContext();
		context.Add(new Dictionary<string, string?> { ["steamId"] = "76561198000000001", ["variableKey"] = "main" },
			"KEY-A",
			"Main");
		context.Add(new Dictionary<string, string?> { ["steamId"] = "76561198000000002", ["variableKey"] = "alt" },
			"KEY-B",
			"Alt");
		var integration = Integration();

		await integration.InitializeAsync(context);

		var names = integration.Variables.Select(definition => definition.Name).ToList();
		Assert.Multiple(() =>
		{
			Assert.That(_clientKeys, Is.EquivalentTo(new[] { "KEY-A", "KEY-B" }));
			Assert.That(integration.Snapshot.Accounts.Select(account => account.Title),
				Is.EqualTo(new[] { "Main", "Alt" }));
			Assert.That(names, Does.Contain("steam_main_is_playing"));
			Assert.That(names, Does.Not.Contain("steam_is_playing"), "there is no variable without an account name");
			Assert.That(names, Does.Contain("steam_alt_current_game_name"));
			Assert.That(names, Does.Contain("steam_alt_total_playtime_hours"));
			Assert.That(names, Has.Count.EqualTo(2 * 10));
		});

		await integration.ShutdownAsync();
	}

	[Test]
	public async Task Two_accounts_whose_names_slug_alike_still_get_distinct_variable_keys()
	{
		var context = new FakeContext();
		context.Add(new Dictionary<string, string?> { ["steamId"] = "76561198000000001" }, "A", "Main");
		context.Add(new Dictionary<string, string?> { ["steamId"] = "76561198000000002" }, "B", "main");
		var integration = Integration();

		await integration.InitializeAsync(context);

		Assert.That(new[] { context.Stored(0, "variableKey"), context.Stored(1, "variableKey") },
			Is.EqualTo(new[] { "main", "main_2" }),
			"the resolved keys are stored so they survive deleting a sibling");

		var prefixes = integration.Variables.Select(definition => definition.Name!)
			.Where(name => name.EndsWith("_friends_online", StringComparison.Ordinal))
			.ToList();
		Assert.Multiple(() =>
		{
			Assert.That(prefixes,
				Is.EquivalentTo(new[] { "steam_main_friends_online", "steam_main_2_friends_online" }));
		});

		await integration.ShutdownAsync();
	}

	[Test]
	public async Task A_rejected_key_and_a_private_friends_list_are_reported_as_issues()
	{
		var context = new FakeContext();
		context.Add(new Dictionary<string, string?> { ["steamId"] = "76561198000000001" }, "A", "Main");
		var integration = Integration();
		await integration.InitializeAsync(context);

		await CalendarTree.WaitForAsync(() => integration.Snapshot.Accounts is [{ Friends: SteamFriendsState.Private }],
			"the friends state never arrived");
		var issues = await integration.GetIssuesAsync();

		Assert.That(issues.Select(issue => issue.Id.Split(':')[0]), Is.EqualTo(new[] { "friends-private" }));

		await integration.ShutdownAsync();
	}

	[Test]
	public async Task A_configuration_without_a_key_is_a_local_account_with_all_variables_and_no_web_client()
	{
		var context = new FakeContext();
		context.Add(new Dictionary<string, string?> { ["steamId"] = "76561198000000001", ["variableKey"] = "pc" },
			null,
			"My PC");
		var integration = Integration();

		await integration.InitializeAsync(context);

		Assert.Multiple(() =>
		{
			Assert.That(_clientKeys, Is.Empty);
			Assert.That(integration.Snapshot.Accounts.Single().HasWebApi, Is.False);
			Assert.That(integration.Variables.Select(definition => definition.Name),
				Is.EqualTo(SteamVariables.Names.Select(name => "steam_pc_" + name)));
		});

		await integration.ShutdownAsync();
	}

	[Test]
	public async Task Shutdown_makes_it_unavailable_clears_the_snapshot_and_tells_listeners()
	{
		var integration = Integration();
		await integration.InitializeAsync(new FakeContext());
		var changes = 0;
		integration.Changed += (_, _) => changes++;

		await integration.ShutdownAsync();

		Assert.Multiple(() =>
		{
			Assert.That(integration.IsAvailable, Is.False);
			Assert.That(integration.Snapshot, Is.EqualTo(SteamSnapshot.Empty));
			Assert.That(changes, Is.GreaterThan(0));
		});
	}

	[Test]
	public async Task It_can_be_restarted_after_a_shutdown()
	{
		var integration = Integration();
		var context = new FakeContext();

		await integration.InitializeAsync(context);
		await integration.ShutdownAsync();
		await integration.InitializeAsync(context);

		Assert.Multiple(() =>
		{
			Assert.That(integration.IsAvailable, Is.True);
			Assert.That(integration.Snapshot.Games, Has.Count.EqualTo(1));
		});

		await integration.ShutdownAsync();
	}

	[Test]
	public async Task Commands_open_steam_uris_through_the_application_service()
	{
		var integration = Integration();
		await integration.InitializeAsync(new FakeContext());

		var outcome = await integration.ExecuteAsync(new SteamCommand(SteamCommandKind.LaunchGame, 730),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(outcome, Is.EqualTo(SteamCommandOutcome.Succeeded));
			Assert.That(_applications.OpenedUris, Is.EqualTo(new[] { "steam://rungameid/730" }));
		});

		await integration.ShutdownAsync();
	}

	[Test]
	public async Task A_running_game_is_visible_through_the_variables()
	{
		_platform.RegistryRunningAppId = 730;
		var integration = Integration();

		var context = new FakeContext();
		context.Add(new Dictionary<string, string?> { ["variableKey"] = "pc" }, null, "My PC");

		await integration.InitializeAsync(context);

		var name = await integration.ReadAsync("steam-pc-current-game-name");
		var playing = await integration.ReadAsync("steam-pc-is-playing");
		Assert.Multiple(() =>
		{
			Assert.That(name.Value, Is.EqualTo("Counter-Strike 2"));
			Assert.That(playing.Value, Is.EqualTo(true));
		});

		await integration.ShutdownAsync();
	}

	[Test]
	public async Task Closing_a_game_asks_the_game_running_here_to_close_from_its_install_folder()
	{
		var steam = Path.Combine(_folders.Root, "Steam");
		SteamFolders.WriteManifest(steam, 730, "Counter-Strike 2", installDir: "Counter-Strike Global Offensive");
		_platform.RegistryRunningAppId = 730;
		_platform.GameProcesses.Add(new SteamGameProcess(42, DateTime.UnixEpoch));
		var integration = Integration();
		await integration.InitializeAsync(new FakeContext());

		var outcome = await integration.ExecuteAsync(new SteamCommand(SteamCommandKind.CloseGame),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(outcome, Is.EqualTo(SteamCommandOutcome.Succeeded));
			Assert.That(_platform.ProcessQueries.Single(),
				Is.EqualTo(
					(730, (string?)Path.Combine(steam, "steamapps", "common", "Counter-Strike Global Offensive"))));
			Assert.That(_platform.CloseRequests, Is.EqualTo(new[] { 42 }));
			Assert.That(_applications.OpenedUris, Is.Empty, "closing goes to the game, not through a steam:// link");
		});

		await integration.ShutdownAsync();
	}

	[Test]
	public async Task Reconfiguring_keeps_the_artwork_cache_that_open_widgets_already_hold()
	{
		var integration = Integration();
		await integration.InitializeAsync(new FakeContext());
		var before = integration.Artwork;

		await integration.ShutdownAsync();
		await integration.InitializeAsync(new FakeContext());

		Assert.Multiple(() =>
		{
			Assert.That(before, Is.Not.Null);
			Assert.That(integration.Artwork, Is.SameAs(before));
		});

		await integration.ShutdownAsync();
	}

	[Test]
	public async Task The_integration_serves_its_own_widget_ui_and_follows_its_own_state()
	{
		_platform.RegistryRunningAppId = 730;
		var integration = Integration();
		await integration.InitializeAsync(new FakeContext());

		await using var session = (await integration.CreateSessionAsync(new UiSessionRequest
			{
				Surface = new UiSurface
				{
					Kind = UiSurfaceKinds.Widget,
					SessionMode = UiSessionModes.Shared,
					Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
					{
						[UiWidgetSurfaceAttributes.WidgetType] =
							JsonSerializer.SerializeToElement(SteamWidgetType.CurrentlyPlayingQualifiedId),
						[UiWidgetSurfaceAttributes.Data] = JsonSerializer.SerializeToElement(new { })
					}
				},
				UiModelVersion = 1
			},
			CancellationToken.None))!;

		Assert.That(CalendarTree.Texts(session.BuildTree().Root), Does.Contain("Counter-Strike 2"));

		await integration.ShutdownAsync();
		Assert.That(CalendarTree.Texts(session.BuildTree().Root), Does.Contain("Steam is not available"));
	}

	[Test]
	public async Task The_widget_shows_the_steam_logo_the_integration_ships()
	{
		var integration = Integration();
		var context = new FakeContext();
		await integration.InitializeAsync(context);

		await using var session = (await integration.CreateSessionAsync(new UiSessionRequest
			{
				Surface = new UiSurface
				{
					Kind = UiSurfaceKinds.Widget,
					SessionMode = UiSessionModes.Shared,
					Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
					{
						[UiWidgetSurfaceAttributes.WidgetType] =
							JsonSerializer.SerializeToElement(SteamWidgetType.CurrentlyPlayingQualifiedId),
						[UiWidgetSurfaceAttributes.Data] = JsonSerializer.SerializeToElement(new { })
					}
				},
				UiModelVersion = 1
			},
			CancellationToken.None))!;
		var logo = await context.UiResources.GetPluginIconAsync("widget", "logo");

		Assert.Multiple(() =>
		{
			Assert.That(logo.MediaType, Is.EqualTo("image/svg+xml"));
			Assert.That(JsonSerializer.Serialize(session.BuildTree().Root), Does.Contain(logo.ResourceId));
			Assert.That(integration.GetIcon(), Is.Not.Empty);
		});

		await integration.ShutdownAsync();
	}

	[Test]
	public void The_widget_types_have_stable_ids_and_a_data_schema()
	{
		var types = Integration().GetWidgetTypes();

		Assert.Multiple(() =>
		{
			Assert.That(types.Select(type => type.Id),
				Is.EqualTo(new[]
					{ "steam-currently-playing", "steam-game-launcher", "steam-friends", "steam-library" }));
			Assert.That(types.All(type => type.DataSchema is not null && type.HasConfiguration), Is.True);
		});
	}

	private SteamIntegration Integration()
	{
		return new SteamIntegration(_platform,
			key =>
			{
				_clientKeys.Add(key);
				return new UnusedClient();
			},
			_applications,
			new FakeTimeProvider());
	}

	private sealed class UnusedClient : ISteamWebApiClient
	{
		public Task<SteamPlayerSummary?> GetPlayerSummaryAsync(string steamId, CancellationToken cancellationToken)
		{
			return Task.FromResult<SteamPlayerSummary?>(null);
		}

		public Task<IReadOnlyList<SteamPlayerSummary>> GetPlayerSummariesAsync(
			IReadOnlyList<string> steamIds,
			CancellationToken cancellationToken)
		{
			return Task.FromResult<IReadOnlyList<SteamPlayerSummary>>([]);
		}

		public Task<IReadOnlyList<string>?> GetFriendIdsAsync(string steamId, CancellationToken cancellationToken)
		{
			return Task.FromResult<IReadOnlyList<string>?>(null);
		}

		public Task<string?> ResolveVanityUrlAsync(string vanityName, CancellationToken cancellationToken)
		{
			return Task.FromResult<string?>(null);
		}

		public Task<IReadOnlyList<SteamOwnedGame>?> GetOwnedGamesAsync(
			string steamId,
			CancellationToken cancellationToken)
		{
			return Task.FromResult<IReadOnlyList<SteamOwnedGame>?>([]);
		}
	}

	private sealed class FakeContext : IIntegrationContext, IIntegrationConfig
	{
		private readonly List<ConfigEntrySnapshot> _entries = [];
		private readonly Dictionary<(Guid, string), string?> _strings = [];
		private readonly Dictionary<(Guid, string), string> _secrets = [];

		public IIntegrationConfig Config => this;
		public IVariableApi Variables => throw new NotSupportedException();
		public IUserVariableApi UserVariables => throw new NotSupportedException();
		public IDeckNavigator Deck => throw new NotSupportedException();
		public IScriptApi Scripts => throw new NotSupportedException();
		public IWidgetApi Widgets => throw new NotSupportedException();
		public RecordingEventPublisher Published { get; } = new();
		public IEventPublisher Events => Published;
		public IUserNotifier Notifications => throw new NotSupportedException();

		public IUiResourceRegistry UiResources { get; } =
			new BuiltInUiResources(SteamWidgetType.OwnerId, new UiResourceStore(), typeof(SteamIntegration));

		public void Add(IReadOnlyDictionary<string, string?> values, string? secret, string title = "Steam")
		{
			var id = Guid.NewGuid();
			_entries.Add(new ConfigEntrySnapshot(id, title));
			foreach (var (key, value) in values)
			{
				_strings[(id, key)] = value;
			}

			if (secret is not null)
			{
				_secrets[(id, "apiKey")] = secret;
			}
		}

		public Task<IReadOnlyList<ConfigEntrySnapshot>> GetEntriesAsync(CancellationToken cancellationToken = default)
		{
			return Task.FromResult<IReadOnlyList<ConfigEntrySnapshot>>([.. _entries]);
		}

		public Task<string?> GetStringAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
		{
			return Task.FromResult(_strings.GetValueOrDefault((entryId, key)));
		}

		public Task<string?> GetSecretAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
		{
			return Task.FromResult<string?>(_secrets.GetValueOrDefault((entryId, key)));
		}

		public Task SetStringAsync(
			Guid entryId,
			string key,
			string? value,
			CancellationToken cancellationToken = default)
		{
			_strings[(entryId, key)] = value;
			return Task.CompletedTask;
		}

		public string? Stored(int index, string key)
		{
			return _strings.GetValueOrDefault((_entries[index].Id, key));
		}

		public Task SetSecretAsync(
			Guid entryId,
			string key,
			string value,
			CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}
	}
}
