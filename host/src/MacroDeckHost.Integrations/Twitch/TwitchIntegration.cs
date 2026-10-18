using MacroDeckHost.Integrations.Streaming;
using MacroDeckHost.Integrations.Streaming.Widgets;
using MacroDeckHost.Integrations.Twitch.Actions;
using MacroDeckHost.Integrations.Twitch.Auth;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Migration;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Integrations.Twitch.Chat;
using MacroDeckHost.Integrations.Twitch.Configuration;
using MacroDeckHost.Integrations.Twitch.Connections;
using MacroDeckHost.Integrations.Twitch.Events;
using MacroDeckHost.Integrations.Twitch.Migration;
using MacroDeckHost.Integrations.Twitch.Variables;
using MacroDeckHost.Integrations.Twitch.Widgets;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Twitch;

[MacroDeckIntegration]
public sealed class TwitchIntegration
	: IIntegration,
		IConfigFlowProvider,
		IVariableProvider,
		IIntegrationIconProvider,
		IEventProvider,
		IDynamicEventOptionsProvider,
		IIntegrationIssueProvider,
		IMigrationProvider,
		IWidgetTypeProvider,
		IUiProvider,
		IDisposable
{
	public const string IntegrationId = "app.macro-deck.twitch";

	private static readonly ILogger _logger = IntegrationLog.For<TwitchIntegration>(IntegrationId);
	private static readonly byte[] _icon = LoadIcon();

	private static readonly IReadOnlyList<VariableDefinition> _templateVariables =
		TwitchVariables.Declare(VariableNameTemplate.Placeholder("account"));

	private readonly TwitchAccountManager _accounts;
	private readonly TwitchChatModerator _moderator;
	private readonly StreamPump _pump;
	private readonly StreamStatsSource _stats;
	private readonly StreamThumbnailCache _thumbnails;
	private readonly TwitchChatImageCache? _ownedImages;
	private readonly StreamWidgetsUiProvider _ui;

	private IIntegrationContext? _context;
	private IVariableApi? _variables;
	private IUserVariableApi? _userVariables;

	public TwitchIntegration()
		: this(new TwitchAccountManager(() => new TwitchOAuthClient(), _logger))
	{
	}

	internal TwitchIntegration(
		TwitchAccountManager accounts,
		IStreamChatImages? images = null,
		TimeProvider? time = null)
	{
		var clock = time ?? TimeProvider.System;

		_accounts = accounts;
		_moderator = new TwitchChatModerator(() => _accounts);
		_ownedImages = images is null ? new TwitchChatImageCache(() => _context?.UiResources, clock, _logger) : null;
		Chat = new StreamChatHub(clock, _logger, images ?? _ownedImages);
		_pump = new StreamPump(Chat);
		_stats = new StreamStatsSource(ReadStats);
		_thumbnails = new StreamThumbnailCache(TwitchStreamPlatform.Platform,
			() => _context?.UiResources,
			clock,
			_logger);
		_ui = new StreamWidgetsUiProvider(TwitchStreamPlatform.Platform,
			Chat,
			images ?? _ownedImages,
			_stats,
			_thumbnails,
			() => _context,
			() => IsInitialized ? _moderator : null,
			clock,
			_logger);
		_accounts.UseChatSink(Chat);
		Actions = TwitchActions.Create(() => _accounts, () => _variables, userVariables: () => _userVariables);
	}

	public string Id => IntegrationId;

	// "Twitch" is a brand name alone, not a sentence - left as a literal rather than a translated key.
	public LocalizedText Name => "Twitch";

	public string Version => "1.0.0";

	public bool IsInitialized { get; private set; }

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public string IconMimeType => "image/svg+xml";

	public IReadOnlyList<EventDefinition> EventDefinitions => TwitchEventDefinitions.All;

	public bool AllowsMultipleConfigurations => true;

	public IReadOnlyList<IIntegrationMigration> Migrations { get; } = [new TwitchMacroDeck2Migration()];

	public IReadOnlyList<VariableDefinition> Variables
		=>
		[
			.. _accounts.Connections.SelectMany(c => TwitchVariables.Declare(c.Account.VariableKey,
				new VariableConfiguration(c.Account.VariableKey, c.Account.Label)))
		];

	public bool VariablesDependOnConfiguration => true;

	public IReadOnlyList<VariableDefinition> DeclaredVariables
	{
		get
		{
			var provided = Variables;
			return provided.Count > 0 ? provided : _templateVariables;
		}
	}

	internal TwitchAccountManager AccountManager => _accounts;

	internal StreamChatHub Chat { get; }

	internal IStreamStatsSource Stats => _stats;

	internal IStreamChatModerator Moderator => _moderator;

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces => _ui.Surfaces;

	public byte[] GetIcon()
	{
		return _icon;
	}

	public IConfigFlow CreateConfigFlow()
	{
		return new TwitchConfigFlow();
	}

	public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		return _ui.CreateSessionAsync(request, cancellationToken);
	}

	public async Task InitializeAsync(IIntegrationContext context)
	{
		IntegrationHttp.Use(context.Http);
		// Captured so an action built in the constructor can write its result variable later;
		// ActionExecutionContext deliberately exposes no variable API.
		_context = context;
		_variables = context.Variables;
		_userVariables = context.UserVariables;

		_pump.Start();
		await _accounts.ReloadAsync(context.Config,
			new TwitchEventEmitter(context.Events),
			() => _ = _variables?.RequestRefreshAsync());
		Chat.SetAccounts(_accounts.ChatAccounts());
		_stats.SetAccounts(_accounts.StatsAccounts());
		_accounts.StartAll();
		IsInitialized = true;
	}

	public async Task ShutdownAsync()
	{
		// Drains a token rotation that is still in flight. Losing it would leave the stored refresh
		// token permanently dead, because Twitch already invalidated it when it issued the new one.
		IsInitialized = false;
		Chat.SetAccounts([]);
		_stats.SetAccounts([]);
		await _accounts.ShutdownAsync();
		await _pump.StopAsync();
		_ownedImages?.Clear();
		_thumbnails.Clear();
	}

	public async Task InitializeAsync(IWidgetTypeProviderContext context, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(context);

		if (_accounts.Connections.Count == 0)
		{
			return;
		}

		await context.RegisterWidgetTypeAsync(ChatWidgetType(), cancellationToken);
		await context.RegisterWidgetTypeAsync(StatsWidgetType(), cancellationToken);
	}

	public IReadOnlyList<WidgetTypeDescriptor> GetWidgetTypes()
	{
		return _accounts.Connections.Count == 0 ? [] : [ChatWidgetType(), StatsWidgetType()];
	}

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		if (TwitchVariables.SplitDefinitionId(localId) is not { } split)
		{
			return ValueTask.FromResult(VariableReading.Unavailable);
		}

		var connection = _accounts.Connections.FirstOrDefault(c =>
			string.Equals(c.Account.VariableKey, split.VariableKey, StringComparison.Ordinal));

		return ValueTask.FromResult(connection is null
			? VariableReading.Unavailable
			: VariableReading.Of(TwitchVariables.Read(connection.Account, connection.State, split.Name)));
	}

	public Task<DynamicOptionsResult> GetEventOptionsAsync(
		EventOptionsContext context,
		CancellationToken cancellationToken)
	{
		var options = context.ParameterName switch
		{
			"account" => _accounts.AccountOptions(),
			"rewardTitle" => RewardOptions(context.CurrentParameters.GetValueOrDefault("account") as string),
			"type" => TwitchEventCatalog.Types.Select(type => new ActionParameterOption { Value = type }).ToList(),
			_ => []
		};

		return Task.FromResult(new DynamicOptionsResult
		{
			Options = options,

			AllowsCustomValue = true,
			CacheSeconds = 30
		});
	}

	public Task<IReadOnlyList<IntegrationIssue>> GetIssuesAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult(_accounts.Issues());
	}

	public Task<IssueResolution> ResolveIssueAsync(string issueId, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(issueId.StartsWith(TwitchAccountManager.TokenIssuePrefix, StringComparison.Ordinal)
			|| issueId.StartsWith(TwitchAccountManager.MissingScopeIssuePrefix, StringComparison.Ordinal)
			|| issueId.StartsWith(TwitchAccountManager.MissingChattersScopeIssuePrefix, StringComparison.Ordinal)
				? IssueResolution.Ok(AppStrings.Integrations.Twitch.Issues.ReconnectAccountResolution(),
					IssueResolutionFollowUp.StartConfigFlow)
				: IssueResolution.Failed(AppStrings.Integrations.Issues.UnknownIssue()));
	}

	public void Dispose()
	{
		_accounts.Dispose();
		_pump.Dispose();
		Chat.Dispose();
		_ownedImages?.Dispose();
		_thumbnails.Dispose();
	}

	private static WidgetTypeDescriptor ChatWidgetType()
	{
		return TwitchStreamPlatform.Platform.ChatWidgetDescriptor();
	}

	private static WidgetTypeDescriptor StatsWidgetType()
	{
		return TwitchStreamPlatform.Platform.StatsWidgetDescriptor();
	}

	private object? ReadStats(StreamStatsAccount account, string name)
	{
		var connection = _accounts.Connections.FirstOrDefault(candidate =>
			string.Equals(candidate.Account.UserId, account.AccountId, StringComparison.Ordinal));

		return connection is null ? null : TwitchVariables.Read(connection.Account, connection.State, name);
	}

	private IReadOnlyList<ActionParameterOption> RewardOptions(string? accountId)
	{
		var connection = _accounts.Resolve(accountId);

		return connection is null
			? []
			: [.. connection.Rewards.Select(reward => new ActionParameterOption { Value = reward.Title })];
	}

	private static byte[] LoadIcon()
	{
		var assembly = typeof(TwitchIntegration).Assembly;
		using var stream = assembly.GetManifestResourceStream(
				$"{typeof(TwitchIntegration).Namespace}.Icons.{StreamWidgetsUiProvider.IconFolder}.{StreamWidgetsUiProvider.LogoIcon}.svg")
			!;
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.ToArray();
	}
}
