using System.IO.Compression;
using System.Reflection;

namespace MacroDeck.Plugin.Cli.Rendering;

internal static class RendererAssets
{
	public static byte[] Script => Read("preview-renderer.js.gz");

	public static byte[] Style => Read("preview-renderer.css.gz");

	private static byte[] Read(string name)
	{
		var assembly = typeof(RendererAssets).Assembly;
		var resource = assembly.GetManifestResourceNames().Single(candidate => candidate.EndsWith(name, StringComparison.Ordinal));

		using var stream = assembly.GetManifestResourceStream(resource)!;
		using var gzip = new GZipStream(stream, CompressionMode.Decompress);
		using var buffer = new MemoryStream();
		gzip.CopyTo(buffer);

		return buffer.ToArray();
	}
}
