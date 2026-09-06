namespace MacroDeck.Sdk.MusicPlayer;

/// <summary>
/// Provider-agnostic snapshot of what a music player is currently doing. Returned by
/// <see cref="IMusicPlayer.GetStateAsync"/> and pushed to the Music Player widget.
/// </summary>
public sealed record MusicPlayerState
{
	/// <summary>False when the provider has no usable session (not authenticated, no device, …).</summary>
	public bool IsConnected { get; init; }

	/// <summary>
	/// True when the provider is configured and retrying, but cannot answer right now (rate limit,
	/// outage). <see cref="IsConnected"/> is false either way; this separates "we cannot reach it at the
	/// moment" from "there is nothing set up", which the widget must not render the same - a temporary
	/// outage showing "Not connected" reads as if the music player had disappeared.
	/// </summary>
	public bool IsUnavailable { get; init; }

	/// <summary>
	/// Short reason for <see cref="IsUnavailable"/>, shown in the widget in place of the artist line.
	/// Keep it to a few words; the widget renders at deck-button size.
	/// </summary>
	public string? StatusMessage { get; init; }

	public PlaybackState PlaybackState { get; init; }

	public string? TrackName { get; init; }

	public IReadOnlyList<string> Artists { get; init; } = [];

	public string? AlbumName { get; init; }

	/// <summary>
	/// Opaque id the host resolves to artwork bytes via <see cref="IMusicPlayer.GetArtworkAsync"/>.
	/// Never a provider URL - the UI requests artwork from the host.
	/// </summary>
	public string? ArtworkId { get; init; }

	public TimeSpan? Position { get; init; }

	public TimeSpan? Duration { get; init; }

	/// <summary>Volume in percent (0–100), or <c>null</c> when the provider does not report it.</summary>
	public int? VolumePercent { get; init; }

	public bool ShuffleEnabled { get; init; }

	public RepeatMode RepeatMode { get; init; }

	public string? DeviceName { get; init; }

	public string? DeviceType { get; init; }

	/// <summary>A disconnected/idle state with no track information.</summary>
	public static MusicPlayerState Disconnected { get; } = new();

	/// <summary>
	/// A configured provider that cannot be reached right now. Distinct from <see cref="Disconnected"/>,
	/// which means there is nothing to reach in the first place.
	/// </summary>
	public static MusicPlayerState Unavailable(string? statusMessage = null)
		=> new() { IsUnavailable = true, StatusMessage = statusMessage };
}
