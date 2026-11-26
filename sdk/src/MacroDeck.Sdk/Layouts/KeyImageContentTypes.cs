namespace MacroDeck.Sdk.Layouts;

/// <summary>The content type of a key image for each of <see cref="KeyImageEncodings" />.</summary>
public static class KeyImageContentTypes
{
	/// <summary>
	/// Uncompressed 8-bit RGBA, top row first, each row width x 4 bytes with no padding, so the content is
	/// exactly width x height x 4 bytes. Always opaque: every alpha byte is 255.
	/// </summary>
	public const string Rgba = "application/x-macrodeck-rgba";

	public const string Png = "image/png";

	/// <summary>Baseline JPEG at quality 90.</summary>
	public const string Jpeg = "image/jpeg";

	/// <summary>A 24-bit BMP file with its rows stored bottom-up, as the format defines by default.</summary>
	public const string Bmp = "image/bmp";

	/// <summary>The content type pictures in <paramref name="encoding" /> carry, or null for an encoding
	/// this version does not know.</summary>
	public static string? For(string? encoding)
	{
		return encoding switch
		{
			KeyImageEncodings.Rgba => Rgba,
			KeyImageEncodings.Png => Png,
			KeyImageEncodings.Jpeg => Jpeg,
			KeyImageEncodings.Bmp => Bmp,
			_ => null
		};
	}
}
