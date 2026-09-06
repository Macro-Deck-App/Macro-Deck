namespace MacroDeckHost.Application.MusicPlayer;

public sealed record ArtworkImageResult(byte[] Content, string ContentType, string ETag);

public interface IMusicPlayerArtworkService
{
	string GetETag(string artworkId, int? size);

	Task<ArtworkImageResult?> GetImage(string instanceId,
		string artworkId,
		int? size,
		CancellationToken cancellationToken);
}
