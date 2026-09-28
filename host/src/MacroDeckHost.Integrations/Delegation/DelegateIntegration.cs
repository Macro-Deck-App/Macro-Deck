using MacroDeck.Localization;
using MacroDeckHost.Integrations.Delegation.Protocol;
using MacroDeckHost.Application.Variables;
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
		IVariablePollingInvalidationConsumer, IDisposable
{
	public const string IntegrationId = SharedVariables.DelegateIntegrationId;

	internal const string SharedImportIssuePrefix = "shared-import:";

	private static readonly TimeSpan RegistrationGrace = TimeSpan.FromSeconds(5);

	private static readonly ILogger _logger = IntegrationLog.For<DelegateIntegration>(IntegrationId);
	private static readonly byte[] _icon = LoadIcon();

	private static readonly IReadOnlyList<VariableDefinition> _templateVariables =
		[DelegateVariables.Declare(VariableNameTemplate.Placeholder("instance"))];

	private readonly DelegateRemoteManager _remotes;
	private readonly SemaphoreSlim _importLock = new(1, 1);

	private IIntegrationConfig? _config;
	private IVariableApi? _variableApi;
	private IVariablePollingInvalidationSignal? _pollingInvalidation;
	private volatile DelegateSharedImports _imports = DelegateSharedImports.Empty;
	private DateTimeOffset _importsBuiltAt = DateTimeOffset.MinValue;
	private volatile bool _disposed;

	public DelegateIntegration()
		: this(new DelegateRemoteManager(() => new DelegateClient(), TimeProvider.System, _logger))
	{
	}

	internal DelegateIntegration(DelegateRemoteManager remotes)
	{
		_remotes = remotes;
		Actions = [new RunRemoteScriptActionDefinition(() => _remotes)];
		_remotes.SharedVariablesChanged += () => _ = RefreshImportsAsync();
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
				new VariableConfiguration(remote.Instance.EntryId.ToString("N"), remote.Instance.MachineName))),
			.. _imports.Imports.Select(import => import.Definition)
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
		_variableApi = context.Variables;

		await _remotes.ReloadAsync(context.Config);
		await RefreshImportsAsync();
		_remotes.StartAll();
		IsInitialized = true;
	}

	public async Task ShutdownAsync()
	{
		IsInitialized = false;
		await _remotes.ShutdownAsync();
	}

	public void UseVariablePollingInvalidation(IVariablePollingInvalidationSignal signal)
		=> _pollingInvalidation = signal;

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		if (_imports.Find(localId) is { } import)
		{
			return ValueTask.FromResult(DelegateSharedImports.Read(import));
		}

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

	public async ValueTask<VariableWriteResult> SetValueAsync(
		string localId,
		object? value,
		CancellationToken cancellationToken = default)
		=> _imports.Find(localId) is { } import
			? await import.Remote.WriteSharedAsync(import.RemoteName,
				DelegateSharedImports.Format(value),
				cancellationToken)
			: VariableWriteResult.NotWritable();

	public async Task<IReadOnlyList<IntegrationIssue>> GetIssuesAsync(CancellationToken cancellationToken = default)
	{
		var issues = _remotes.Issues().ToList();
		var imports = _imports;
		var owned = _variableApi is null
			? null
			: (await _variableApi.GetAllAsync()).Select(v => v.Name).ToHashSet(StringComparer.Ordinal);

		foreach (var remote in _remotes.Remotes.Where(r => r.ImportsSharedVariables))
		{
			var names = imports.Skipped.GetValueOrDefault(remote.Instance.InstanceId, []).ToList();
			if (owned is not null && DateTimeOffset.UtcNow - _importsBuiltAt > RegistrationGrace)
			{
				names.AddRange(imports.Imports
					.Where(i => i.Remote == remote && !owned.Contains(i.Definition.Name!))
					.Select(i => i.RemoteName));
			}

			if (names.Count == 0)
			{
				continue;
			}

			issues.Add(new IntegrationIssue
			{
				Id = SharedImportIssuePrefix + remote.Instance.InstanceId,
				Title = AppStrings.Integrations.Delegation.Issues.SharedImportSkippedTitle(count: names.Count,
					label: remote.Instance.Label),
				Description = AppStrings.Integrations.Delegation.Issues.SharedImportSkippedDescription(
					names: string.Join(", ", names)),
				Severity = IntegrationIssueSeverity.Warning
			});
		}

		return issues;
	}

	internal async Task RefreshImportsAsync()
	{
		if (_disposed)
		{
			return;
		}

		try
		{
			await _importLock.WaitAsync();
		}
		catch (ObjectDisposedException)
		{
			return;
		}

		try
		{
			var imports = DelegateSharedImports.Build(_remotes.Remotes);
			_imports = imports;
			_importsBuiltAt = DateTimeOffset.UtcNow;

			if (_variableApi is { } api)
			{
				var declared = imports.Imports.ToDictionary(i => i.Definition.ResolvedId!, StringComparer.Ordinal);
				foreach (var handle in await api.GetAllAsync())
				{
					var stale = handle.DefinitionId is not { } definitionId
						? false
						: declared.TryGetValue(definitionId, out var import)
							? import.Definition.Type != handle.Type
							: DelegateVariables.KeyOfDefinitionId(definitionId) is null;
					if (stale)
					{
						await api.DeleteAsync(handle.Id);
					}
				}
			}

			_pollingInvalidation?.MarkStale(IntegrationId);
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Macro Deck Delegate could not update the imported shared variables");
		}
		finally
		{
			if (!_disposed)
			{
				_importLock.Release();
			}
		}
	}

	public Task<IssueResolution> ResolveIssueAsync(string issueId, CancellationToken cancellationToken = default)
		=> Task.FromResult(
			issueId.StartsWith(DelegateRemoteManager.CredentialsRejectedIssuePrefix, StringComparison.Ordinal) ||
			issueId.StartsWith(DelegateRemoteManager.UnreachableIssuePrefix, StringComparison.Ordinal) ||
			issueId.StartsWith(DelegateRemoteManager.SelfDelegationIssuePrefix, StringComparison.Ordinal)
				? IssueResolution.Ok(followUp: IssueResolutionFollowUp.StartConfigFlow)
				: IssueResolution.Failed(AppStrings.Integrations.Issues.UnknownIssue()));

	public void Dispose()
	{
		_disposed = true;
		_remotes.Dispose();
		_importLock.Dispose();
	}

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
