namespace MacroDeckHost.Application.MusicPlayer;

public sealed record ProcessedArtworkResult(byte[] MasterWebp, IReadOnlyDictionary<int, byte[]> Variants);

public interface IArtworkProcessor
{
	Task<ProcessedArtworkResult?> Process(byte[] original, CancellationToken cancellationToken);
}
