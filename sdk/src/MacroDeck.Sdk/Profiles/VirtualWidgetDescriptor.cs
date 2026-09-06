namespace MacroDeck.Sdk.Profiles;

/// <summary>
/// Read-only description of a widget in a virtual folder. <see cref="Type"/> is a widget type name
/// (e.g. "ActionButton", "MusicPlayer", "Slider"); <see cref="Data"/> is the same JSON payload a
/// stored widget would carry.
/// </summary>
public sealed record VirtualWidgetDescriptor(
	string Id,
	string Type,
	int PositionX,
	int PositionY,
	int Width = 1,
	int Height = 1,
	string? Data = null);
