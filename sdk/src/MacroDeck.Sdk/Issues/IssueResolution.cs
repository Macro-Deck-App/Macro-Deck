using MacroDeck.Localization;

namespace MacroDeck.Sdk.Issues;

/// <summary>What the UI should do after an issue's resolve action ran.</summary>
public enum IssueResolutionFollowUp
{
	/// <summary>Nothing further; just refresh the issue list.</summary>
	None,

	/// <summary>Open the integration's config flow (e.g. to re-enter credentials).</summary>
	StartConfigFlow
}

/// <summary>Result of attempting to resolve an integration issue.</summary>
public sealed class IssueResolution
{
	public bool Success { get; init; }

	/// <summary>Optional message to show the user (e.g. "Check System Settings to grant access").</summary>
	public LocalizedText Message { get; init; }

	public IssueResolutionFollowUp FollowUp { get; init; } = IssueResolutionFollowUp.None;

	public static IssueResolution Ok(LocalizedText message = default,
		IssueResolutionFollowUp followUp = IssueResolutionFollowUp.None)
		=> new() { Success = true, Message = message, FollowUp = followUp };

	public static IssueResolution Failed(LocalizedText message = default)
		=> new() { Success = false, Message = message };
}
