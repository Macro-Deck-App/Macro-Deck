namespace MacroDeck.Sdk.Layouts;

/// <summary>
/// How the host delivers the pictures it renders for each key of a grid - see
/// <see cref="LayoutGrid.KeyImages" />. Every picture is <see cref="LayoutGrid.KeySize" /> pixels as
/// delivered: the host renders the key, mirrors it when <see cref="MirrorHorizontal" /> is set, then
/// rotates it clockwise by <see cref="Rotation" />, so for 90 and 270 it renders the key at the swapped
/// size. A format the host cannot honour (no <see cref="LayoutGrid.KeySize" />, a rotation other than
/// 0, 90, 180 or 270, or an encoding it does not know) means no key images for the layout, never a
/// failed registration.
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
}
