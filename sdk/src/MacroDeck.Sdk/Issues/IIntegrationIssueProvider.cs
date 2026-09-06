namespace MacroDeck.Sdk.Issues;

/// <summary>
/// Capability interface for integrations that can report issues (missing permissions, invalid
/// credentials, disconnected services, …) and offer a resolution action. Implementations should make
/// <see cref="GetIssuesAsync"/> cheap and non-blocking (return cached state) since it is polled to
/// render badges; do the actual work in <see cref="ResolveIssueAsync"/>.
/// </summary>
public interface IIntegrationIssueProvider
{
	Task<IReadOnlyList<IntegrationIssue>> GetIssuesAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Runs the resolution for the given issue id (request a permission, trigger a reconnect, …) and
	/// tells the UI what to do next.
	/// </summary>
	Task<IssueResolution> ResolveIssueAsync(string issueId, CancellationToken cancellationToken = default);
}
