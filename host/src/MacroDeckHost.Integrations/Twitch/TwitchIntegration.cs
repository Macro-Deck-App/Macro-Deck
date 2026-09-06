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
		IDisposable
{
	public const string IntegrationId = "app.macro-deck.twitch";

	private static readonly ILogger _logger = IntegrationLog.For<TwitchIntegration>(IntegrationId);
	private static readonly byte[] _icon = LoadIcon();

	private static readonly IReadOnlyList<VariableDefinition> _templateVariables =
		TwitchVariables.Declare(VariableNameTemplate.Placeholder("account"));

	private readonly TwitchAccountManager _accounts;

	private IVariableApi? _variables;

	public TwitchIntegration()
		: this(new TwitchAccountManager(() => new TwitchOAuthClient(), _logger))
	{
	}

	internal TwitchIntegration(TwitchAccountManager accounts)
	{
		_accounts = accounts;
		Actions = TwitchActions.Create(() => _accounts, () => _variables);
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

		await _accounts.ReloadAsync(context.Config, new TwitchEventEmitter(context.Events));
		_accounts.StartAll();
		IsInitialized = true;
	}

	public async Task ShutdownAsync()
	{
		// Drains a token rotation that is still in flight. Losing it would leave the stored refresh
		// token permanently dead, because Twitch already invalidated it when it issued the new one.
		IsInitialized = false;
		await _accounts.ShutdownAsync();
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
		=> Task.FromResult(_accounts.Issues());

	public Task<IssueResolution> ResolveIssueAsync(string issueId, CancellationToken cancellationToken = default)
		=> Task.FromResult(issueId.StartsWith(TwitchAccountManager.TokenIssuePrefix, StringComparison.Ordinal) ||
			issueId.StartsWith(TwitchAccountManager.MissingScopeIssuePrefix, StringComparison.Ordinal)
				? IssueResolution.Ok(AppStrings.Integrations.Twitch.Issues.ReconnectAccountResolution(),
					IssueResolutionFollowUp.StartConfigFlow)
				: IssueResolution.Failed(AppStrings.Integrations.Issues.UnknownIssue()));

	public void Dispose() => _accounts.Dispose();

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
