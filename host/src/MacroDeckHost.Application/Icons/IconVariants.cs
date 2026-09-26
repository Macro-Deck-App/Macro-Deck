using System.Globalization;
using MacroDeckHost.Domain.Icons;

namespace MacroDeckHost.Application.Icons;

public static class IconVariants
{
	public const string Master = "master";

	public static readonly int[] TargetSizes = [128, 256, 512];

	public static string Derived(int size, string masterContentHash)
		=> $"{size.ToString(CultureInfo.InvariantCulture)}-{MasterToken(masterContentHash)}";

	public static string MasterToken(string masterContentHash)
	{
		var hex = masterContentHash.StartsWith(ContentHash.Sha256Prefix, StringComparison.Ordinal)
			? masterContentHash[ContentHash.Sha256Prefix.Length..]
			: masterContentHash;
		return hex.Length > 16 ? hex[..16] : hex;
	}

	public static string Resolve(int? requestedSize, IReadOnlyList<int> availableSizes)
	{
		if (requestedSize is null || availableSizes.Count == 0)
		{
			return Master;
		}

		var best = availableSizes
			.Where(size => size >= requestedSize.Value)
			.OrderBy(size => size)
			.Select(size => (int?)size)
			.FirstOrDefault();

		return best?.ToString(CultureInfo.InvariantCulture) ?? Master;
	}
}
