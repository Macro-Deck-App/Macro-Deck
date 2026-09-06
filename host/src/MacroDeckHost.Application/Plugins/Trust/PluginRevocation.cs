namespace MacroDeckHost.Application.Plugins.Trust;

public enum PluginRevocationStatus
{
	NotRevoked,
	Revoked,
	Unavailable
}

public sealed record PluginRevocationResult(PluginRevocationStatus Status, string? Message);

public interface IPluginRevocationSource
{
	Task<PluginRevocationResult> CheckAsync(string certificateId, CancellationToken cancellationToken = default);
}
