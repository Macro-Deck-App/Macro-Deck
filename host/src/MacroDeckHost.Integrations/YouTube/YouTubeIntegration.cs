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
using MacroDeckHost.Integrations.Streaming;
using MacroDeckHost.Integrations.Streaming.Widgets;
using MacroDeckHost.Integrations.YouTube.Actions;
using MacroDeckHost.Integrations.YouTube.Auth;
using MacroDeckHost.Integrations.YouTube.Chat;
using MacroDeckHost.Integrations.YouTube.Configuration;
using MacroDeckHost.Integrations.YouTube.Connections;
using MacroDeckHost.Integrations.YouTube.Events;
using MacroDeckHost.Integrations.YouTube.Variables;
using MacroDeckHost.Integrations.YouTube.Widgets;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.YouTube;

[MacroDeckIntegration]
public sealed class YouTubeIntegration
	: IIntegration,
		IConfigFlowProvider,
		IVariableProvider,
		IIntegrationIconProvider,
		IEventProvider,
		IDynamicEventOptionsProvider,
		IIntegrationIssueProvider,
		IWidgetTypeProvider,
		IUiProvider,
		IDisposable
{
	public const string IntegrationId = "app.macro-deck.youtube";

	private static readonly ILogger _logger = IntegrationLog.For<YouTubeIntegration>(IntegrationId);
	private static readonly byte[] _icon = LoadIcon();

	private static readonly IReadOnlyList<VariableDefinition> _templateVariables =
		YouTubeVariables.Declare(VariableNameTemplate.Placeholder("account"));

	private readonly YouTubeAccountManager _accounts;
	private readonly YouTubeChatModerator _moderator;
	private readonly Func<IConfigFlow> _configFlowFactory;
	private readonly TimeProvider _time;
	private readonly StreamPump _pump;
	private readonly StreamStatsSource _stats;
	private readonly StreamThumbnailCache _thumbnails;
	private readonly StreamWidgetsUiProvider _ui;

	private IIntegrationContext? _context;

	public YouTubeIntegration()
		: this(new YouTubeAccountManager(() => new YouTubeOAuthClient(), _logger),
			() => new YouTubeConfigFlow(),
			TimeProvider.System)
	{
	}

	internal YouTubeIntegration(
		YouTubeAccountManager accounts,
		Func<IConfigFlow>? configFlowFactory = null,
		TimeProvider? time = null)
	{
		_accounts = accounts;
		_configFlowFactory = configFlowFactory ?? (() => new YouTubeConfigFlow());
		_time = time ?? TimeProvider.System;
		_moderator = new YouTubeChatModerator(() => _accounts);
		Chat = new StreamChatHub(_time, _logger);
		_pump = new StreamPump(Chat);
		_stats = new StreamStatsSource(ReadStats);
		_thumbnails = new StreamThumbnailCache(YouTubeStreamPlatform.Platform,
			() => _context?.UiResources,
			_time,
			_logger);
		_ui = new StreamWidgetsUiProvider(YouTubeStreamPlatform.Platform,
			Chat,
			null,
			_stats,
			_thumbnails,
			() => _context,
			() => IsInitialized ? _moderator : null,
			_time,
			_logger);
		_accounts.UseChatSink(Chat);
		Actions = YouTubeActions.Create(() => _accounts);
	}

	public string Id => IntegrationId;

	// "YouTube" is a brand name alone, not a sentence - left as a literal rather than a translated key.
	public LocalizedText Name => "YouTube";

	public string Version => "1.0.0";

	public bool IsInitialized { get; private set; }

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public string IconMimeType => "image/svg+xml";

	public IReadOnlyList<EventDefinition> EventDefinitions => YouTubeEventDefinitions.All;

	public bool AllowsMultipleConfigurations => true;

	public IReadOnlyList<VariableDefinition> Variables
		=>
		[
			.. _accounts.Connections.SelectMany(c => YouTubeVariables.Declare(c.Account.VariableKey,
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

	internal YouTubeAccountManager AccountManager => _accounts;

	internal StreamChatHub Chat { get; }

	internal IStreamStatsSource Stats => _stats;

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces => _ui.Surfaces;

	public byte[] GetIcon()
	{
		return _icon;
	}

	public IConfigFlow CreateConfigFlow()
	{
		return _configFlowFactory();
	}

	public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		return _ui.CreateSessionAsync(request, cancellationToken);
	}

	public async Task InitializeAsync(IIntegrationContext context)
	{
		IntegrationHttp.Use(context.Http);
		_context = context;
		_pump.Start();
		await _accounts.ReloadAsync(context.Config, new YouTubeEventEmitter(context.Events));
		Chat.SetAccounts(_accounts.ChatAccounts());
		_stats.SetAccounts(_accounts.StatsAccounts());
		_accounts.StartAll();
		IsInitialized = true;
	}

	public async Task ShutdownAsync()
	{
		IsInitialized = false;
		Chat.SetAccounts([]);
		_stats.SetAccounts([]);
		await _accounts.ShutdownAsync();
		await _pump.StopAsync();
		_thumbnails.Clear();
	}

	public async Task InitializeAsync(IWidgetTypeProviderContext context, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(context);

		foreach (var descriptor in GetWidgetTypes())
		{
			await context.RegisterWidgetTypeAsync(descriptor, cancellationToken);
		}
	}

	public IReadOnlyList<WidgetTypeDescriptor> GetWidgetTypes()
	{
		return _accounts.Connections.Count == 0
			? []
			:
			[
				YouTubeStreamPlatform.Platform.ChatWidgetDescriptor(),
				YouTubeStreamPlatform.Platform.StatsWidgetDescriptor()
			];
	}

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		if (YouTubeVariables.SplitDefinitionId(localId) is not { } split)
		{
			return ValueTask.FromResult(VariableReading.Unavailable);
		}

		var connection = _accounts.Connections.FirstOrDefault(c =>
			string.Equals(c.Account.VariableKey, split.VariableKey, StringComparison.Ordinal));

		var value = connection is null
			? null
			: YouTubeVariables.Read(connection.Account, connection.State, split.Name, _time.GetUtcNow());

		return ValueTask.FromResult(value is null ? VariableReading.Unavailable : VariableReading.Of(value));
	}

	public Task<DynamicOptionsResult> GetEventOptionsAsync(
		EventOptionsContext context,
		CancellationToken cancellationToken)
	{
		var options = context.ParameterName switch
		{
			"account" => _accounts.AccountOptions(),
			"type" => YouTubeEventIds.Specific.Select(id => new ActionParameterOption { Value = id }).ToList(),
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
		return Task.FromResult(issueId.StartsWith(YouTubeAccountManager.TokenIssuePrefix, StringComparison.Ordinal)
			? IssueResolution.Ok(AppStrings.Integrations.YouTube.Issues.ReconnectResolution(),
				IssueResolutionFollowUp.StartConfigFlow)
			: IssueResolution.Failed(AppStrings.Integrations.Issues.UnknownIssue()));
	}

	public void Dispose()
	{
		_accounts.Dispose();
		_pump.Dispose();
		Chat.Dispose();
		_thumbnails.Dispose();
	}

	private object? ReadStats(StreamStatsAccount account, string name)
	{
		var connection = _accounts.Connections.FirstOrDefault(candidate =>
			string.Equals(candidate.Account.ChannelId, account.AccountId, StringComparison.Ordinal));

		return connection is null
			? null
			: YouTubeVariables.Read(connection.Account, connection.State, name, _time.GetUtcNow());
	}

	private static byte[] LoadIcon()
	{
		var assembly = typeof(YouTubeIntegration).Assembly;
		using var stream = assembly.GetManifestResourceStream(
				$"{typeof(YouTubeIntegration).Namespace}.Icons.{StreamWidgetsUiProvider.IconFolder}.{StreamWidgetsUiProvider.LogoIcon}.svg")
			!;
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.ToArray();
	}
}
