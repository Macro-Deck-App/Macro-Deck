using System.Collections.Concurrent;
using MacroDeckHost.Localization;
using MacroDeck.Sdk.Issues;

namespace MacroDeckHost.Application.Integrations;

public static class IntegrationHostIssueIds
{
	public const string Startup = "host.startup";
}

public interface IIntegrationHostIssueStore
{
	void RaiseStartupFailure(string integrationId, string details);

	void RaiseStartupTimeout(string integrationId);

	void Clear(string integrationId);

	IReadOnlyList<IntegrationIssue> IssuesFor(string integrationId);

	bool Has(string integrationId);
}

public sealed class IntegrationHostIssueStore : IIntegrationHostIssueStore
{
	private readonly ConcurrentDictionary<string, IntegrationIssue> _issues = new(StringComparer.Ordinal);
	private readonly IIntegrationIssueBroadcastTrigger _trigger;

	public IntegrationHostIssueStore(IIntegrationIssueBroadcastTrigger trigger)
	{
		_trigger = trigger;
	}

	public void RaiseStartupFailure(string integrationId, string details)
	{
		_issues[integrationId] = new IntegrationIssue
		{
			Id = IntegrationHostIssueIds.Startup,
			Title = AppStrings.Integrations.Issues.StartupFailedTitle(),
			Description = AppStrings.Errors.Integrations.StartupFailed(details: details),
			Severity = IntegrationIssueSeverity.Error,
			ActionLabel = AppStrings.Integrations.Issues.StartupRetry()
		};
		_trigger.RequestRefresh();
	}

	public void RaiseStartupTimeout(string integrationId)
	{
		_issues[integrationId] = new IntegrationIssue
		{
			Id = IntegrationHostIssueIds.Startup,
			Title = AppStrings.Integrations.Issues.StartupFailedTitle(),
			Description = AppStrings.Errors.Integrations.StartupTimedOut(),
			Severity = IntegrationIssueSeverity.Error,
			ActionLabel = AppStrings.Integrations.Issues.StartupRetry()
		};
		_trigger.RequestRefresh();
	}

	public void Clear(string integrationId)
	{
		if (_issues.TryRemove(integrationId, out _))
		{
			_trigger.RequestRefresh();
		}
	}

	public IReadOnlyList<IntegrationIssue> IssuesFor(string integrationId)
		=> _issues.TryGetValue(integrationId, out var issue) ? [issue] : [];

	public bool Has(string integrationId) => _issues.ContainsKey(integrationId);
}
