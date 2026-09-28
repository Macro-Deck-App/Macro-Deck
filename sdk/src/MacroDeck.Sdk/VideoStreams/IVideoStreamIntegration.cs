namespace MacroDeck.Sdk.VideoStreams;

/// <summary>
/// Implemented by integrations that offer live video streams. The integration registers any number of
/// <see cref="IVideoStreamProvider" /> objects, one per source it talks to (an OBS instance, a camera, a
/// capture device), and Macro Deck opens sessions on them for the consumers that show a stream.
/// </summary>
/// <remarks>
/// The host calls <see cref="InitializeAsync" /> after the integration itself has initialized. Every
/// provider registered through the context is withdrawn, and every open session closed with
/// <see cref="VideoStreamSessionReason.ProviderRemoved" />, when the integration stops or initializes again,
/// so a provider never outlives the integration that registered it.
/// </remarks>
public interface IVideoStreamIntegration
{
	/// <summary>
	/// Registers the providers the integration offers, and keeps registering and withdrawing them as its
	/// configuration changes. A provider whose source is not configured yet registers later.
	/// </summary>
	Task InitializeAsync(IVideoStreamProviderContext context, CancellationToken cancellationToken = default);
}
