using MacroDeckHost.Domain.Icons;

namespace MacroDeckHost.Application.Icons;

public sealed record ProcessedIconResult(
	byte[] MasterWebp,
	IReadOnlyDictionary<int, byte[]> Variants,
	int Width,
	int Height,
	bool IsAnimated,
	int? FrameCount,
	string OriginalFormat,
	SourceContentHash SourceContentHash)
{
	public MasterContentHash MasterContentHash { get; }
		= MasterContentHash.Compute(MasterWebp);
}
