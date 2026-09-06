namespace MacroDeck.Plugin.Hosting;

/// <summary>
/// What a plugin says about itself. Fixed once <see cref="PluginHostBuilder.Build" /> has run.
///
/// <para>
/// Identity now comes from <c>manifest.json</c> at the content root - which, for a host-launched plugin,
/// is its version directory. <see cref="Id" /> still reaches the host via registration or the launch
/// environment; <see cref="Name" /> and <see cref="Version" /> both ride the session request instead
/// (both change across a plugin's own updates, while registration happens once).
/// <see cref="Description" /> and <see cref="IconPath" /> stay local - they are served from
/// <c>/_macrodeck/info</c> and logged at startup, but carrying them over the wire needs a protocol
/// addition, which belongs with the host-side work rather than here.
/// </para>
/// </summary>
public sealed record PluginMetadata
{
	/// <summary>Reverse-domain owner id, e.g. <c>com.example.my-plugin</c>. Validated at build.</summary>
	public required string Id { get; init; }

	/// <summary>Shown to the user wherever the plugin is listed.</summary>
	public required string Name { get; init; }

	/// <summary>The plugin's own version, independent of the protocol and SDK versions.</summary>
	public required string Version { get; init; }

	public string? Description { get; init; }

	/// <summary>The manifest's <c>icon</c>, verbatim - a forward-slash path relative to the content root,
	/// never a resolved absolute path. Verified to exist at build.</summary>
	public string? IconPath { get; init; }
}
