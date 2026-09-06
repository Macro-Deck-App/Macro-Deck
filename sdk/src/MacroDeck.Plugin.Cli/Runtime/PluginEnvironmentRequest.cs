using MacroDeck.Plugin.Hosting;

namespace MacroDeck.Plugin.Cli.Runtime;

/// <summary>What <see cref="PluginEnvironmentComposer.Compose" /> needs to build one mode's environment.
/// Every field the chosen <see cref="Mode" /> does not use is ignored, so a caller may populate both
/// managed and self-registering fields once and reuse the same request for either mode.</summary>
internal sealed record PluginEnvironmentRequest
{
	public required PluginRegistrationMode Mode { get; init; }

	/// <summary>Both modes: <c>MACRO_DECK_PLUGIN_HOST_URL</c>.</summary>
	public required string HostUrl { get; init; }

	/// <summary>Both modes: <c>MACRO_DECK_PLUGIN_INSTANCE_ID</c>.</summary>
	public required string InstanceId { get; init; }

	/// <summary>Both modes: becomes <c>ASPNETCORE_URLS</c> directly, not a <c>MACRO_DECK_PLUGIN_*</c>
	/// variable - the supervisor's own listener address for this launch.</summary>
	public required string ListenUrl { get; init; }

	/// <summary>Managed only: <c>MACRO_DECK_PLUGIN_ID</c>.</summary>
	public string? PluginId { get; init; }

	/// <summary>Managed only: <c>MACRO_DECK_PLUGIN_SECRET</c>.</summary>
	public string? Secret { get; init; }

	/// <summary>Managed only: <c>MACRO_DECK_PLUGIN_DATA_DIRECTORY</c>.</summary>
	public string? DataDirectory { get; init; }

	/// <summary>Managed only: <c>MACRO_DECK_PLUGIN_LAUNCH_ID</c>.</summary>
	public string? LaunchId { get; init; }

	/// <summary>Self-registering only: <c>MACRO_DECK_PLUGIN_ENROLLMENT_TOKEN</c>.</summary>
	public string? EnrollmentToken { get; init; }

	/// <summary>Self-registering only: <c>MACRO_DECK_PLUGIN_STATE_DIRECTORY</c>.</summary>
	public string? StateDirectory { get; init; }

	/// <summary>Self-registering only: <c>MACRO_DECK_PLUGIN_PAIRING</c>. Whether the launched plugin may
	/// fall back to interactive pairing when no <see cref="EnrollmentToken" /> is given.</summary>
	public bool PairingEnabled { get; init; } = true;
}
