using System.Globalization;
using System.Text.Json;

namespace MacroDeckHost.Integrations.Obs;

internal static class ObsConfigurationMetadata
{
	public const string SchemaKey = "obsConfigurationSchema";
	public const string SchemaVersion = "multiple-v1";
	public const string VariableIdentityKey = "obsVariableIdentity";

	public static string SerializeIdentity(ObsConfigurationIdentity identity)
		=> JsonSerializer.Serialize(identity);

	public static bool TryParseIdentity(string? json, out ObsConfigurationIdentity identity)
	{
		try
		{
			identity = JsonSerializer.Deserialize<ObsConfigurationIdentity>(json ?? string.Empty)!;
			return identity is { SourceTitle.Length: > 0, Key.Length: > 0 };
		}
		catch (JsonException)
		{
			identity = null!;
			return false;
		}
	}
}

internal sealed record ObsConfigurationIdentity(string SourceTitle, string Key);

internal sealed record ObsConfigurationSettings(string Host, int Port, string? Password)
{
	public string Url => $"ws://{Host}:{Port.ToString(CultureInfo.InvariantCulture)}";
}

internal sealed record ObsRuntime(
	Guid Id,
	string Title,
	ObsConfigurationIdentity Identity,
	ObsConfigurationSettings Settings,
	ObsConnection Connection);
