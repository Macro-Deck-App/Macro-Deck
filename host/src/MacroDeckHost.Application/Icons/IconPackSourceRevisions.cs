using MacroDeckHost.Application.Caching;

namespace MacroDeckHost.Application.Icons;

public static class IconPackSourceRevisions
{
	public static async Task ForgetSourceRevision(this IIconPackCache cache, Guid packId)
	{
		if (cache.GetPackById(packId) is { SourceRevision: not null } pack)
		{
			pack.SourceRevision = null;
			await cache.AddOrUpdatePack(pack);
		}
	}
}
