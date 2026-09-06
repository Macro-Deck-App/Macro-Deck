using MacroDeck.Plugin.Protocol.Capabilities.Issues;
using MacroDeck.Sdk.Issues;

namespace MacroDeckHost.Application.Plugins.Capabilities.Mapping;

public static class IssueMapper
{
	public static IntegrationIssue ToDomain(IntegrationIssueDescriptorDto dto)
	{
		if (!Enum.TryParse<IntegrationIssueSeverity>(dto.Severity, ignoreCase: false, out var severity))
		{
			throw new InvalidOperationException($"Unknown integration issue severity '{dto.Severity}'.");
		}

		return new IntegrationIssue
		{
			Id = dto.Id, Title = dto.Title, Description = dto.Description ?? default, Severity = severity,
			ActionLabel = dto.ActionLabel ?? default
		};
	}

	public static IssueResolution ToDomain(IssueResolveResult dto)
	{
		if (!Enum.TryParse<IssueResolutionFollowUp>(dto.FollowUp, ignoreCase: false, out var followUp))
		{
			throw new InvalidOperationException($"Unknown issue resolution follow-up '{dto.FollowUp}'.");
		}

		return new IssueResolution { Success = dto.Success, Message = dto.Message ?? default, FollowUp = followUp };
	}
}
