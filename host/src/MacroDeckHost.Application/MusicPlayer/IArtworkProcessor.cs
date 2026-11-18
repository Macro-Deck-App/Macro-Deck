namespace MacroDeckHost.Application.MusicPlayer;

public sealed record ProcessedArtworkResult(
	byte[] Master,
	IReadOnlyDictionary<int, byte[]> Variants,
	string ContentType);

public interface IArtworkProcessor
{
	Task<ProcessedArtworkResult?> Process(byte[] original, CancellationToken cancellationToken);
}
