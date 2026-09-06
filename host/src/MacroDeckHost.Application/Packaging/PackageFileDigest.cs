namespace MacroDeckHost.Application.Packaging;

/// <summary>One declared archive entry's identity, shared by every signable Macro Deck package format
/// (icon packs and portable archives). Mirrors <c>PluginFileDigest</c> in
/// <c>MacroDeck.Plugin.Packaging</c> so a signature's canonical digest is built the same way regardless
/// of which format it covers.</summary>
public sealed class PackageFileDigest
{
	/// <summary>Forward-slash separated, relative to the archive root.</summary>
	public string Path { get; set; } = string.Empty;

	/// <summary><c>sha256:&lt;64 lowercase hex&gt;</c>.</summary>
	public string Sha256 { get; set; } = string.Empty;

	public long Size { get; set; }
}
