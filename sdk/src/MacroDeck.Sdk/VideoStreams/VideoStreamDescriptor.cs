using MacroDeck.Localization;

namespace MacroDeck.Sdk.VideoStreams;

/// <summary>One stream a provider offers.</summary>
/// <param name="Id">
/// Stable id, unique within the provider: 1 to 256 characters, no control characters. Spaces are allowed,
/// so a source's own name, such as an OBS scene name, can serve as the id. Consumers store it.
/// </param>
/// <param name="Name">The stream's name as a picker shows it, in the reader's own language.</param>
/// <param name="Description">One sentence under the name. Absent means none.</param>
/// <param name="Width">Native width in pixels, when known. With <paramref name="Height" /> it gives the aspect
/// ratio a consumer reserves before the first frame.</param>
/// <param name="Height">Native height in pixels, when known.</param>
/// <param name="HasAudio">Whether the stream carries audio.</param>
/// <param name="State">The stream's state at its source.</param>
/// <param name="Metadata">Provider-defined, opaque to Macro Deck. At most 32 entries, keys up to 64 and
/// values up to 2048 characters.</param>
public sealed record VideoStreamDescriptor(
	string Id,
	LocalizedText Name,
	LocalizedText? Description = null,
	int? Width = null,
	int? Height = null,
	bool HasAudio = false,
	VideoStreamState State = VideoStreamState.Connected,
	IReadOnlyDictionary<string, string>? Metadata = null);
