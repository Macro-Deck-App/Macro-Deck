using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using MacroDeck.Ui.Model.Resources;
using MacroDeckHost.Integrations.Steam.Configuration;
using MacroDeckHost.Integrations.Steam.Events;
using MacroDeckHost.Integrations.Steam.Native;
using MacroDeckHost.Integrations.Steam.Protocol;
using MacroDeckHost.Integrations.Steam.State;
using MacroDeckHost.Integrations.Steam.Variables;
using MacroDeckHost.Integrations.Steam.Widgets;
using MacroDeckHost.Integrations.Steam.Actions;
using MacroDeckHost.Platform.Applications;
using Serilog;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.Steam;
using WidgetStrings = MacroDeckHost.Localization.AppStrings.Widgets.Steam;

namespace MacroDeckHost.Integrations.Steam;

[MacroDeckIntegration(EnabledByDefault = false)]
public sealed partial class SteamIntegration
	: IIntegration, IVariableProvider, IConfigFlowProvider, IIntegrationIconProvider, IWidgetTypeProvider,
		IIntegrationIssueProvider, IUiProvider, ISteamSource, IEventProvider, IDynamicEventOptionsProvider, IDisposable
{
	public const string IntegrationId = SteamWidgetType.OwnerId;

	private static readonly TimeSpan _localInterval = TimeSpan.FromSeconds(3);
	private static readonly TimeSpan _undetectedInterval = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan _selfInterval = TimeSpan.FromSeconds(30);
	private static readonly TimeSpan _friendsInterval = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan _ownedGamesInterval = TimeSpan.FromMinutes(1);

	private static readonly ILogger _logger = IntegrationLog.For<SteamIntegration>(IntegrationId);

	private static readonly byte[] _icon = LoadIcon();

	private static readonly string[] _nullableString = ["string", "null"];

	private readonly ISteamPlatform _platform;
	private readonly Func<string, ISteamWebApiClient> _clientFactory;
	private readonly SteamLauncher _launcher;
	private readonly SteamGameCloser _closer;
	private readonly TimeProvider _time;

	private SteamSnapshot _published = SteamSnapshot.Empty;
	private SteamTracker? _tracker;
	private SteamArtworkCache? _artwork;
	private UiResource? _logo;
	private List<SteamAccountConfig> _accounts = [];
	private CancellationTokenSource? _cts;
	private Task[] _loops = [];

	public SteamIntegration()
		: this(new SystemSteamPlatform(),
			key => new SteamWebApiClient(key),
			ApplicationServiceFactory.Create(IntegrationLog.For(IntegrationId)),
			TimeProvider.System)
	{
	}

	internal SteamIntegration(
		ISteamPlatform platform,
		Func<string, ISteamWebApiClient> clientFactory,
		IApplicationService applications,
		TimeProvider time)
	{
		_platform = platform;
		_clientFactory = clientFactory;
		_launcher = new SteamLauncher(applications);
		_closer = new SteamGameCloser(platform, time);
		_time = time;
		Actions = SteamActions.Create(this);
	}

	public string Id => IntegrationId;

	public LocalizedText Name => "Steam";

	public string Version => "1.0.0";

	public bool IsInitialized { get; private set; }

	public bool IsAvailable => IsInitialized;

	public event EventHandler? Changed;

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public IReadOnlyList<EventDefinition> EventDefinitions => SteamEventDefinitions.All;

	public string IconMimeType => "image/svg+xml";

	public bool AllowsMultipleConfigurations => true;

	public bool RequiresConfiguration => false;

	public bool VariablesDependOnConfiguration => true;

	public IReadOnlyList<VariableDefinition> Variables
		=>
		[
			.. _accounts.SelectMany(account => SteamVariables.Declare(account.VariableKey,
				new VariableConfiguration(account.EntryId, account.Title)))
		];

	public IReadOnlyList<VariableDefinition> DeclaredVariables
		=> _accounts.Count > 0
			? Variables
			: SteamVariables.Declare(VariableNameTemplate.Placeholder("account"), null);

	public SteamSnapshot Snapshot => _tracker?.Snapshot ?? SteamSnapshot.Empty;

	internal SteamArtworkCache? Artwork => _artwork;

	public byte[] GetIcon()
	{
		return _icon;
	}

	public IConfigFlow CreateConfigFlow()
	{
		return new SteamConfigFlow(_clientFactory);
	}

	public async Task InitializeAsync(IIntegrationContext context)
	{
		IntegrationHttp.Use(context.Http);
		await StopAsync();

		var entries = await context.Config.GetEntriesAsync();
		var accounts = new List<SteamAccountConfig>();
		var folders = new List<string>();
		var usedKeys = new HashSet<string>(StringComparer.Ordinal);

		foreach (var entry in entries)
		{
			folders.AddRange(SteamConfigFlow.ParseFolders(
				await context.Config.GetStringAsync(entry.Id, SteamConfigKeys.LibraryFolders)));

			var steamId = await context.Config.GetStringAsync(entry.Id, SteamConfigKeys.SteamId);
			var apiKey = await context.Config.GetSecretAsync(entry.Id, SteamConfigKeys.ApiKey);
			var hasWebApi = !string.IsNullOrWhiteSpace(steamId) && !string.IsNullOrWhiteSpace(apiKey);

			var storedKey = await context.Config.GetStringAsync(entry.Id, SteamConfigKeys.VariableKey);
			var key = storedKey is { Length: > 0 } && usedKeys.Add(storedKey)
				? storedKey
				: SteamVariables.UniqueKey(entry.Title, usedKeys);

			if (!string.Equals(key, storedKey, StringComparison.Ordinal))
			{
				await context.Config.SetStringAsync(entry.Id, SteamConfigKeys.VariableKey, key);
			}

			accounts.Add(hasWebApi
				? new SteamAccountConfig(entry.Id.ToString("D"),
					entry.Title,
					key,
					steamId!.Trim(),
					_clientFactory(apiKey!))
				: new SteamAccountConfig(entry.Id.ToString("D"), entry.Title, key));
		}

		_accounts = accounts;
		if (_artwork is null)
		{
			_artwork = new SteamArtworkCache(context.UiResources, _time);
		}
		else
		{
			_artwork.UseResources(context.UiResources);
		}

		_logo = await LogoAsync(context.UiResources);
		var tracker = new SteamTracker(_platform,
			accounts,
			[.. folders.Distinct(StringComparer.Ordinal)],
			_time,
			Publish,
			item => context.Events.Publish(item.Id, item.Payload));
		_tracker = tracker;

		PollLocalSafely(tracker);

		_cts = new CancellationTokenSource();
		var token = _cts.Token;
		var loops = new List<Task> { Task.Run(() => LocalLoopAsync(tracker, token), token) };

		if (tracker.HasWebApi)
		{
			loops.Add(Task.Run(() => WebLoopAsync(tracker, _selfInterval, static (t, ct) => t.PollSelfAsync(ct), token),
				token));
			loops.Add(Task.Run(()
					=> WebLoopAsync(tracker, _friendsInterval, static (t, ct) => t.PollFriendsAsync(ct), token),
				token));
			loops.Add(Task.Run(()
					=> WebLoopAsync(tracker, _ownedGamesInterval, static (t, ct) => t.PollOwnedGamesAsync(ct), token),
				token));
		}

		_loops = [.. loops];

		IsInitialized = true;
		Changed?.Invoke(this, EventArgs.Empty);
		_logger.Information("Steam integration started with {Accounts} account(s)", accounts.Count);
	}

	public void Dispose()
	{
		_cts?.Dispose();
		DisposeClients();
		_artwork?.Dispose();
	}

	public async Task ShutdownAsync()
	{
		IsInitialized = false;
		await StopAsync();
		Changed?.Invoke(this, EventArgs.Empty);
	}

	public Task InitializeAsync(IWidgetTypeProviderContext context, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(context);

		return RegisterAsync();

		async Task RegisterAsync()
		{
			foreach (var type in GetWidgetTypes())
			{
				await context.RegisterWidgetTypeAsync(type, cancellationToken);
			}
		}
	}

	public IReadOnlyList<WidgetTypeDescriptor> GetWidgetTypes()
	{
		return [CurrentlyPlayingType(), GameLauncherType(), FriendsType(), LibraryType()];
	}

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		return ValueTask.FromResult(SteamVariables.Read(localId, Snapshot, _time.GetUtcNow()));
	}

	public Task<SteamCommandOutcome> ExecuteAsync(SteamCommand command, CancellationToken cancellationToken)
	{
		return command.Kind == SteamCommandKind.CloseGame
			? CloseGameAsync(command.Force, cancellationToken)
			: _launcher.ExecuteAsync(command, cancellationToken);
	}

	public Task<DynamicOptionsResult> GetEventOptionsAsync(
		EventOptionsContext context,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);

		var snapshot = Snapshot;
		IReadOnlyList<ActionParameterOption> options = context.ParameterName switch
		{
			SteamEventPayload.Configuration =>
			[
				.. IsGameEvent(context.EventId)
					?
					[
						new ActionParameterOption
							{ Value = SteamEventPayload.ThisComputer, Label = WidgetStrings.ThisComputer() }
					]
					: Array.Empty<ActionParameterOption>(),
				.. _accounts.Where(account => account.HasWebApi)
					.Select(account => new ActionParameterOption { Value = account.EntryId, Label = account.Title })
			],
			SteamEventPayload.FriendId => FriendOptions(snapshot,
				context.CurrentParameters.GetValueOrDefault(SteamEventPayload.Configuration) as string,
				context.Filter?.Trim()),
			_ => []
		};

		return Task.FromResult(new DynamicOptionsResult { Options = options, CacheSeconds = 0 });
	}

	public Task<IReadOnlyList<IntegrationIssue>> GetIssuesAsync(CancellationToken cancellationToken = default)
	{
		var issues = new List<IntegrationIssue>();
		foreach (var account in Snapshot.Accounts)
		{
			if (account.Web == SteamWebState.InvalidKey)
			{
				issues.Add(new IntegrationIssue
				{
					Id = $"invalid-key:{account.EntryId}",
					Title = Strings.Issues.InvalidKey.Title(),
					Description = Strings.Issues.InvalidKey.Description(account.Title),
					Severity = IntegrationIssueSeverity.Error
				});
			}
			else if (account.Web == SteamWebState.Connected && account.Friends == SteamFriendsState.Private)
			{
				issues.Add(new IntegrationIssue
				{
					Id = $"friends-private:{account.EntryId}",
					Title = Strings.Issues.PrivateFriends.Title(),
					Description = Strings.Issues.PrivateFriends.Description(account.Title),
					Severity = IntegrationIssueSeverity.Warning
				});
			}

			if (account.Web == SteamWebState.Connected && account.Owned == SteamOwnedState.Private)
			{
				issues.Add(new IntegrationIssue
				{
					Id = $"games-private:{account.EntryId}",
					Title = Strings.Issues.PrivateGames.Title(),
					Description = Strings.Issues.PrivateGames.Description(account.Title),
					Severity = IntegrationIssueSeverity.Warning
				});
			}
		}

		return Task.FromResult<IReadOnlyList<IntegrationIssue>>(issues);
	}

	public Task<IssueResolution> ResolveIssueAsync(string issueId, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(IssueResolution.Failed());
	}

	internal static WidgetTypeDescriptor CurrentlyPlayingType()
	{
		return new WidgetTypeDescriptor(SteamWidgetType.CurrentlyPlayingLocalId,
			Strings.Widget.CurrentlyPlaying.Name(),
			Strings.Widget.CurrentlyPlaying.Description(),
			JsonSerializer.Serialize(new Dictionary<string, object>
			{
				[SteamWidgetType.ShowSessionTimeKey] = true,
				[SteamWidgetType.AccountKey] = string.Empty
			}),
			JsonSerializer.Serialize(new Dictionary<string, object>
			{
				["type"] = "object",
				["properties"] = new Dictionary<string, object>
				{
					[SteamWidgetType.ShowSessionTimeKey] = new { type = "boolean" },
					[SteamWidgetType.AccountKey] = new { type = "string" },
					[SteamWidgetType.BackgroundColorKey] = new
					{
						type = _nullableString, description = "#rrggbb or transparent"
					}
				}
			}),
			true)
		{
			AppearanceProperties = [WidgetAppearanceProperty.BackgroundColor]
		};
	}

	internal static WidgetTypeDescriptor GameLauncherType()
	{
		return new WidgetTypeDescriptor(SteamWidgetType.GameLauncherLocalId,
			Strings.Widget.GameLauncher.Name(),
			Strings.Widget.GameLauncher.Description(),
			JsonSerializer.Serialize(new Dictionary<string, object>
			{
				[SteamWidgetType.SourceKey] = SteamWidgetType.RecentSource,
				[SteamWidgetType.GameKey] = string.Empty,
				[SteamWidgetType.CountKey] = SteamWidgetType.DefaultCount
			}),
			JsonSerializer.Serialize(new Dictionary<string, object>
			{
				["type"] = "object",
				["properties"] = new Dictionary<string, object>
				{
					[SteamWidgetType.SourceKey] = new { type = "string", @enum = SteamWidgetType.Sources },
					[SteamWidgetType.GameKey] = new { type = "string" },
					[SteamWidgetType.CountKey] = new
					{
						type = "integer",
						minimum = SteamWidgetType.MinimumCount,
						maximum = SteamWidgetType.MaximumCount
					},
					[SteamWidgetType.FavoritesKey] = new { type = "array", items = new { type = "string" } },
					[SteamWidgetType.BackgroundColorKey] = new
					{
						type = _nullableString, description = "#rrggbb or transparent"
					}
				}
			}),
			true)
		{
			AppearanceProperties = [WidgetAppearanceProperty.BackgroundColor]
		};
	}

	private Task<SteamCommandOutcome> CloseGameAsync(bool force, CancellationToken cancellationToken)
	{
		return _tracker is { } tracker
			? _closer.CloseAsync(tracker.Snapshot, force, cancellationToken)
			: Task.FromResult(SteamCommandOutcome.NotAvailable);
	}

	private static bool IsGameEvent(string eventId)
	{
		return eventId is SteamEventIds.GameStarted or SteamEventIds.GameStopped;
	}

	private static List<ActionParameterOption> FriendOptions(SteamSnapshot snapshot, string? accountId, string? filter)
	{
		return
		[
			.. snapshot.Accounts
				.Where(account => string.IsNullOrEmpty(accountId) || account.EntryId == accountId)
				.SelectMany(account => account.FriendList)
				.DistinctBy(friend => friend.SteamId)
				.Where(friend
					=> string.IsNullOrEmpty(filter)
					|| friend.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
				.OrderBy(friend => friend.Name, StringComparer.CurrentCultureIgnoreCase)
				.Select(friend => new ActionParameterOption { Value = friend.SteamId, Label = friend.Name })
		];
	}

	internal static WidgetTypeDescriptor FriendsType()
	{
		return AccountWidgetType(SteamWidgetType.FriendsLocalId,
			Strings.Widget.Friends.Name(),
			Strings.Widget.Friends.Description());
	}

	internal static WidgetTypeDescriptor LibraryType()
	{
		return AccountWidgetType(SteamWidgetType.LibraryLocalId,
			Strings.Widget.Library.Name(),
			Strings.Widget.Library.Description());
	}

	private static WidgetTypeDescriptor AccountWidgetType(string localId, LocalizedText name, LocalizedText description)
	{
		return new WidgetTypeDescriptor(localId,
			name,
			description,
			JsonSerializer.Serialize(new Dictionary<string, object>
			{
				[SteamWidgetType.AccountKey] = string.Empty
			}),
			JsonSerializer.Serialize(new Dictionary<string, object>
			{
				["type"] = "object",
				["properties"] = new Dictionary<string, object>
				{
					[SteamWidgetType.AccountKey] = new { type = "string" },
					[SteamWidgetType.BackgroundColorKey] = new
					{
						type = _nullableString, description = "#rrggbb or transparent"
					}
				}
			}),
			true)
		{
			AppearanceProperties = [WidgetAppearanceProperty.BackgroundColor]
		};
	}

	private void Publish(SteamSnapshot snapshot)
	{
		var previous = Interlocked.Exchange(ref _published, snapshot);
		foreach (var account in snapshot.Accounts)
		{
			var before = previous.Accounts.FirstOrDefault(candidate => candidate.EntryId == account.EntryId);
			if (before is null || before.Web != account.Web || before.Friends != account.Friends)
			{
				_logger.Information("Steam account {Account}: Web API is {Web}, friends are {Friends}",
					account.Title,
					account.Web,
					account.Friends);
			}
		}

		Changed?.Invoke(this, EventArgs.Empty);
	}

	private async Task StopAsync()
	{
		var cts = _cts;
		var loops = _loops;
		_cts = null;
		_loops = [];
		_tracker = null;

		if (cts is not null)
		{
			await cts.CancelAsync();
		}

		try
		{
			await Task.WhenAll(loops);
		}
		catch (OperationCanceledException)
		{
		}

		cts?.Dispose();
		DisposeClients();
		_accounts = [];
	}

	private void DisposeClients()
	{
		foreach (var account in _accounts)
		{
			(account.Client as IDisposable)?.Dispose();
		}
	}

	private static void PollLocalSafely(SteamTracker tracker)
	{
		try
		{
			tracker.PollLocal();
		}
		catch (Exception exception) when (exception is IOException
			or UnauthorizedAccessException
			or ArgumentException
			or NotSupportedException)
		{
			_logger.Warning(exception, "Steam local detection failed");
		}
	}

	private static async Task LocalLoopAsync(SteamTracker tracker, CancellationToken cancellationToken)
	{
		try
		{
			while (true)
			{
				await Task.Delay(tracker.SteamFound ? _localInterval : _undetectedInterval, cancellationToken);

				PollLocalSafely(tracker);
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	private static async Task WebLoopAsync(
		SteamTracker tracker,
		TimeSpan interval,
		Func<SteamTracker, CancellationToken, Task> poll,
		CancellationToken cancellationToken)
	{
		try
		{
			while (true)
			{
				await poll(tracker, cancellationToken);
				await Task.Delay(interval, cancellationToken);
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	private static byte[] LoadIcon()
	{
		var assembly = typeof(SteamIntegration).Assembly;
		using var stream = assembly.GetManifestResourceStream(
				$"{typeof(SteamIntegration).Namespace}.Icons.{SteamWidgetSessions.IconFolder}.{SteamWidgetSessions.LogoIcon}.svg")
			!;
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.ToArray();
	}
}
