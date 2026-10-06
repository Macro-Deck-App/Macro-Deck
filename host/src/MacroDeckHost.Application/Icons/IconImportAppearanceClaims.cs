using MacroDeckHost.Application.Caching;
using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Icons;

public enum IconImportAppearanceClaim
{
	Claimed,
	Taken,
	Refused
}

public sealed class IconImportAppearanceClaims
{
	private readonly IIconPackCache _cache;
	private readonly Guid _batchId;
	private readonly Dictionary<Guid, HashSet<string>> _claimedKeys = new();

	public IconImportAppearanceClaims(IIconPackCache cache, Guid batchId)
	{
		_cache = cache;
		_batchId = batchId;
	}

	public IconImportAppearanceClaim Claim(IconEntity parent, IReadOnlyDictionary<string, string> traits)
	{
		if (!_claimedKeys.TryGetValue(parent.Id, out var keys))
		{
			keys = _cache.GetAppearances(parent.Id)
				.Select(asset => asset.AppearanceTraits is { } existing ? IconAppearanceTraits.ToKey(existing) : string.Empty)
				.ToHashSet(StringComparer.Ordinal);
			_claimedKeys[parent.Id] = keys;
		}

		var key = IconAppearanceTraits.ToKey(traits);
		if (keys.Contains(key))
		{
			return parent.ImportBatchId == _batchId ? IconImportAppearanceClaim.Refused : IconImportAppearanceClaim.Taken;
		}

		if (keys.Count >= IconAppearanceTraits.MaxAppearancesPerIcon)
		{
			return IconImportAppearanceClaim.Refused;
		}

		keys.Add(key);
		return IconImportAppearanceClaim.Claimed;
	}
}
