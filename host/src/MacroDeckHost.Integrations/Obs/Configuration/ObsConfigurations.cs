using System.Globalization;
using MacroDeckHost.Integrations.Obs.Connections;

namespace MacroDeckHost.Integrations.Obs.Configuration;

internal static class ObsConfigurationMetadata
{
	public const string SchemaKey = "obsConfigurationSchema";
	public const string SchemaVersion = "multiple-v1";
	public const string VariableIdentityKey = "obsVariableIdentity";
}

internal sealed record ObsConfigurationSettings(string Host, int Port, string? Password)
{
	public string Url => $"ws://{Host}:{Port.ToString(CultureInfo.InvariantCulture)}";
}

internal sealed record ObsRuntime(
	Guid Id,
	string Title,
	ConfigurationIdentity Identity,
	ObsConfigurationSettings Settings,
	ObsConnection Connection);
