using MacroDeck.Plugin.Protocol.Versioning;

namespace MacroDeck.Plugin.Packaging.Manifest;

/// <summary>
/// The <c>manifest.json</c> DTO for a plugin version, as #415's on-disk layout defines it
/// (<c>plugins/&lt;id&gt;/versions/&lt;version&gt;/manifest.json</c>). Unknown properties are ignored by the
/// reader for forward compatibility; this record only carries what the supervisor needs to launch and
/// monitor the plugin.
/// </summary>
public sealed record PluginManifest
{
	/// <summary>The only <see cref="ManifestVersion"/> this host understands. Anything else is rejected
	/// before the rest of the document is even deserialized.</summary>
	public const int SupportedManifestVersion = 1;

	public required int ManifestVersion { get; init; }

	public required string Id { get; init; }

	public required string Name { get; init; }

	public required string Version { get; init; }

	public string? Description { get; init; }

	/// <summary>Forward-slash separated path to the plugin's icon, relative to the version directory (the
	/// artifact root). Declarative only - nothing in the host loads these bytes; a running plugin's icon
	/// still arrives over the asset pipeline. Shape-validated by the reader, never checked to exist.</summary>
	public string? Icon { get; init; }

	/// <summary>Keyed by RID (e.g. <c>"win-x64"</c>). Resolved against
	/// <see cref="PluginRuntimeIdentifiers.CandidatesFor"/>.</summary>
	public required IReadOnlyDictionary<string, PluginEntrypoint> Entrypoints { get; init; }

	public PluginShutdownSettings? Shutdown { get; init; }

	public PluginHealthSettings? Health { get; init; }

	public PluginPublisher? Publisher { get; init; }

	/// <summary>SPDX identifier by convention; carried verbatim and never parsed.</summary>
	public string? License { get; init; }

	public string? Homepage { get; init; }

	public string? Repository { get; init; }

	/// <summary>Null means "declares nothing", which is never the same as "incompatible".</summary>
	public PluginCompatibility? Compatibility { get; init; }

	/// <summary>Declared for disclosure only. Nothing in the host enforces these today; see
	/// <c>PluginPermissions</c>.</summary>
	public IReadOnlyList<string>? Permissions { get; init; }

	/// <summary>The languages this plugin's own user-facing strings are available in, as BCP-47 tags
	/// (<c>en</c>, <c>de-DE</c>, <c>zh-Hant-TW</c>) - never truncated to a two-letter code, which would
	/// collapse <c>zh-Hans</c> and <c>zh-Hant</c> onto the same value. Lets a store list a plugin's
	/// languages before it is installed; a running plugin still serves its catalog over the localization
	/// capability, which stays the authority once it is. Filled in by the packaging pipeline from the
	/// plugin's resource files where it can discover them, and carried verbatim otherwise.</summary>
	public IReadOnlyList<string>? Languages { get; init; }

	public IReadOnlyList<PluginDependency>? Dependencies { get; init; }

	/// <summary>Plugins that must <em>not</em> be installed alongside this one.</summary>
	public IReadOnlyList<PluginDependency>? Conflicts { get; init; }

	public IReadOnlyList<PluginIconPackReference>? IconPacks { get; init; }

	/// <summary>Per-file digests covering the artifact payload. When present the installer verifies every
	/// entry and rejects undeclared files; when absent it records an advisory warning.</summary>
	public IReadOnlyList<PluginFileDigest>? Files { get; init; }

	public PluginSignature? Signature { get; init; }
}

/// <summary>One RID's launch target, relative to the version directory.</summary>
public sealed record PluginEntrypoint
{
	/// <summary>Relative to the version directory; must stay inside it, no <c>..</c> segments, no
	/// absolute path. Validated by the reader, not this record.</summary>
	public required string Executable { get; init; }

	public IReadOnlyList<string>? Arguments { get; init; }

	/// <summary>Null means <see cref="PluginEntrypointRuntimeKind.SelfContained"/> - the only behaviour
	/// that existed before this field, so an older manifest keeps its meaning.</summary>
	public PluginEntrypointRuntime? Runtime { get; init; }
}

/// <summary>How an entrypoint expects to be launched.</summary>
public sealed record PluginEntrypointRuntime
{
	public PluginEntrypointRuntimeKind Kind { get; init; } = PluginEntrypointRuntimeKind.SelfContained;

