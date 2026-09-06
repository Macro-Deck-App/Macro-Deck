using System.Text.Json;

namespace MacroDeckHost.Widgets.MusicPlayer;

/// <summary>
/// The stored Music Player widget configuration, as
/// <c>widget-data-music-player-v1.schema.json</c> describes it. Only the keys the view reads are
/// modelled - <c>border</c> and <c>flows</c> stay where they are, painted and run by the tile around
/// this tree rather than by it.
/// </summary>
public sealed record MusicPlayerWidgetData
{
	/// <summary>The cover style that fills the tile with artwork and overlays the text on it.</summary>
	public const string FullCoverStyle = "full";

	/// <summary>The cover style that stacks a square cover above the text.</summary>
	public const string SmallCoverStyle = "small";

	public string? InstanceId { get; init; }

	public string CoverStyle { get; init; } = SmallCoverStyle;

	public bool ShowHeader { get; init; } = true;

	public bool ShowTitle { get; init; } = true;

	public bool ShowArtist { get; init; } = true;

	public bool ShowAlbum { get; init; } = true;

	public bool ShowTimeline { get; init; } = true;

	/// <summary>Whether the tile is filled with artwork rather than stacking a square cover.</summary>
	public bool IsFullCover => string.Equals(CoverStyle, FullCoverStyle, StringComparison.Ordinal);

	/// <summary>Reads the stored JSON string a <c>WidgetEntity</c> carries. Absent, blank or malformed
	/// data parses as an empty object, so a widget saved by an older or newer client still renders on its
	/// defaults rather than failing the session.</summary>
	public static JsonElement ParseData(string? json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return default;
		}

		try
		{
			using var document = JsonDocument.Parse(json);

			return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : default;
		}
		catch (JsonException)
		{
			return default;
		}
	}

	public static MusicPlayerWidgetData Parse(JsonElement data)
	{
		if (data.ValueKind != JsonValueKind.Object)
		{
			return new MusicPlayerWidgetData();
		}

		var coverStyle = ReadString(data, "coverStyle");

		return new MusicPlayerWidgetData
		{
			InstanceId = ReadString(data, "instanceId"),
			// An unknown spelling falls back to the small cover rather than failing the session: the
			// schema leaves the widget savable with keys it does not know, so the view has to be too.
			CoverStyle = string.Equals(coverStyle, FullCoverStyle, StringComparison.Ordinal)
				? FullCoverStyle
				: SmallCoverStyle,
			ShowHeader = ReadBool(data, "showHeader") ?? true,
			ShowTitle = ReadBool(data, "showTitle") ?? true,
			ShowArtist = ReadBool(data, "showArtist") ?? true,
			ShowAlbum = ReadBool(data, "showAlbum") ?? true,
			ShowTimeline = ReadBool(data, "showTimeline") ?? true,
		};
	}

	private static string? ReadString(JsonElement data, string name)
		=> data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;

	private static bool? ReadBool(JsonElement data, string name)
		=> data.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
			? value.GetBoolean()
			: null;
}
