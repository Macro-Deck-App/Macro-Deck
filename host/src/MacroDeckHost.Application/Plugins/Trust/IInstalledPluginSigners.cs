using MacroDeckHost.Application.Plugins.Runtime;

namespace MacroDeckHost.Application.Plugins.Trust;

public sealed record PluginSigners(string CertificateId, string? IssuerCertificateId);

public interface IInstalledPluginSigners
{
	PluginSigners? Read(InstalledPluginVersion version);
}
