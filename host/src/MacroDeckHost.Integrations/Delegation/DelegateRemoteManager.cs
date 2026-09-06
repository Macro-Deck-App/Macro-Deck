using MacroDeckHost.Integrations.Delegation.Protocol;
using MacroDeckHost.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Issues;
using Serilog;

namespace MacroDeckHost.Integrations.Delegation;

internal sealed class DelegateRemoteManager : IDisposable
{
	internal const string CredentialsRejectedIssuePrefix = "credentials-rejected:";
	internal const string UnreachableIssuePrefix = "unreachable:";
	internal const string SelfDelegationIssuePrefix = "self-delegation:";
	internal const string ThrottledIssuePrefix = "throttled:";
	internal const string DuplicateIssuePrefix = "duplicate:";

	internal static readonly TimeSpan UnreachableGrace = TimeSpan.FromMinutes(5);

	private readonly Func<IDelegateClient> _clientFactory;
	private readonly TimeProvider _time;
	private readonly ILogger _logger;

	private volatile List<DelegateRemote> _remotes = [];
	private volatile List<StaleEntry> _stale = [];

	public DelegateRemoteManager(Func<IDelegateClient> clientFactory, TimeProvider time, ILogger logger)
	{
		_clientFactory = clientFactory;
		_time = time;
		_logger = logger;
	}

	public IReadOnlyList<DelegateRemote> Remotes => _remotes;

	public async Task ReloadAsync(IIntegrationConfig config, CancellationToken cancellationToken = default)
	{
		await StopAllAsync();

		var entries = await config.GetEntriesAsync(cancellationToken);
		var candidates = new List<Candidate>();
		for (var order = 0; order < entries.Count; order++)
		{
			var candidate = await ReadCandidate(config, entries[order], order, cancellationToken);
			if (candidate is not null)
			{
				candidates.Add(candidate);
			}
		}

		var (winners, stale) = Reconcile(candidates);
		_remotes = AssignVariableKeys(winners).Select(candidate => Build(candidate, config)).ToList();
		_stale = stale;

		_logger.Information("Macro Deck Delegate configured with {Count} remote(s)", _remotes.Count);
	}

	public void StartAll()
	{
		foreach (var remote in _remotes)
		{
			remote.Start();
		}
	}

	public DelegateResolution Resolve(string? instanceId)
	{
		var remotes = _remotes;

		if (string.IsNullOrEmpty(instanceId))
		{
			return remotes.Count switch
			{
				0 => DelegateResolution.NotConfigured,
				1 => DelegateResolution.Found(remotes[0]),
				_ => DelegateResolution.Ambiguous
			};
		}

		var match = remotes.FirstOrDefault(r =>
			string.Equals(r.Instance.InstanceId, instanceId, StringComparison.Ordinal));
		return match is null ? DelegateResolution.NotFound : DelegateResolution.Found(match);
	}

	public IReadOnlyList<ActionParameterOption> InstanceOptions()
		=> _remotes.Select(r => new ActionParameterOption { Value = r.Instance.InstanceId, Label = r.Instance.Label })
			.ToList();

	public IReadOnlyList<IntegrationIssue> Issues()
	{
		var issues = new List<IntegrationIssue>();
		var now = _time.GetUtcNow();

		foreach (var remote in _remotes)
		{
			if (remote.CredentialsRejected)
			{
				issues.Add(new IntegrationIssue
				{
					Id = CredentialsRejectedIssuePrefix + remote.Instance.InstanceId,
					Title = AppStrings.Integrations.Delegation.Issues.CredentialsRejectedTitle(
						label: remote.Instance.Label),
					Description
						= AppStrings.Integrations.Delegation.Issues.CredentialsRejectedDescription(
							label: remote.Instance.Label),
					Severity = IntegrationIssueSeverity.Error,
					ActionLabel = AppStrings.Integrations.Delegation.Issues.SignInAgainAction()
				});
			}
			else if (remote.UnreachableSince is { } since && now - since >= UnreachableGrace)
			{
				issues.Add(new IntegrationIssue
				{
					Id = UnreachableIssuePrefix + remote.Instance.InstanceId,
					Title = AppStrings.Integrations.Delegation.Issues.UnreachableTitle(label: remote.Instance.Label),
					Description
						= AppStrings.Integrations.Delegation.Issues
							.UnreachableDescription(label: remote.Instance.Label),
					Severity = IntegrationIssueSeverity.Warning,
					ActionLabel = AppStrings.Integrations.Delegation.Issues.OpenSetupAction()
				});
			}

			if (remote.SelfDelegationDetected)
			{
				issues.Add(new IntegrationIssue
				{
					Id = SelfDelegationIssuePrefix + remote.Instance.InstanceId,
					Title = AppStrings.Integrations.Delegation.Issues.SelfDelegationTitle(label: remote.Instance.Label),
					Description = AppStrings.Integrations.Delegation.Issues.SelfDelegationDescription(),
					Severity = IntegrationIssueSeverity.Error,
					ActionLabel = AppStrings.Integrations.Delegation.Issues.OpenSetupAction()
				});
			}

			if (remote.ThrottledUntil is { } until && now < until)
			{
				issues.Add(new IntegrationIssue
				{
					Id = ThrottledIssuePrefix + remote.Instance.InstanceId,
					Title = AppStrings.Integrations.Delegation.Issues.ThrottledTitle(label: remote.Instance.Label),
					Description = AppStrings.Integrations.Delegation.Issues.ThrottledDescription(),
					Severity = IntegrationIssueSeverity.Warning
				});
			}
		}

		foreach (var stale in _stale)
		{
			issues.Add(new IntegrationIssue
			{
				Id = DuplicateIssuePrefix + stale.InstanceId,
				Title = AppStrings.Integrations.Delegation.Issues.DuplicateTitle(),
				Description = AppStrings.Integrations.Delegation.Issues.DuplicateDescription(title: stale.Title),
				Severity = IntegrationIssueSeverity.Warning
			});
		}

		return issues;
	}

