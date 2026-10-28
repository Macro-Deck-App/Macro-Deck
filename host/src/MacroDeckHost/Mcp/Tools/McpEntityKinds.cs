namespace MacroDeckHost.Mcp.Tools;

public static class McpEntityKinds
{
	public const string Profiles = "profiles";
	public const string Folders = "folders";
	public const string Widgets = "widgets";
	public const string Automations = "automations";
	public const string Scripts = "scripts";
	public const string Variables = "variables";

	public static readonly IReadOnlyList<string> All = [Profiles, Folders, Widgets, Automations, Scripts, Variables];

	public static string Normalize(string? entity)
	{
		var normalized = entity?.Trim().ToLowerInvariant() ?? string.Empty;
		if (!All.Contains(normalized))
		{
			throw McpToolErrors.Create(McpToolErrors.InvalidArgument,
				$"entity must be one of: {string.Join(", ", All)}.");
		}

		return normalized;
	}
}
