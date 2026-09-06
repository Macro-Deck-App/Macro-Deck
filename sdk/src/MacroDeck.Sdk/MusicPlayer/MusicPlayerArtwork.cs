namespace MacroDeck.Sdk.MusicPlayer;

/// <summary>
/// Raw album/track artwork bytes resolved by a provider. The host serves these to the UI so
/// the frontend never talks to the provider directly.
/// </summary>
public sealed record MusicPlayerArtwork(byte[] Data, string MimeType);
