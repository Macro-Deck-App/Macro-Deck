namespace MacroDeckHost.Application.Packaging;

/// <summary>Detached signature over a package's canonical digest. Written only by the signing tool,
/// never by the host - this type exists so <c>IconPackManifest</c> and <c>PortableArchiveManifest</c>
/// round-trip it unchanged rather than dropping it on the next export.</summary>
public sealed class PackageSignature
{
	/// <summary>Only <c>"ed25519"</c> is understood.</summary>
	public string Algorithm { get; set; } = string.Empty;

	public string KeyId { get; set; } = string.Empty;

	/// <summary>Base64.</summary>
	public string Value { get; set; } = string.Empty;

	public DateTimeOffset? SignedAt { get; set; }
}
