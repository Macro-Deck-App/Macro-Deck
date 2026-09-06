using MacroDeck.Plugin.Packaging.Artifacts;

namespace MacroDeck.Signing.Packages;

/// <summary>
/// A Macro Deck package format <see cref="PackageSigner"/> and <see cref="PackageVerifier"/> can sign or
/// verify. Every format is a ZIP archive that carries its own signature: <c>certificate.json</c> and
/// <c>certificate.sig</c> at the archive root, and a <c>signature</c> object embedded in its manifest.
/// There is no detached signature file for any of the four - a signed artifact always verifies on its own.
/// </summary>
public enum SignablePackageFormat
{
	/// <summary>A <c>.macroDeckPlugin</c> archive. Manifest entry <c>manifest.json</c>; digest computed by
	/// <see cref="MacroDeck.Plugin.Packaging.Artifacts.PluginArtifactDigest"/>.</summary>
	Plugin,

	/// <summary>A <c>.macroDeckIconPack</c> archive. Manifest entry <c>pack.json</c>; digest computed by
	/// <see cref="IconPackDigest"/>.</summary>
	IconPack,

	/// <summary>A <c>.macroDeckProfile</c> archive. Manifest entry <c>manifest.json</c>; digest computed by
	/// <see cref="PortablePackageDigest"/>.</summary>
	Profile,

	/// <summary>A <c>.macroDeckFolder</c> archive. Manifest entry <c>manifest.json</c>; digest computed by
	/// <see cref="PortablePackageDigest"/>.</summary>
	Folder,

	/// <summary>A <c>.macroDeckWidget</c> archive. Manifest entry <c>manifest.json</c>; digest computed by
	/// <see cref="PortablePackageDigest"/>.</summary>
	Widget
}

/// <summary>Resolves a package path to the <see cref="SignablePackageFormat"/> its extension names.</summary>
public static class SignablePackageFormats
{
	/// <summary>The <c>MacroDeckHost.Application</c> project that owns the canonical constants for these
	/// four extensions cannot be referenced from the SDK, so they are declared here instead.</summary>
	public const string IconPackExtension = ".macroDeckIconPack";

	public const string ProfileExtension = ".macroDeckProfile";

	public const string FolderExtension = ".macroDeckFolder";

	public const string WidgetExtension = ".macroDeckWidget";

	/// <summary>Resolves <paramref name="path"/>'s extension to a <see cref="SignablePackageFormat"/>,
	/// case-insensitively, or returns <see langword="null"/> when it names no known format.</summary>
	public static SignablePackageFormat? Resolve(string path)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);

		var extension = Path.GetExtension(path);
		if (extension.Equals(PluginArtifactFiles.MacroDeckPluginExtension, StringComparison.OrdinalIgnoreCase))
		{
			return SignablePackageFormat.Plugin;
		}

		if (extension.Equals(IconPackExtension, StringComparison.OrdinalIgnoreCase))
		{
			return SignablePackageFormat.IconPack;
		}

		if (extension.Equals(ProfileExtension, StringComparison.OrdinalIgnoreCase))
		{
			return SignablePackageFormat.Profile;
		}

		if (extension.Equals(FolderExtension, StringComparison.OrdinalIgnoreCase))
		{
			return SignablePackageFormat.Folder;
		}

		if (extension.Equals(WidgetExtension, StringComparison.OrdinalIgnoreCase))
		{
			return SignablePackageFormat.Widget;
		}

		return null;
	}
}
