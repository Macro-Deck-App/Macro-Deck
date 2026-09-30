using MacroDeck.Sdk.Identity;
using MacroDeck.Sdk.Issues;
using Serilog;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Integrations;

public interface IIntegrationIssueService
{
	Task<IReadOnlyList<IntegrationIssue>> GetIssuesAsync(string integrationId,
		CancellationToken cancellationToken = default);

	Task<IssueResolution?> ResolveAsync(string integrationId,
		string issueId,
		CancellationToken cancellationToken = default);
}

public sealed class IntegrationIssueService : IIntegrationIssueService
{
	private readonly IIntegrationRegistry _registry;
	private readonly IIntegrationHostIssueStore _hostIssueStore;
	private readonly IIntegrationLifecycle _lifecycle;
	private readonly ILogger _logger = Log.ForContext<IntegrationIssueService>();

	public IntegrationIssueService(
		IIntegrationRegistry registry,
		IIntegrationHostIssueStore hostIssueStore,
		IIntegrationLifecycle lifecycle)
	{
		_registry = registry;
		_hostIssueStore = hostIssueStore;
		_lifecycle = lifecycle;
	}

	public async Task<IReadOnlyList<IntegrationIssue>> GetIssuesAsync(
		string integrationId,
		CancellationToken cancellationToken = default)
	{
		if (!TryGetIntegration(integrationId, out var integration))
		{
			return [];
		}

		var hostIssues = _hostIssueStore.IssuesFor(integrationId);

		if (integration is not IIntegrationIssueProvider provider)
		{
			return hostIssues;
		}

		try
		{
			var issues = await provider.GetIssuesAsync(cancellationToken);
			var hostIssueIds = hostIssues.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
			var providerIssues = issues.Where(issue =>
				IsUsableIssueId(integrationId, issue.Id) && !hostIssueIds.Contains(issue.Id));

			return [.. hostIssues, .. providerIssues];
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Failed to get issues for integration {IntegrationId}", integrationId);
			return hostIssues;
		}
	}

	public async Task<IssueResolution?> ResolveAsync(
		string integrationId,
		string issueId,
		CancellationToken cancellationToken = default)
	{
		if (!TryGetIntegration(integrationId, out var integration))
		{
			return null;
		}

		if (issueId == IntegrationHostIssueIds.Startup)
		{
			await _lifecycle.ReinitializeAsync(integrationId, cancellationToken);
			return _hostIssueStore.Has(integrationId)
				? IssueResolution.Failed()
				: IssueResolution.Ok();
		}

		if (integration is not IIntegrationIssueProvider provider || !IsUsableIssueId(integrationId, issueId))
		{
			return null;
		}

		return await provider.ResolveIssueAsync(issueId, cancellationToken);
	}

	private bool IsUsableIssueId(string integrationId, string issueId)
	{
		if (QualifiedId.TryCreate(integrationId, issueId, LocalIdKind.Resource, out _))
		{
			return true;
		}

		_logger.Warning("Ignoring issue '{IssueId}' from integration {IntegrationId}: not a usable id",
			issueId,
			integrationId);
		return false;
	}

	private bool TryGetIntegration(string integrationId, out MacroDeck.Sdk.IIntegration integration)
	{
		integration = null!;
		var candidate = _registry.Integrations.FirstOrDefault(i => i.Id == integrationId);
		if (candidate is null || !_registry.IsEnabled(integrationId))
		{
			return false;
		}

		integration = candidate;
		return true;
	}
}
