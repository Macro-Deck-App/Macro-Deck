using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Pbm;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Qoi;
using SixLabors.ImageSharp.Formats.Tga;
using SixLabors.ImageSharp.Formats.Webp;

namespace MacroDeckHost.Infrastructure.Icons;

internal static class ImageSharpDecoding
{
	// Every default format except TIFF: icons and artwork are untrusted input, and no supported source needs the
	// TIFF decoder, whose advisories stay open on 3.1 (audit suppressions in Directory.Packages.props).
	public static Configuration Configuration { get; } = new(new PngConfigurationModule(),
		new JpegConfigurationModule(),
		new GifConfigurationModule(),
		new BmpConfigurationModule(),
		new PbmConfigurationModule(),
		new TgaConfigurationModule(),
		new WebpConfigurationModule(),
		new QoiConfigurationModule());

	public static DecoderOptions Options { get; } = new() { Configuration = Configuration };
}
