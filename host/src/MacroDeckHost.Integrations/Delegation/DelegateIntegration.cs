using MacroDeck.Localization;
using MacroDeckHost.Integrations.Delegation.Protocol;
using MacroDeckHost.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using Serilog;

namespace MacroDeckHost.Integrations.Delegation;

[MacroDeckIntegration]
public sealed class DelegateIntegration
	: IIntegration, IConfigFlowProvider, IVariableProvider, IIntegrationIconProvider, IIntegrationIssueProvider,
		IDisposable
{
	public const string IntegrationId = "app.macro-deck.delegate";

	private static readonly ILogger _logger = IntegrationLog.For<DelegateIntegration>(IntegrationId);
	private static readonly byte[] _icon = LoadIcon();

	private static readonly IReadOnlyList<VariableDefinition> _templateVariables =
		[DelegateVariables.Declare(VariableNameTemplate.Placeholder("instance"))];

	private readonly DelegateRemoteManager _remotes;

	private IIntegrationConfig? _config;

	public DelegateIntegration()
		: this(new DelegateRemoteManager(() => new DelegateClient(), TimeProvider.System, _logger))
	{
	}

	internal DelegateIntegration(DelegateRemoteManager remotes)
	{
		_remotes = remotes;
		Actions = [new RunRemoteScriptActionDefinition(() => _remotes)];
	}

	public string Id => IntegrationId;

	public LocalizedText Name => AppStrings.Integrations.Delegation.Name();

	public string Version => "1.0.0";

	public bool IsInitialized { get; private set; }

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public string IconMimeType => "image/png";

	public bool AllowsMultipleConfigurations => true;

	public IReadOnlyList<VariableDefinition> Variables
		=>
		[
			.. _remotes.Remotes.Select(remote => DelegateVariables.Declare(remote.Instance.VariableKey,
				new VariableConfiguration(remote.Instance.EntryId.ToString("N"), remote.Instance.MachineName)))
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

	internal DelegateRemoteManager RemoteManager => _remotes;

	public byte[] GetIcon() => _icon;

	public IConfigFlow CreateConfigFlow()
		=> new DelegateConfigFlow(() => new DelegateClient(), _config, TimeProvider.System);

	public async Task InitializeAsync(IIntegrationContext context)
	{
		_config = context.Config;

		await _remotes.ReloadAsync(context.Config);
		_remotes.StartAll();
		IsInitialized = true;
	}

	public async Task ShutdownAsync()
	{
		IsInitialized = false;
		await _remotes.ShutdownAsync();
	}

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		var key = DelegateVariables.KeyOfDefinitionId(localId);
		if (key is null)
		{
			return ValueTask.FromResult(VariableReading.Unavailable);
		}

		var remote = _remotes.Remotes
			.FirstOrDefault(r => string.Equals(r.Instance.VariableKey, key, StringComparison.Ordinal));

		return ValueTask.FromResult(remote is null
			? VariableReading.Unavailable
			: VariableReading.Of(DelegateVariables.Read(remote)));
	}

	public Task<IReadOnlyList<IntegrationIssue>> GetIssuesAsync(CancellationToken cancellationToken = default)
		=> Task.FromResult(_remotes.Issues());

	public Task<IssueResolution> ResolveIssueAsync(string issueId, CancellationToken cancellationToken = default)
		=> Task.FromResult(
			issueId.StartsWith(DelegateRemoteManager.CredentialsRejectedIssuePrefix, StringComparison.Ordinal) ||
			issueId.StartsWith(DelegateRemoteManager.UnreachableIssuePrefix, StringComparison.Ordinal) ||
			issueId.StartsWith(DelegateRemoteManager.SelfDelegationIssuePrefix, StringComparison.Ordinal)
				? IssueResolution.Ok(followUp: IssueResolutionFollowUp.StartConfigFlow)
				: IssueResolution.Failed(AppStrings.Integrations.Issues.UnknownIssue()));

	public void Dispose() => _remotes.Dispose();

	private static byte[] LoadIcon()
	{
		var assembly = typeof(DelegateIntegration).Assembly;
		var name = assembly.GetManifestResourceNames()
			.First(n => n.EndsWith("delegate-icon.png", StringComparison.Ordinal));
		using var stream = assembly.GetManifestResourceStream(name)!;
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.ToArray();
	}
}
