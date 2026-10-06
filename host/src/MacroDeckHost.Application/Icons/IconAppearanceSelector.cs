using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Icons;

public static class IconAppearanceSelector
{
	public static IconEntity Select(IconEntity parent,
		IReadOnlyList<IconEntity> appearances,
		IconAppearanceContext context)
	{
		if (appearances.Count == 0 || context.IsEmpty)
		{
			return parent;
		}

		var priority = context.Motion == IconAppearanceTraits.Static
			? new[] { IconAppearanceTraits.Motion, IconAppearanceTraits.ColorScheme }
			: new[] { IconAppearanceTraits.ColorScheme, IconAppearanceTraits.Motion };

		return appearances
			.Where(appearance => appearance.ProcessingState == IconProcessingState.Ready &&
				Matches(appearance.AppearanceTraits, context))
			.OrderByDescending(appearance => appearance.AppearanceTraits!.Count)
			.ThenByDescending(appearance => appearance.AppearanceTraits!.ContainsKey(priority[0]))
			.ThenByDescending(appearance => appearance.AppearanceTraits!.ContainsKey(priority[1]))
			.ThenBy(appearance => IconAppearanceTraits.ToKey(appearance.AppearanceTraits!), StringComparer.Ordinal)
			.FirstOrDefault() ?? parent;
	}

	public static IconEntity? FindPinned(IReadOnlyList<IconEntity> appearances, string canonicalKey)
		=> appearances.FirstOrDefault(appearance => appearance.ProcessingState == IconProcessingState.Ready &&
			appearance.AppearanceTraits is { } traits &&
			string.Equals(IconAppearanceTraits.ToKey(traits), canonicalKey, StringComparison.Ordinal));

	private static bool Matches(IReadOnlyDictionary<string, string>? traits, IconAppearanceContext context)
		=> traits is { Count: > 0 } &&
			traits.All(pair => string.Equals(context.ValueOf(pair.Key), pair.Value, StringComparison.Ordinal));
}
