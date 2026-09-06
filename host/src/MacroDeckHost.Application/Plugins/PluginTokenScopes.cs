namespace MacroDeckHost.Application.Plugins;

public static class PluginTokenScopes
{
	public const string Enroll = "plugin:enroll";

	public static readonly IReadOnlyList<string> All = [Enroll];
}
