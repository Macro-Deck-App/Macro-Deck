namespace MacroDeck.Plugin.Protocol.Versioning;

/// <summary>
/// A single monotonically increasing integer major - not semver. Unknown fields are ignored and
/// unknown types are reported rather than fatal, so every additive change is backward-compatible by
/// construction; a bump only ever means "breaking", which is what a semver triple's minor and patch
/// components would never express.
/// </summary>
public static class ProtocolVersions
{
	public const int Minimum = 1;

	public const int Current = 3;

	/// <summary>
	/// The first version whose descriptor DTOs carry localized text: a plugin's action names, parameter
	/// labels and config-flow text may be a <c>{"$localized":…}</c> reference the reader's client
	/// resolves, instead of text already flattened into the plugin's own language. Below this a plugin
	/// must send a plain string, because a host that predates it would reject the object shape.
	/// </summary>
	public const int LocalizedDescriptors = 3;

	public static readonly IReadOnlyList<int> Supported = BuildSupported();

	private static List<int> BuildSupported()
	{
		var versions = new List<int>(Current - Minimum + 1);
		for (var version = Minimum; version <= Current; version++)
		{
			versions.Add(version);
		}

		return versions;
	}
}
