using System.Text;
using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeck.Plugin.Packaging.Artifacts;

/// <summary>
/// The canonical byte sequence a plugin signature covers.
/// <para>
/// It is deliberately <em>not</em> re-serialized manifest JSON. Two serializers - or two versions of one -
/// disagree about property order, escaping and number formatting, so a signature over re-serialized JSON
/// breaks for reasons that have nothing to do with the artifact. This document is derived from the fields
/// that identify the payload and is stable by construction.
/// </para>
/// </summary>
public static class PluginArtifactDigest
{
	/// <summary>Prefix carrying the digest document's own format version, so a future layout is
	/// distinguishable rather than silently mis-verified.</summary>
	private const string DocumentHeader = "macro-deck-plugin/1";

	/// <summary>
	/// Builds the digest document. The layout is a public contract - changing it invalidates every
	/// signature ever issued - and is documented in ADR 0029 and the plugin hosting guide:
	/// <code>
	/// macro-deck-plugin/1\n
	/// &lt;id&gt;\n
	/// &lt;version&gt;\n
	/// e\t&lt;rid&gt;\t&lt;kind&gt;\t&lt;dotnetVersion&gt;\t&lt;executable&gt;\t&lt;arg&gt;\t...\n   (sorted by rid)
	/// p\t&lt;permission&gt;\n                                              (sorted)
	/// f\t&lt;path&gt;\t&lt;sha256&gt;\t&lt;size&gt;\n                                  (sorted by path)
	/// </code>
	/// <para>
	/// The entrypoints and permissions are in here, not just the file digests, because those are what
	/// decide which file runs, with which arguments, and what it claims to be allowed to reach. A digest
	/// over the payload alone would let a signed artifact be repackaged to launch a different signed file
	/// with attacker-chosen arguments while the signature still verified.
	/// </para>
	/// </summary>
	public static byte[] Compute(PluginManifest manifest)
	{
		ArgumentNullException.ThrowIfNull(manifest);

		var builder = new StringBuilder();
		builder.Append(DocumentHeader).Append('\n');
		builder.Append(manifest.Id).Append('\n');
		builder.Append(manifest.Version).Append('\n');

		foreach (var (rid, entrypoint) in manifest.Entrypoints.OrderBy(pair => pair.Key, StringComparer.Ordinal))
		{
			var runtime = entrypoint.Runtime;
			builder.Append("e\t").Append(rid).Append('\t')
				.Append(runtime?.Kind ?? PluginEntrypointRuntimeKind.SelfContained).Append('\t')
				.Append(runtime?.DotnetVersion ?? string.Empty).Append('\t')
				.Append(entrypoint.Executable);

			foreach (var argument in entrypoint.Arguments ?? [])
			{
				builder.Append('\t').Append(argument);
			}

			builder.Append('\n');
		}

		foreach (var permission in (manifest.Permissions ?? []).OrderBy(value => value, StringComparer.Ordinal))
		{
			builder.Append("p\t").Append(permission).Append('\n');
		}

		foreach (var file in (manifest.Files ?? []).OrderBy(entry => entry.Path, StringComparer.Ordinal))
		{
			builder.Append("f\t").Append(file.Path).Append('\t')
				.Append(file.Sha256).Append('\t')
				.Append(file.Size).Append('\n');
		}

		return Encoding.UTF8.GetBytes(builder.ToString());
	}
}
