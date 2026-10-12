using MacroDeck.Localization;

namespace MacroDeck.Sdk.MusicPlayer;

/// <summary>A music player as every reader sees it, qualified with the provider that offers it.</summary>
public sealed record MusicPlayerInstanceSnapshot
{
	/// <summary>
	/// The player's id across all providers, <c>&lt;provider id&gt;::&lt;instance id&gt;</c>, for example
	/// <c>net.example.jukebox::default</c>. It is the id album-art URLs, artwork registration and
	/// <see cref="IMusicPlayerApi.GetAlbumColorAsync" /> take.
	/// </summary>
	public required string Id { get; init; }

	/// <summary>The plugin or integration that offers the player.</summary>
	public required string ProviderId { get; init; }

	/// <summary>The provider's name, for example <c>Spotify</c>.</summary>
	public LocalizedText ProviderName { get; init; }

	/// <summary>The name the provider gives the player, for example <c>Spotify (alice)</c>.</summary>
	public required string DisplayName { get; init; }
}
