using System.Text;
using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.IconPacks;

namespace MacroDeck.Signing.Packages;

/// <summary>Which archive entry carries a <see cref="SignablePackageFormat"/>'s manifest.</summary>
internal static class PackageManifestEntry
{
	/// <summary>The icon pack format's manifest is <c>pack.json</c>, not <c>manifest.json</c> - the only
	/// one of the four formats that differs.</summary>
	public const string IconPackManifestFileName = "pack.json";

	public static string NameFor(SignablePackageFormat format) => format switch
	{
		SignablePackageFormat.IconPack => IconPackManifestFileName,
		_ => PluginArtifactFiles.ManifestFileName
	};

	public static int MaxBytesFor(SignablePackageFormat format) => format switch
	{
		SignablePackageFormat.IconPack => IconPackArchiveLimits.MaxManifestBytes,
		_ => PluginArtifactLimits.MaxManifestBytes
	};

	public static async Task<string?> ReadBoundedAsync(Stream manifest, int maxBytes, CancellationToken cancellationToken)
	{
		using var buffer = new MemoryStream();
		var chunk = new byte[81_920];
		int read;
		while ((read = await manifest.ReadAsync(chunk, cancellationToken)) > 0)
		{
			if (buffer.Length + read > maxBytes)
			{
				return null;
			}

			buffer.Write(chunk, 0, read);
		}

		buffer.Position = 0;
		using var reader = new StreamReader(buffer, Encoding.UTF8);
		return await reader.ReadToEndAsync(cancellationToken);
	}
}
