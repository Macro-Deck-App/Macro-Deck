using MacroDeck.Localization;

namespace MacroDeck.Plugin.Protocol.Capabilities.Issues;

/// <summary>
/// Mirrors the SDK's <c>IntegrationIssue</c>. <see cref="Severity" /> is a string, not the SDK's
/// <c>IntegrationIssueSeverity</c> enum - see <c>ActionParameterDto</c>'s remarks for why every wire
/// DTO in this project follows that rule.
/// </summary>
public sealed record IntegrationIssueDescriptorDto
{
	public required string Id { get; init; }

	public required LocalizedText Title { get; init; }

	public LocalizedText? Description { get; init; }

	/// <summary>One of the SDK's <c>IntegrationIssueSeverity</c> member names: "Info", "Warning", "Error".</summary>
	public required string Severity { get; init; }

	public LocalizedText? ActionLabel { get; init; }
}

/// <summary>Result of the <c>list</c> operation.</summary>
public sealed record IssueListResult
{
	public required IReadOnlyList<IntegrationIssueDescriptorDto> Issues { get; init; }
}

/// <summary>Arguments for the <c>resolve</c> operation.</summary>
public sealed record IssueResolveArguments
{
	public required string IssueId { get; init; }
}

/// <summary>Result of the <c>resolve</c> operation, mirroring the SDK's <c>IssueResolution</c>.
/// <see cref="FollowUp" /> is a string, not the SDK's <c>IssueResolutionFollowUp</c> enum - see
/// <c>ActionParameterDto</c>'s remarks.</summary>
public sealed record IssueResolveResult
{
	public bool Success { get; init; }

	public LocalizedText? Message { get; init; }

	/// <summary>One of the SDK's <c>IssueResolutionFollowUp</c> member names: "None", "StartConfigFlow".</summary>
	public required string FollowUp { get; init; }
}
