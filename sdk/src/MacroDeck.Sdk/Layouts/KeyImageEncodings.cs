namespace MacroDeck.Sdk.Layouts;

/// <summary>
/// The encodings a <see cref="LayoutKeyImageFormat" /> may name. A string vocabulary like
/// <see cref="LayoutRegionKinds" />, so a later version can add one without breaking either side; a host
/// that does not know the encoding a layout asks for renders no key images for it.
/// </summary>
public static class KeyImageEncodings
{
	/// <summary>Raw pixels, delivered as <see cref="KeyImageContentTypes.Rgba" />.</summary>
	public const string Rgba = "rgba";

	/// <summary>Delivered as <see cref="KeyImageContentTypes.Png" />.</summary>
	public const string Png = "png";

	/// <summary>Delivered as <see cref="KeyImageContentTypes.Jpeg" />, at quality 90.</summary>
	public const string Jpeg = "jpeg";

	/// <summary>Delivered as <see cref="KeyImageContentTypes.Bmp" />.</summary>
	public const string Bmp = "bmp";

	public static readonly IReadOnlyList<string> All = [Rgba, Png, Jpeg, Bmp];

	private static readonly HashSet<string> _known = new(All, StringComparer.Ordinal);

	public static bool IsKnown(string? encoding)
	{
		return encoding is not null && _known.Contains(encoding);
	}
}
