namespace MacroDeck.Sdk.MusicPlayer;

/// <summary>
/// A selectable music player exposed by a provider - typically one per configured account.
/// <see cref="Id"/> is unique within the provider; <see cref="DisplayName"/> is shown to the user
/// (e.g. "Spotify (alice)").
/// </summary>
public sealed record MusicPlayerInstance(string Id, string DisplayName);
