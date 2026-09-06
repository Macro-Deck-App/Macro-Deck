namespace MacroDeck.Sdk.Devices;

/// <summary>
/// A widget's resolved appearance as it should render on a device. Field names mirror
/// <see cref="MacroDeck.Sdk.Widgets.WidgetAppearancePatch" /> where they overlap, so the two read
/// consistently; unlike a patch, every value here is already resolved rather than a partial change.
/// </summary>
public sealed record DeviceSurfaceAppearance
{
	/// <summary>The widget's label, already resolved - variables and templates expanded - and localized
	/// in the host's active language. A provider renders it as-is.</summary>
	public string? Label { get; init; }

	public string? LabelColor { get; init; }

	public string? BackgroundColor { get; init; }

	/// <summary>
	/// An icon-pack icon's bare id, and nothing else - never a provider reference, never a synthesised
	/// id. <see cref="IconId" /> carries no marker saying so because none has ever been needed: every
	/// value this field has ever held is a GUID, so a provider may parse it as one. When the currently
	/// rendered icon instead comes from an action icon provider, this is null and
	/// <see cref="HasProviderIcon" /> is true - widening this field to carry anything else would be a
	/// silent contract break for every provider already relying on that assumption.
	/// </summary>
	public string? IconId { get; init; }

	/// <summary>
	/// The content identity of the currently rendered icon: it changes whenever the image is replaced,
	/// while <see cref="IconId" /> (or the lack of one) stays the same. A provider that caches icon bytes
	/// must key its cache on this as well as on <see cref="IconId" />, and re-fetch when it changes -
	/// re-rendering an icon under the same id is otherwise indistinguishable from no change at all. Set
	/// exactly when there is an image to identify - alongside <see cref="IconId" /> for an icon-pack icon,
	/// alongside <see cref="HasProviderIcon" /> for a provider-owned one, and null for neither - so either
	/// kind of caching provider has exactly one key to watch. Not the same string as
	/// <see cref="DeviceIconImage.ETag" />, which additionally identifies the requested size.
	/// </summary>
	public string? IconVersion { get; init; }

	/// <summary>
	/// True when the currently rendered icon comes from an action icon provider rather than the icon
	/// pack, in which case <see cref="IconId" /> is null - see its remarks. A provider-aware plugin fetches
	/// the bytes through the <c>devices</c> capability's <c>widget-icon</c> operation
	/// (<see cref="IDeviceSession.GetWidgetIconAsync" /> in-process), addressed by the owning widget's own
	/// id rather than by an icon id. A plugin built against an older SDK does not know this field exists
	/// and renders label and colour only, exactly as it would for an icon-less widget.
	/// </summary>
	public bool HasProviderIcon { get; init; }

	public string? IconFit { get; init; }

	public double? IconZoom { get; init; }

	public double? IconOffsetX { get; init; }

	public double? IconOffsetY { get; init; }

	public int? FontSize { get; init; }

	public string? TextAlign { get; init; }

	public string? LabelPosition { get; init; }

	/// <summary>Forward-compatible escape hatch for appearance data not yet promoted to a named property.</summary>
	public IReadOnlyDictionary<string, string> Extra { get; init; }
		= new Dictionary<string, string>(StringComparer.Ordinal);
}
