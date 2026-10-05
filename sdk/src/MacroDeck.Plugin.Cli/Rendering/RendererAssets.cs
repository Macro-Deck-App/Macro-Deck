using System.IO.Compression;

namespace MacroDeck.Plugin.Cli.Rendering;

internal static class RendererAssets
{
	private const string ScriptName = "preview-renderer.js.gz";

	public static bool IsEmbedded => typeof(RendererAssets).Assembly.GetManifestResourceInfo(ScriptName) is not null;

	public static byte[] Script => Read(ScriptName);

	public static byte[] Style => Read("preview-renderer.css.gz");

	private static byte[] Read(string name)
	{
		using var stream = typeof(RendererAssets).Assembly.GetManifestResourceStream(name) ??
			throw new InvalidOperationException(
				"This build of macrodeck-plugin has no preview renderer: run npm ci in ui and rebuild the CLI.");
		using var gzip = new GZipStream(stream, CompressionMode.Decompress);
		using var buffer = new MemoryStream();
		gzip.CopyTo(buffer);

		return buffer.ToArray();
	}
}
