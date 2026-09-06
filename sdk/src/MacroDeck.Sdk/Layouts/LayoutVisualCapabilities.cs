namespace MacroDeck.Sdk.Layouts;

/// <summary>
/// What a surface can actually render, so the host and editor can avoid offering something the hardware
/// will drop. Every flag defaults to <c>false</c>: a provider that declares nothing is treated as able to
/// do nothing, which fails safe rather than promising capabilities the device does not have.
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
		CustomFolderViews = true
	};

	public bool StaticIcons { get; init; }

	/// <summary>Whether an animated icon plays. A device without it is sent the first frame instead.</summary>
	public bool AnimatedIcons { get; init; }

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
	/// <see cref="WidgetSpacing" /> for why this is a capability and not a fixed value.</summary>
	public bool CornerRadius { get; init; }

	/// <summary>
	/// Whether this surface can render a folder view - a Macro Deck UI tree standing in for the widget
	/// grid. Unlike the flags above this is not about fidelity: a surface the host rasterises a key grid
	/// for cannot show an arbitrary tree at all, so a folder whose view it cannot render would simply be
	/// blank there. Macro Deck therefore does not offer a folder view for a profile whose claiming device
	/// says no.
	/// </summary>
	public bool CustomFolderViews { get; init; }

	/// <summary>
	/// The practical refresh ceiling in updates per second, where the hardware has one. Advisory: it lets
	/// the host avoid pushing faster than a device can absorb, and is not a rate limit the host enforces.
	/// </summary>
	public int? MaxUpdatesPerSecond { get; init; }
}
