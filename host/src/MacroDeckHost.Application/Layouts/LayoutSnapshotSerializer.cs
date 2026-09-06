using System.Text.Json;
using MacroDeck.Sdk.Layouts;

namespace MacroDeckHost.Application.Layouts;

/// <summary>The one serializer used to write and read <c>DeviceEntity.LayoutSnapshot</c>.</summary>
public static class LayoutSnapshotSerializer
{
	private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web) { WriteIndented = false };

	public static string Serialize(LayoutDescriptor layout) => JsonSerializer.Serialize(layout, _options);

	/// <summary>A snapshot that fails to deserialize is treated as absent, never as a fault - it may
	/// predate a contract change in a layout a provider has not re-registered since.</summary>
	public static bool TryDeserialize(string? snapshot, out LayoutDescriptor layout)
	{
		if (string.IsNullOrEmpty(snapshot))
		{
			layout = null!;
			return false;
		}

		try
		{
			var deserialized = JsonSerializer.Deserialize<LayoutDescriptor>(snapshot, _options);
			if (deserialized is null)
			{
				layout = null!;
				return false;
			}

			layout = deserialized;
			return true;
		}
		catch (JsonException)
		{
			layout = null!;
			return false;
		}
	}
}