	public async Task ShutdownAsync() => await StopAllAsync();

	public void Dispose()
	{
		foreach (var remote in _remotes)
		{
			remote.Dispose();
		}

		_remotes = [];
		_stale = [];
	}

	private static (List<Candidate> Winners, List<StaleEntry> Stale) Reconcile(List<Candidate> candidates)
	{
		var winners = new List<Candidate>();
		var stale = new List<StaleEntry>();

		foreach (var group in candidates.GroupBy(c => c.InstanceId, StringComparer.Ordinal))
		{
			var ordered = group.OrderByDescending(c => c.ConfiguredAt).ThenByDescending(c => c.Order).ToList();
			winners.Add(ordered[0]);
			stale.AddRange(ordered.Skip(1).Select(c => new StaleEntry(c.InstanceId, c.Title)));
		}

		return (winners, stale);
	}

	private static List<Candidate> AssignVariableKeys(List<Candidate> winners)
	{
		var used = new HashSet<string>(StringComparer.Ordinal);
		var result = new List<Candidate>(winners.Count);

		foreach (var candidate in winners.OrderBy(c => c.InstanceId, StringComparer.Ordinal))
		{
			var key = candidate.InstanceKey;
			var suffix = 2;
			while (!used.Add(key))
			{
				key = $"{candidate.InstanceKey}_{suffix}";
				suffix++;
			}

			result.Add(candidate with { VariableKey = key });
		}

		return result;
	}

	private async Task<Candidate?> ReadCandidate(
		IIntegrationConfig config,
		ConfigEntrySnapshot entry,
		int order,
		CancellationToken cancellationToken)
	{
		var baseUrlRaw = await config.GetStringAsync(entry.Id, DelegateConfigKeys.BaseUrl, cancellationToken);
		var username = await config.GetStringAsync(entry.Id, DelegateConfigKeys.Username, cancellationToken);
		var password = await config.GetSecretAsync(entry.Id, DelegateConfigKeys.Password, cancellationToken);
		var instanceId = await config.GetStringAsync(entry.Id, DelegateConfigKeys.InstanceId, cancellationToken);
		var machineName = await config.GetStringAsync(entry.Id, DelegateConfigKeys.MachineName, cancellationToken);
		var instanceKey = await config.GetStringAsync(entry.Id, DelegateConfigKeys.InstanceKey, cancellationToken);

		var baseUrl = DelegateEndpoint.TryBuild(baseUrlRaw);

		if (baseUrl is null ||
			string.IsNullOrEmpty(username) ||
			string.IsNullOrEmpty(password) ||
			string.IsNullOrEmpty(instanceId) ||
			string.IsNullOrEmpty(machineName) ||
			string.IsNullOrEmpty(instanceKey))
		{
			// One broken entry must not take the working ones with it, so it is skipped, not thrown on.
			_logger.Warning("Macro Deck Delegate config entry {EntryId} is incomplete; skipping", entry.Id);
			return null;
		}

		var configuredAt = await config.GetStringAsync(entry.Id, DelegateConfigKeys.ConfiguredAt, cancellationToken);
		var storedScripts = await config.GetStringAsync(entry.Id, DelegateConfigKeys.RemoteScripts, cancellationToken);

		return new Candidate(entry.Id,
			entry.Title,
			instanceId,
			machineName,
			instanceKey,
			baseUrl,
			username,
			password,
			DelegateRemote.ParseTimestamp(configuredAt) ?? DateTimeOffset.MinValue,
			DelegateRemote.ParseScripts(storedScripts),
			order);
	}

	private DelegateRemote Build(Candidate candidate, IIntegrationConfig config)
	{
		var instance = new DelegateInstance(candidate.EntryId,
			candidate.InstanceId,
			candidate.MachineName,
			candidate.VariableKey,
			candidate.BaseUrl,
			candidate.Username,
			candidate.ConfiguredAt);

		return new DelegateRemote(instance,
			candidate.Password,
			_clientFactory(),
			config,
			_time,
			_logger,
			candidate.InitialScripts);
	}

	private async Task StopAllAsync()
	{
		var remotes = _remotes;
		_remotes = [];
		_stale = [];
		await Task.WhenAll(remotes.Select(remote => remote.StopAsync()));

		foreach (var remote in remotes)
		{
			remote.Dispose();
		}
	}

	private sealed record Candidate(
		Guid EntryId,
		string Title,
		string InstanceId,
		string MachineName,
		string InstanceKey,
		Uri BaseUrl,
		string Username,
		string Password,
		DateTimeOffset ConfiguredAt,
		IReadOnlyDictionary<string, DelegateScriptSummary> InitialScripts,
		int Order)
	{
		public string VariableKey { get; init; } = InstanceKey;
	}

	private sealed record StaleEntry(string InstanceId, string Title);
}

internal enum DelegateResolutionKind
{
	Found,
	NotConfigured,
	Ambiguous,
	NotFound
}

internal readonly record struct DelegateResolution(DelegateResolutionKind Kind, DelegateRemote? Remote)
{
	public static DelegateResolution NotConfigured { get; } = new(DelegateResolutionKind.NotConfigured, null);

	public static DelegateResolution Ambiguous { get; } = new(DelegateResolutionKind.Ambiguous, null);

	public static DelegateResolution NotFound { get; } = new(DelegateResolutionKind.NotFound, null);

	public static DelegateResolution Found(DelegateRemote remote) => new(DelegateResolutionKind.Found, remote);
}
