namespace MacroDeckHost.Application.MusicPlayer;

public static class ArtworkVariants
{
	public static readonly int[] TargetSizes = [128, 256];

	public static int? Resolve(int? requestedSize, IEnumerable<int> availableSizes)
	{
		if (requestedSize is null)
		{
			return null;
		}

		return availableSizes
			.Where(size => size >= requestedSize.Value)
			.OrderBy(size => size)
			.Select(size => (int?)size)
			.FirstOrDefault();
	}
}
