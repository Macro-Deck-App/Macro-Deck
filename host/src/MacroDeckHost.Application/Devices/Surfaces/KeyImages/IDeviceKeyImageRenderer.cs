using System.Text.Json;
using MacroDeck.Sdk.Layouts;

namespace MacroDeckHost.Application.Devices.Surfaces.KeyImages;

public interface IDeviceKeyImageRenderer
{
	event EventHandler? FontsChanged;

	TimeSpan PressMinVisible { get; }

	DeviceKeyImageRenderResult Render(DeviceKeyImageRenderRequest request);
}

public enum DeviceKeyCellKind
{
	Background,

	Empty,

	Widget
}

public sealed record DeviceKeyImageAppearance(string ThemeMode, string? AccentColor, string? FontFamily);

public sealed record DeviceKeyImageResource(ReadOnlyMemory<byte> Content, string MediaType, string? ContentHash = null);

public sealed record DeviceKeyImageResourceRequest(string ResourceId, int? DisplayPx);

public interface IDeviceKeyImageResourceLookup
{
	DeviceKeyImageResource? TryGet(string resourceId, int? displayPx);
}

public sealed record DeviceKeyImageRenderRequest
{
	public required LayoutKeySize KeySize { get; init; }

	public required LayoutKeyImageFormat Format { get; init; }

	public required DeviceKeyCellKind Kind { get; init; }

	public int Columns { get; init; } = 1;

	public int Rows { get; init; } = 1;

	public string? BackgroundColor { get; init; }

	public double CornerRadius { get; init; }

	public double Spacing { get; init; }

	public bool EmptyCellVisible { get; init; } = true;

	public bool Borders { get; init; }

	public bool AnimatedIcons { get; init; }

	public bool Pressed { get; init; }

	public required DeviceKeyImageAppearance Appearance { get; init; }

	public required string Culture { get; init; }

	public required TimeZoneInfo TimeZone { get; init; }

	public required DateTimeOffset Now { get; init; }

	public string? WidgetType { get; init; }

	public JsonElement? WidgetData { get; init; }

	public JsonElement? Tree { get; init; }

	public IDeviceKeyImageResourceLookup? Resources { get; init; }
}

public sealed record DeviceKeyImagePicture(
	int RowOffset,
	int ColumnOffset,
	ReadOnlyMemory<byte> Content,
	string ContentType);

public sealed record DeviceKeyImageRenderResult(
	IReadOnlyList<DeviceKeyImagePicture> Keys,
	DateTimeOffset? NextChangeAt,
	IReadOnlyList<DeviceKeyImageResourceRequest> MissingResources);
