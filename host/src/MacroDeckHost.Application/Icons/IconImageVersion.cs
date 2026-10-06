using System.Text;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Icons;

namespace MacroDeckHost.Application.Icons;

public static class IconImageVersion
{
	public static string? Of(IconEntity icon) => icon.MasterContentHash ?? icon.SourceContentHash;

	public static string? Of(IconEntity icon, IReadOnlyList<IconEntity> appearances)
	{
		var own = Of(icon);
		if (appearances.Count == 0)
		{
			return own;
		}

		var folded = new StringBuilder(own ?? icon.Id.ToString("N"));
		foreach (var entry in appearances
			.Select(appearance => (Key: Key(appearance), Appearance: appearance))
			.OrderBy(entry => entry.Key, StringComparer.Ordinal))
		{
			folded.Append('|')
				.Append(entry.Key)
				.Append('=')
				.Append(Of(entry.Appearance) ?? entry.Appearance.Id.ToString("N"))
				.Append(':')
				.Append(entry.Appearance.ProcessingState);
		}

		return ContentHash.Compute(Encoding.UTF8.GetBytes(folded.ToString()));
	}

	private static string Key(IconEntity appearance)
		=> appearance.AppearanceTraits is { } traits ? IconAppearanceTraits.ToKey(traits) : string.Empty;
}
