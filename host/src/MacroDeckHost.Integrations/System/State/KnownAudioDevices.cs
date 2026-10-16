using System.Text.Json;

namespace MacroDeckHost.Integrations.System.State;

public sealed record KnownAudioDevice(string Flow, string DeviceId, string Key, string Slug, string Name);

internal static class KnownAudioDevices
{
	public const string StorageKey = "system-audio-devices.json";

	// The same shape the host wrote to system-audio-devices.json, so an adopted file keeps loading.
	private static readonly JsonSerializerOptions _options = new()
	{
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = true,
		AllowTrailingCommas = true
	};

	public static string Serialize(IReadOnlyList<KnownAudioDevice> devices)
	{
		return JsonSerializer.Serialize(devices, _options);
	}

	public static IReadOnlyList<KnownAudioDevice>? Deserialize(string content)
	{
		try
		{
			return JsonSerializer.Deserialize<List<KnownAudioDevice>>(content, _options);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	public static bool IsValid(string content)
	{
		return Deserialize(content) is not null;
	}
}
