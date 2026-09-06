namespace MacroDeckHost.Application.Plugins;

public static class PluginRegistrationOrigins
{
	public const string DeveloperToken = "developer-token";

	public const string Pairing = "pairing";

	public static readonly IReadOnlyList<string> All = [DeveloperToken, Pairing];
}
