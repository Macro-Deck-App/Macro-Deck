namespace MacroDeckHost.Application.MusicPlayer;

public sealed record ArtworkImageResult(byte[] Content, string ContentType, string ETag);

public interface IMusicPlayerArtworkService
{
	string GetETag(string artworkId, int? size);

	Task<ArtworkImageResult?> GetImage(string instanceId,
		string artworkId,
		int? size,
		CancellationToken cancellationToken);

	// Shares the plain instance's cache: MusicPlayerState.ArtworkId documents one image per id whatever the options.
	Task<ArtworkImageResult?> GetImage(MusicPlayerVariant variant,
		string artworkId,
		int? size,
		CancellationToken cancellationToken);
}
