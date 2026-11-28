namespace MacroDeck.Sdk.Layouts;

/// <summary>
/// What a surface can actually render, so the host and editor can avoid offering something the hardware
/// will drop. Every flag defaults to <c>false</c>: a provider that declares nothing is treated as able to
/// do nothing, which fails safe rather than promising capabilities the device does not have.
/// <see cref="ScreenSavers" /> is the exception: it is nullable, and leaving it unset keeps the host's
/// default.
/// </summary>
public sealed record LayoutVisualCapabilities
{
	/// <summary>Everything a full-colour software client can do. The built-in client layout uses this.</summary>
	public static readonly LayoutVisualCapabilities Full = new()
	{
		StaticIcons = true,
		AnimatedIcons = true,
		Borders = true,
		BackgroundColors = true,
		TextLabels = true,
		Transparency = true,
		WidgetSpacing = true,
		CornerRadius = true,
		CustomFolderViews = true,
		ScreenSavers = true
	};

	public bool StaticIcons { get; init; }

	/// <summary>Whether an animated icon plays. A device without it is sent the first frame instead, and a
	/// key image the host renders for it shows that first frame too.</summary>
	public bool AnimatedIcons { get; init; }

	/// <summary>Whether widget borders are shown. Without it the host draws no border ring on the key
	/// images it renders.</summary>
	public bool Borders { get; init; }

	public bool BackgroundColors { get; init; }

	public bool TextLabels { get; init; }

	/// <summary>Whether alpha is honoured. Without it a transparent icon composites onto the background.</summary>
	public bool Transparency { get; init; }

	/// <summary>
	/// Whether the profile's widget spacing changes anything here. False for hardware whose gaps are
	/// physical: the host still sends the value, the surface simply renders the same either way, so an
	/// editor should say the setting has no effect rather than pretend the device fixes it.
	/// </summary>
	public bool WidgetSpacing { get; init; }

	/// <summary>Whether the profile's widget corner radius changes anything here. See
	/// <see cref="WidgetSpacing" /> for why this is a capability and not a fixed value. A key image grid
	/// that fixes its radius with <see cref="LayoutKeyImageFormat.CornerRadius" /> overrides it.</summary>
	public bool CornerRadius { get; init; }

	/// <summary>
	/// Whether this surface can render a folder view - a Macro Deck UI tree standing in for the widget
	/// grid. Unlike the flags above this is not about fidelity: a surface the host rasterises a key grid
	/// for cannot show an arbitrary tree at all, so a folder whose view it cannot render would simply be
	/// blank there. Macro Deck therefore does not offer a folder view for a profile whose claiming device
	/// says no. The key images the host renders for a <see cref="LayoutGrid.KeyImages" /> grid show only
	/// the widget grid, so such a layout declares this false.
	/// </summary>
	public bool CustomFolderViews { get; init; }

	/// <summary>
	/// Whether the device can take part in screen savers. <c>false</c> says it cannot: Macro Deck refuses to
	/// turn a screen saver on for the device or to show one on it, and keeps whatever screen saver settings
	/// the device already has stored. <c>null</c>, the default, declares nothing and keeps the host's
	/// behaviour, and so does <c>true</c>: it is not a promise that the device will show a screen saver.
	/// A screen saver concerns the whole device. The host reads the value of the layout's primary grid region
	/// (<see cref="LayoutDescriptor.PrimaryGrid" />) and, where that is <c>null</c>, the layout-wide value
	/// in <see cref="LayoutCapabilities.Visuals" />. A layout with several grid regions has no primary grid,
	/// so declare it layout-wide there.
	/// </summary>
	public bool? ScreenSavers { get; init; }

	/// <summary>
	/// The practical refresh ceiling in updates per second, where the hardware has one. Advisory: it lets
	/// the host avoid pushing faster than a device can absorb, and is not a rate limit the host enforces
	/// on surfaces. Key images the host renders for a <see cref="LayoutGrid.KeyImages" /> grid are capped
	/// at it, and at 30 per second when it is unset.
	/// </summary>
	public int? MaxUpdatesPerSecond { get; init; }
}