	/// <summary><c>major.minor</c>, e.g. <c>"10.0"</c>. Required when <see cref="Kind"/> is
	/// <see cref="PluginEntrypointRuntimeKind.FrameworkDependent"/>, ignored otherwise.</summary>
	public string? DotnetVersion { get; init; }
}

public enum PluginEntrypointRuntimeKind
{
	/// <summary>The executable runs on its own; the host launches it directly.</summary>
	SelfContained,

	/// <summary>The executable is a managed assembly launched through the <c>dotnet</c> muxer.</summary>
	FrameworkDependent
}

/// <summary>Who published the artifact. Informational; it is not an identity the host trusts on its own -
/// that is what <see cref="PluginSignature"/> is for.</summary>
public sealed record PluginPublisher
{
	public required string Name { get; init; }

	/// <summary>Reverse-domain publisher id when present, validated like a plugin id.</summary>
	public string? Id { get; init; }

	public string? Email { get; init; }

	public string? Url { get; init; }
}

/// <summary>The host surfaces this artifact expects. Every member is optional; an absent member declares
/// nothing and never blocks an install.</summary>
public sealed record PluginCompatibility
{
	/// <summary>Range over the plugin SDK version. Recorded but not enforced - the host cannot know which
	/// SDK a plugin was built against until it connects.</summary>
	public string? Sdk { get; init; }

	/// <summary>Integer-major protocol range, reused verbatim from the protocol package so the manifest
	/// and the wire handshake can never drift apart.</summary>
	public ProtocolVersionRange? Protocol { get; init; }

	/// <summary>Range over the Macro Deck host version.</summary>
	public string? MacroDeck { get; init; }
}

/// <summary>A declared relationship to another plugin, used for both dependencies and conflicts.</summary>
public sealed record PluginDependency
{
	public required string Id { get; init; }

	/// <summary>Null means any version.</summary>
	public string? VersionRange { get; init; }

	/// <summary>False makes this a hard requirement, true a recommendation.</summary>
	public bool Optional { get; init; }
}

/// <summary>A declared relationship to an icon pack. Shape-validated today; resolution is not implemented
/// because icon packs carry no stable string id yet.</summary>
public sealed record PluginIconPackReference
{
	public required string Id { get; init; }

	public string? VersionRange { get; init; }

	public bool Optional { get; init; }
}

/// <summary>One payload file's expected identity.</summary>
public sealed record PluginFileDigest
{
	/// <summary>Forward-slash separated, relative to the version directory.</summary>
	public required string Path { get; init; }

	/// <summary><c>sha256:&lt;64 lowercase hex&gt;</c>.</summary>
	public required string Sha256 { get; init; }

	public required long Size { get; init; }
}

/// <summary>Detached signature over the artifact digest. The host validates its shape and then verifies it
/// cryptographically against the signing certificate chain before trusting the artifact.</summary>
public sealed record PluginSignature
{
	/// <summary>Only <c>"ed25519"</c> is understood.</summary>
	public required string Algorithm { get; init; }

	public required string KeyId { get; init; }

	/// <summary>Base64.</summary>
	public required string Value { get; init; }

	public DateTimeOffset? SignedAt { get; init; }
}

/// <summary>Graceful-shutdown timing. <see cref="GracefulTimeoutSeconds"/> is clamped to <c>[1, 60]</c>
/// by the reader; this record carries whatever value survived that clamp.</summary>
public sealed record PluginShutdownSettings
{
	public int GracefulTimeoutSeconds { get; init; } = 10;
}

/// <summary>Health-probe timing and target. <see cref="IntervalSeconds"/> is clamped to <c>[5, 120]</c>,
/// <see cref="TimeoutSeconds"/> to <c>[1, 10]</c>, <see cref="UnhealthyThreshold"/> to <c>[2, 10]</c> -
/// the floor of 2 makes "do not restart after a single missed health check" structural.</summary>
public sealed record PluginHealthSettings
{
	public int IntervalSeconds { get; init; } = 15;

	public int TimeoutSeconds { get; init; } = 2;

	public int UnhealthyThreshold { get; init; } = 3;

	/// <summary>The SDK's actual health route - not <c>/health</c>.</summary>
	public string Path { get; init; } = "/_macrodeck/health";
}
