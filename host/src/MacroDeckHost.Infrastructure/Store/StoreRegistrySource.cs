using System.Text.Json;
using System.Text.RegularExpressions;
using MacroDeckHost.Application.Store.Model;

namespace MacroDeckHost.Infrastructure.Store;

internal static partial class StoreRegistrySource
{
	public static StoreVersionSource? Parse(JsonElement? source)
	{
		if (source is not { ValueKind: JsonValueKind.Object } element)
		{
			return null;
		}

		var commit = Text(element, "commit") is { } candidate && CommitPattern().IsMatch(candidate) ? candidate : null;
		var tag = Text(element, "tag") is { } name && IsTag(name) ? name : null;
		return commit is null && tag is null ? null : new StoreVersionSource { Commit = commit, Tag = tag };
	}

	private static bool IsTag(string tag)
	{
		return TagPattern().IsMatch(tag)
			&& !tag.StartsWith('/')
			&& !tag.StartsWith('-')
			&& !tag.Contains("..", StringComparison.Ordinal)
			&& !tag.EndsWith('/')
			&& !tag.EndsWith(".lock", StringComparison.Ordinal);
	}

	private static string? Text(JsonElement element, string name)
	{
		return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;
	}

	[GeneratedRegex(@"\A[0-9a-f]{40}\z")]
	private static partial Regex CommitPattern();

	[GeneratedRegex(@"\A[A-Za-z0-9._/+-]{1,200}\z")]
	private static partial Regex TagPattern();
}
