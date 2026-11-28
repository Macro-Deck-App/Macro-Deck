namespace MacroDeck.Sdk.Layouts;

/// <summary>
/// How the host delivers the pictures it renders for each key of a grid - see
/// <see cref="LayoutGrid.KeyImages" />. Every picture is <see cref="LayoutGrid.KeySize" /> pixels as
/// delivered: the host renders the key, mirrors it when <see cref="MirrorHorizontal" /> is set, then
/// rotates it clockwise by <see cref="Rotation" />, so for 90 and 270 it renders the key at the swapped
/// size. A format the host cannot honour (no <see cref="LayoutGrid.KeySize" />, a rotation other than
/// 0, 90, 180 or 270, an encoding it does not know, or a negative <see cref="CornerRadius" />) means no
/// key images for the layout, never a failed registration.
/// </summary>
public sealed record LayoutKeyImageFormat
{
	/// <summary>One of <see cref="KeyImageEncodings" />. Decides the content type of every picture - see
	/// <see cref="KeyImageContentTypes" />.</summary>
	public required string Encoding { get; init; }

	/// <summary>Clockwise rotation in degrees applied after mirroring: 0, 90, 180 or 270.</summary>
	public int Rotation { get; init; }

	/// <summary>Whether the picture is mirrored left to right before it is rotated.</summary>
	public bool MirrorHorizontal { get; init; }

	/// <summary>
	/// A fixed corner radius for every key picture of the grid, in pixels of one delivered picture
	/// (<see cref="LayoutGrid.KeySize" />), the same before and after <see cref="Rotation" />. When set, the
	/// host rounds widget tiles, empty cells and the border ring by it instead of the profile's widget corner
	/// radius, whatever <see cref="LayoutVisualCapabilities.CornerRadius" /> says, and the profile's corner
	/// radius setting is reported as having no effect on the device. A widget spanning several keys is
	/// rounded at its outer corners; a key drawn as plain background is not shaped.
	/// 0 means square keys. A value above half the smaller side of the key is clamped to it, a negative
	/// value is a format the host cannot honour. <c>null</c>, the default, keeps the profile's radius. A host
	/// that predates this property ignores it and uses the profile's radius when
	/// <see cref="LayoutVisualCapabilities.CornerRadius" /> is true, so declare that false as well if the keys
	/// must not take the profile's radius there either.
	/// </summary>
	public int? CornerRadius { get; init; }
}
