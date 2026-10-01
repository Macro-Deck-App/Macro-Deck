using System.Text.RegularExpressions;
using MacroDeck.Plugin.Protocol.Capabilities.Ui;

namespace MacroDeck.Plugin.Cli.Rendering;

internal static partial class PreviewFileNames
{
	public static IReadOnlyDictionary<string, string> BaseNames(IReadOnlyList<UiPreviewDescriptorDto> previews)
	{
		var names = new Dictionary<string, string>(StringComparer.Ordinal);
		var used = new HashSet<string>(StringComparer.Ordinal);
		var crowded = previews.GroupBy(preview => Slug(preview.Scenario))
			.Where(group => group.Count() > 1)
			.Select(group => group.Key)
			.ToHashSet(StringComparer.Ordinal);

		foreach (var preview in previews)
		{
			var name = Slug(preview.Scenario);

			if (crowded.Contains(name))
			{
				name = $"{Slug(preview.View)}-{name}";
			}

			var unique = name;

			for (var index = 2; !used.Add(unique); index++)
			{
				unique = $"{name}-{index}";
			}

			names[preview.Id] = unique;
		}

		return names;
	}

	public static string Slug(string text)
	{
		var slug = NonAlphanumeric().Replace(text.ToLowerInvariant(), "-").Trim('-');

		return slug.Length == 0 ? "preview" : slug;
	}

	[GeneratedRegex("[^a-z0-9]+")]
	private static partial Regex NonAlphanumeric();
}
