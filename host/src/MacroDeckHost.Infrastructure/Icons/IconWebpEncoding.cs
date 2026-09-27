using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;

namespace MacroDeckHost.Infrastructure.Icons;

internal static class IconWebpEncoding
{
	private const int StaticQuality = 85;
	private const int AnimatedQuality = 75;

	// Animations stay lossy at Level2: lossless took tens of seconds per icon and produced resized
	// renditions larger than the master. The alpha plane stays exact, so transparent frames still clear.
	public static WebpEncoder For(bool isAnimated)
		=> isAnimated
			? new WebpEncoder
			{
				FileFormat = WebpFileFormatType.Lossy,
				Quality = AnimatedQuality,
				Method = WebpEncodingMethod.Level2
			}
			: new WebpEncoder { FileFormat = WebpFileFormatType.Lossy, Quality = StaticQuality };

	public static async Task<byte[]> EncodeToBytes(Image image,
		WebpEncoder encoder,
		CancellationToken cancellationToken)
	{
		using var stream = new MemoryStream();
		await image.SaveAsync(stream, encoder, cancellationToken);
		return stream.ToArray();
	}
}
