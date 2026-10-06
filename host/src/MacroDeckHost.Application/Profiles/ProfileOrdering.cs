using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Profiles;

public static class ProfileOrdering
{
	public static List<ProfileEntity> Sort(IEnumerable<ProfileEntity> profiles)
		=> profiles
			.OrderBy(profile => profile.Order)
			.ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
			.ThenBy(profile => profile.Id)
			.ToList();
}
