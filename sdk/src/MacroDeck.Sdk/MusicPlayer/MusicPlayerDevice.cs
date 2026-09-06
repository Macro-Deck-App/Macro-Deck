namespace MacroDeck.Sdk.MusicPlayer;

/// <summary>
/// A playback device a music player provider can switch to, e.g. a Spotify Connect endpoint.
/// <see cref="Id"/> is opaque to the host and resolved by the provider in
/// <see cref="IMusicPlayerDeviceProvider.TransferPlaybackAsync"/>.
/// </summary>
/// <param name="Type">
/// Free-form display string the provider assigns (e.g. Spotify: "Computer", "Smartphone",
/// "Speaker"). Not an enum - providers differ too much in their device taxonomy to unify one.
/// </param>
public sealed record MusicPlayerDevice(
	string Id,
	string Name,
	string? Type = null,
	bool IsActive = false,
	int? VolumePercent = null);
