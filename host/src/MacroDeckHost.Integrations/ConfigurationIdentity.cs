using System.Text.Json;

namespace MacroDeckHost.Integrations;

internal sealed record ConfigurationIdentity(string SourceTitle, string Key);

internal static class ConfigurationIdentityJson
{
	public static string Serialize(ConfigurationIdentity identity)
	{
		return JsonSerializer.Serialize(identity);
	}

	public static bool TryParse(string? json, out ConfigurationIdentity identity)
	{
		try
		{
			identity = JsonSerializer.Deserialize<ConfigurationIdentity>(json ?? string.Empty)!;
			return identity is { SourceTitle.Length: > 0, Key.Length: > 0 };
		}
		catch (JsonException)
		{
			identity = null!;
			return false;
		}
	}
}
