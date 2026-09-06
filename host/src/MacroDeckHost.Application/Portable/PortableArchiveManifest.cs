using System.Text.Json.Serialization;
using MacroDeckHost.Application.Packaging;

namespace MacroDeckHost.Application.Portable;

public sealed class PortableArchiveManifest
{
	// Additive only: Files/Signature are optional and ignored when absent, so an older Macro Deck can
	// still read an archive written by this one. Bump this only for a change an older reader cannot
	// tolerate (see PortableArchive.cs:112).
	public const int CurrentFormatVersion = 2;

	public int FormatVersion { get; set; } = CurrentFormatVersion;

	public PortableArchiveKind Kind { get; set; }

	public string AppVersion { get; set; } = string.Empty;

	public DateTime CreatedAt { get; set; }

	public bool IncludesSecrets { get; set; }

	public PortableArchiveContents Contents { get; set; } = new();

	public PortableEncryptionInfo? Encryption { get; set; }

	/// <summary>
	/// Per-entry digests for an unencrypted archive. An encrypted archive declares no files - the
	/// payload does not exist until after the manifest bytes are used as encryption associated data -
	/// so a password-protected export is not signable.
	/// </summary>
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public List<PackageFileDigest>? Files { get; set; }

	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public PackageSignature? Signature { get; set; }
}
