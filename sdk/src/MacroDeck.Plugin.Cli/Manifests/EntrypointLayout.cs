using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeck.Plugin.Cli.Manifests;

/// <summary>
/// The one judgement of whether a manifest's declared entrypoints can even be staged without colliding -
/// shared by <see cref="Building.PluginBuilder" /> (which must fail a build over it) and
/// <see cref="ManifestValidator" /> (which must report it as a package-level problem), so the two commands
/// can never disagree about the same manifest.
/// </summary>
internal static class EntrypointLayout
{
	/// <summary><paramref name="Failure" /> is null when every entrypoint resolves to its own directory.
	/// <paramref name="OffendingRids" /> names whichever entrypoint(s) <paramref name="Failure" /> is about -
	/// one RID for an entrypoint sitting at the package root, two for a pair sharing a directory.</summary>
	public readonly record struct Resolution(
		IReadOnlyDictionary<string, string> Destinations,
		string? Failure,
		IReadOnlyList<string> OffendingRids);

	/// <summary>Resolves each of <paramref name="rids" />'s declared entrypoint to the directory it would
	/// stage into, stopping at the first RID whose entrypoint sits at the package root or shares a directory
	/// with an earlier one - the same first-failure behaviour <c>build</c> already had before this was
	/// extracted.</summary>
	public static Resolution Resolve(PluginManifest manifest, IReadOnlyList<string> rids)
	{
		var destinations = new Dictionary<string, string>(StringComparer.Ordinal);
		var byDirectory = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		foreach (var rid in rids)
		{
			var normalized = EntrypointPresence.Normalize(manifest.Entrypoints[rid].Executable);
			var separator = normalized.LastIndexOf('/');

			if (separator <= 0)
			{
				var failure = $"Entrypoint '{rid}' declares '{manifest.Entrypoints[rid].Executable}' at the package " +
					"root. Per-runtime output needs its own directory, conventionally 'runtimes/" +
					rid +
					"/', " +
					"so platforms cannot overwrite each other.";

				return new Resolution(destinations, failure, [rid]);
			}

			var directory = normalized[..separator];

			if (byDirectory.TryGetValue(directory, out var other))
			{
				var failure = $"Entrypoints '{other}' and '{rid}' both stage into '{directory}'. Each runtime " +
					"identifier needs its own directory, conventionally 'runtimes/<rid>/'.";

				return new Resolution(destinations, failure, [other, rid]);
			}

			byDirectory[directory] = rid;
			destinations[rid] = directory;
		}

		return new Resolution(destinations, null, []);
	}
}
