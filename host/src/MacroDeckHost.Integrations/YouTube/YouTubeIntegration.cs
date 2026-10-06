using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Application.StreamChat;
using MacroDeckHost.Application.StreamStats;
using MacroDeckHost.Integrations.YouTube.Actions;
using MacroDeckHost.Integrations.YouTube.Auth;
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
		IStreamChatSinkConsumer,
		IStreamStatsSinkConsumer,
		IStreamChatModerator,
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

	private IStreamChatSink? _chatSink;
	private IStreamStatsSink? _statsSink;

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

	public byte[] GetIcon() => _icon;

	public IConfigFlow CreateConfigFlow() => _configFlowFactory();

	public async Task InitializeAsync(IIntegrationContext context)
	{
		_accounts.UseChatSink(_chatSink);
		await _accounts.ReloadAsync(context.Config, new YouTubeEventEmitter(context.Events));
		_chatSink?.SetAccounts(_accounts.ChatAccounts());
		_statsSink?.SetAccounts(_accounts.StatsAccounts());
		_accounts.StartAll();
		IsInitialized = true;
	}

	public async Task ShutdownAsync()
	{
		IsInitialized = false;
		_chatSink?.SetAccounts([]);
		_statsSink?.SetAccounts([]);
		await _accounts.ShutdownAsync();
	}

	public void UseStreamChatSink(IStreamChatSink sink) => _chatSink = sink;

	public void UseStreamStatsSink(IStreamStatsSink sink) => _statsSink = sink;

	public Task<ChatModerationResult> ModerateAsync(
		string accountId,
		ChatModerationRequest request,
		CancellationToken cancellationToken)
		=> _moderator.ModerateAsync(accountId, request, cancellationToken);

	public async Task InitializeAsync(IWidgetTypeProviderContext context, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(context);

		foreach (var descriptor in GetWidgetTypes())
		{
			await context.RegisterWidgetTypeAsync(descriptor, cancellationToken);
		}
	}

	public IReadOnlyList<WidgetTypeDescriptor> GetWidgetTypes()
		=> _accounts.Connections.Count == 0
			? []
			: [StreamPlatforms.YouTube.ChatWidgetDescriptor(), StreamPlatforms.YouTube.StatsWidgetDescriptor()];

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
		=> Task.FromResult(_accounts.Issues());

	public Task<IssueResolution> ResolveIssueAsync(string issueId, CancellationToken cancellationToken = default)
		=> Task.FromResult(issueId.StartsWith(YouTubeAccountManager.TokenIssuePrefix, StringComparison.Ordinal)
			? IssueResolution.Ok(AppStrings.Integrations.YouTube.Issues.ReconnectResolution(),
				IssueResolutionFollowUp.StartConfigFlow)
			: IssueResolution.Failed(AppStrings.Integrations.Issues.UnknownIssue()));

	public void Dispose() => _accounts.Dispose();

	private static byte[] LoadIcon()
	{
		var assembly = typeof(YouTubeIntegration).Assembly;
		var name = assembly.GetManifestResourceNames()
			.First(n => n.EndsWith(".YouTube.youtube-icon.svg", StringComparison.Ordinal));
		using var stream = assembly.GetManifestResourceStream(name)!;
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.ToArray();
	}
}
