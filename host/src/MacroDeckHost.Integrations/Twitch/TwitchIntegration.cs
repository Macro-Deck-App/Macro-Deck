using MacroDeckHost.Application.Twitch.Chat;
using MacroDeckHost.Application.Twitch.Stats;
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
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
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
		ITwitchChatSinkConsumer,
		ITwitchStatsSinkConsumer,
		ITwitchChatModerator,
		IDisposable
{
	public const string IntegrationId = "app.macro-deck.twitch";

	private static readonly ILogger _logger = IntegrationLog.For<TwitchIntegration>(IntegrationId);
	private static readonly byte[] _icon = LoadIcon();

	private static readonly IReadOnlyList<VariableDefinition> _templateVariables =
		TwitchVariables.Declare(VariableNameTemplate.Placeholder("account"));

	private readonly TwitchAccountManager _accounts;
	private readonly TwitchChatModerator _moderator;

	private const string ChatDataSchema
		= """{"type":"object","properties":{"account":{"type":"string"},"allowModeration":{"type":"boolean"},"backgroundColor":{"type":["string","null"],"description":"#rrggbb or transparent"},"textSize":{"type":["number","null"],"minimum":25,"maximum":300,"description":"Chat text size in percent, 100 when unset"},"messageColor":{"type":["string","null"],"description":"#rrggbb for the chat message text, the theme text colour when unset"},"nameColor":{"type":["string","null"],"description":"#rrggbb for every chatter name, each chatter's own Twitch colour when unset"}}}""";

	private const string StatsDataSchema
		= """{"type":"object","properties":{"account":{"type":"string"},"style":{"type":"string","enum":["overview","statsRow","liveRow","valueGraph","value"]},"metric":{"type":"string","enum":["viewers","chatters","followers","subscribers"]},"tiles":{"type":"array","items":{"type":"string","enum":["viewers","chatters","followers","subscribers"]}},"details":{"type":"array","items":{"type":"string","enum":["title","category","uptime"]}},"showThumbnail":{"type":"boolean"},"backgroundColor":{"type":["string","null"],"description":"#rrggbb or transparent"}}}""";

	private ITwitchChatSink? _chatSink;
	private ITwitchStatsSink? _statsSink;
	private IVariableApi? _variables;
	private IUserVariableApi? _userVariables;

	public TwitchIntegration()
		: this(new TwitchAccountManager(() => new TwitchOAuthClient(), _logger))
	{
	}

	internal TwitchIntegration(TwitchAccountManager accounts)
	{
		_accounts = accounts;
		_moderator = new TwitchChatModerator(() => _accounts);
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

	public byte[] GetIcon() => _icon;

	public IConfigFlow CreateConfigFlow() => new TwitchConfigFlow();

	public async Task InitializeAsync(IIntegrationContext context)
	{
		// Captured so an action built in the constructor can write its result variable later;
		// ActionExecutionContext deliberately exposes no variable API.
		_variables = context.Variables;
		_userVariables = context.UserVariables;

		_accounts.UseChatSink(_chatSink);
		await _accounts.ReloadAsync(context.Config, new TwitchEventEmitter(context.Events));
		_chatSink?.SetAccounts(_accounts.ChatAccounts());
		_statsSink?.SetAccounts(_accounts.StatsAccounts());
		_accounts.StartAll();
		IsInitialized = true;
	}

	public async Task ShutdownAsync()
	{
		// Drains a token rotation that is still in flight. Losing it would leave the stored refresh
		// token permanently dead, because Twitch already invalidated it when it issued the new one.
		IsInitialized = false;
		_chatSink?.SetAccounts([]);
		_statsSink?.SetAccounts([]);
		await _accounts.ShutdownAsync();
	}

	public void UseTwitchChatSink(ITwitchChatSink sink) => _chatSink = sink;

	public void UseTwitchStatsSink(ITwitchStatsSink sink) => _statsSink = sink;

	public Task<TwitchChatModerationResult> ModerateAsync(
		string accountId,
		TwitchChatModerationRequest request,
		CancellationToken cancellationToken)
		=> _moderator.ModerateAsync(accountId, request, cancellationToken);

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
		=> _accounts.Connections.Count == 0 ? [] : [ChatWidgetType(), StatsWidgetType()];

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
		=> Task.FromResult(_accounts.Issues());

	public Task<IssueResolution> ResolveIssueAsync(string issueId, CancellationToken cancellationToken = default)
		=> Task.FromResult(issueId.StartsWith(TwitchAccountManager.TokenIssuePrefix, StringComparison.Ordinal) ||
			issueId.StartsWith(TwitchAccountManager.MissingScopeIssuePrefix, StringComparison.Ordinal) ||
			issueId.StartsWith(TwitchAccountManager.MissingChattersScopeIssuePrefix, StringComparison.Ordinal)
				? IssueResolution.Ok(AppStrings.Integrations.Twitch.Issues.ReconnectAccountResolution(),
					IssueResolutionFollowUp.StartConfigFlow)
				: IssueResolution.Failed(AppStrings.Integrations.Issues.UnknownIssue()));

	public void Dispose() => _accounts.Dispose();

	private static WidgetTypeDescriptor ChatWidgetType()
		=> new(TwitchChatWidgetType.LocalId,
			AppStrings.Integrations.Twitch.ChatWidget.Name(),
			AppStrings.Integrations.Twitch.ChatWidget.Description(),
			DefaultData: """{"account":"","allowModeration":true}""",
			DataSchema: ChatDataSchema,
			HasConfiguration: true)
		{
			AppearanceProperties = [WidgetAppearanceProperty.BackgroundColor],
		};

	private static WidgetTypeDescriptor StatsWidgetType()
		=> new(TwitchStatsWidgetType.LocalId,
			AppStrings.Integrations.Twitch.StatsWidget.Name(),
			AppStrings.Integrations.Twitch.StatsWidget.Description(),
			DefaultData: $$"""{"account":"","style":"{{TwitchStatsWidgetType.DefaultStyle}}","metric":"{{TwitchStatsWidgetType.DefaultMetric}}","tiles":["viewers","chatters","followers"],"details":["title","category","uptime"],"showThumbnail":true}""",
			DataSchema: StatsDataSchema,
			HasConfiguration: true)
		{
			AppearanceProperties = [WidgetAppearanceProperty.BackgroundColor],
		};

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
		var name = assembly.GetManifestResourceNames()
			.First(n => n.EndsWith("twitch-icon.svg", StringComparison.Ordinal));
		using var stream = assembly.GetManifestResourceStream(name)!;
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.ToArray();
	}
}
