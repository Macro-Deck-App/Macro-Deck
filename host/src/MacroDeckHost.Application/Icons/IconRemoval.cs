using MacroDeckHost.Application.Caching;
using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Icons;

public static class IconRemoval
{
	public static IReadOnlyList<IconEntity> ExpandWithAppearances(IIconPackCache cache, IEnumerable<IconEntity> icons)
	{
		var expanded = new List<IconEntity>();
		var seen = new HashSet<Guid>();
		foreach (var icon in icons)
		{
			if (seen.Add(icon.Id))
			{
				expanded.Add(icon);
			}

			if (icon.AppearanceOfId is not null)
			{
				continue;
			}

			foreach (var appearance in cache.GetAppearances(icon.Id))
			{
				if (seen.Add(appearance.Id))
				{
					expanded.Add(appearance);
				}
			}
		}

		return expanded;
	}
}
