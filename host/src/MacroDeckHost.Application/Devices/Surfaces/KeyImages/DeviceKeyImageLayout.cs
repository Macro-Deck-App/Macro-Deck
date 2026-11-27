using MacroDeck.Sdk.Layouts;

namespace MacroDeckHost.Application.Devices.Surfaces.KeyImages;

public sealed record DeviceKeyImageLayout
{
	public const int DefaultMaxUpdatesPerSecond = 30;

	public required int Rows { get; init; }

	public required int Columns { get; init; }

	public required LayoutKeySize KeySize { get; init; }

	public required LayoutKeyImageFormat Format { get; init; }

	public bool Borders { get; init; }

	public bool AnimatedIcons { get; init; }

	public bool CornerRadius { get; init; }

	public bool WidgetSpacing { get; init; }

	public int MaxUpdatesPerSecond { get; init; } = DefaultMaxUpdatesPerSecond;

	public static DeviceKeyImageLayout? Of(LayoutDescriptor layout, out string? problem)
	{
		problem = null;
		if (layout.PrimaryGrid is not { Grid: { KeyImages: { } format } grid } region)
		{
			return null;
		}

		if (grid.KeySize is not { Width: > 0, Height: > 0 } keySize)
		{
			problem = "the grid declares no key size";
			return null;
		}

		if (format.Rotation is not (0 or 90 or 180 or 270))
		{
			problem = $"rotation {format.Rotation} is not 0, 90, 180 or 270";
			return null;
		}

		if (!KeyImageEncodings.IsKnown(format.Encoding))
		{
			problem = $"encoding '{format.Encoding}' is unknown";
			return null;
		}

		if (grid.Rows <= 0 || grid.Columns <= 0)
		{
			problem = "the grid has no cells";
			return null;
		}

		// A layout that declares no visuals supports none of them, as LayoutCapabilities documents.
		var visuals = region.Visuals ?? layout.Capabilities?.Visuals ?? new LayoutVisualCapabilities();
		return new DeviceKeyImageLayout
		{
			Rows = grid.Rows,
			Columns = grid.Columns,
			KeySize = keySize,
			Format = format,
			Borders = visuals.Borders,
			AnimatedIcons = visuals.AnimatedIcons,
			CornerRadius = visuals.CornerRadius,
			WidgetSpacing = visuals.WidgetSpacing,
			MaxUpdatesPerSecond = visuals.MaxUpdatesPerSecond is { } rate && rate > 0
				? rate
				: DefaultMaxUpdatesPerSecond
		};
	}
}
