namespace MacroDeckHost.Application.Plugins.Pairing;

public sealed class PluginPairingOptions
{
	public static readonly PluginPairingOptions Default = new();

	public TimeSpan RequestLifetime { get; init; } = TimeSpan.FromMinutes(5);

	public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);

	public int MaxPendingRequests { get; init; } = 5;
}
