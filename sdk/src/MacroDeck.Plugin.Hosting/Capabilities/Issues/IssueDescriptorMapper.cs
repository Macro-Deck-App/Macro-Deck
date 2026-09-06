using MacroDeck.Plugin.Protocol.Capabilities.Issues;
using MacroDeck.Sdk.Issues;
using MacroDeck.Plugin.Hosting.Localization;

namespace MacroDeck.Plugin.Hosting.Capabilities.Issues;

/// <summary>Maps the SDK's issue types to their wire DTOs.</summary>
internal static class IssueDescriptorMapper
{
	public static IntegrationIssueDescriptorDto ToDto(IntegrationIssue issue)
		=> new()
		{
			Id = issue.Id,
			Title = PluginText.ToWire(issue.Title),
			Description = PluginText.ToWireOrNull(issue.Description),
			Severity = issue.Severity.ToString(),
			ActionLabel = PluginText.ToWireOrNull(issue.ActionLabel)
		};

	public static IssueResolveResult ToDto(IssueResolution resolution)
		=> new()
		{
			Success = resolution.Success, Message = PluginText.ToWireOrNull(resolution.Message),
			FollowUp = resolution.FollowUp.ToString()
		};
}
