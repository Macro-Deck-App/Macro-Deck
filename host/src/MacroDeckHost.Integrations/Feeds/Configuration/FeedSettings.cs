using System.Globalization;
using System.Text.Json;
using MacroDeckHost.Integrations.Feeds.Protocol;

namespace MacroDeckHost.Integrations.Feeds.Configuration;

internal static class FeedConfigKeys
{
	public const string Name = "name";

	public const string Url = "url";

	public const string Group = "group";

	public const string RefreshMinutes = "refreshMinutes";

	public const string IncludeKeywords = "includeKeywords";

	public const string ExcludeKeywords = "excludeKeywords";

	public const string Categories = "categories";

	public const string AllowLocalNetwork = "allowLocalNetwork";

	public const string VariableKey = "variableKey";
}

internal sealed record FeedSettings(Guid EntryId, string Title, Uri Url, string VariableKey)
{
	public const int DefaultRefreshMinutes = 30;

	public const int MinRefreshMinutes = 5;

	public const int MaxRefreshMinutes = 24 * 60;

	public const string ExampleUrl = "https://example.com/feed.xml";

	public string? Group { get; init; }

	public int RefreshMinutes { get; init; } = DefaultRefreshMinutes;

	public FeedFilter Filter { get; init; } = FeedFilter.None;

	public bool AllowLocalNetwork { get; init; }

	public TimeSpan RefreshInterval => TimeSpan.FromMinutes(RefreshMinutes);

	public static Uri? TryParseUrl(string? value)
	{
		var text = (value ?? string.Empty).Trim();
		if (text.Length == 0)
		{
			return null;
		}

		if (!text.Contains("://", StringComparison.Ordinal))
		{
			text = "https://" + text;
		}

		return Uri.TryCreate(text, UriKind.Absolute, out var url) && FeedClient.IsWebUrl(url) ? url : null;
	}

	public static int ClampRefresh(double? minutes)
	{
		return minutes is { } value && double.IsFinite(value)
			? (int)Math.Clamp(Math.Round(value), MinRefreshMinutes, MaxRefreshMinutes)
			: DefaultRefreshMinutes;
	}

	public static double? ReadNumber(object? value)
	{
		return value switch
		{
			null => null,
			double number => number,
			float number => number,
			int number => number,
			long number => number,
			decimal number => (double)number,
			JsonElement { ValueKind: JsonValueKind.Number } element => element.GetDouble(),
			JsonElement { ValueKind: JsonValueKind.String } element => ReadNumber(element.GetString()),
			string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
				=> parsed,
			_ => null
		};
	}

	public static bool ReadBool(object? value)
	{
		return value switch
		{
			bool flag => flag,
			JsonElement { ValueKind: JsonValueKind.True } => true,
			JsonElement { ValueKind: JsonValueKind.String } element => ReadBool(element.GetString()),
			string text => string.Equals(text.Trim(), "true", StringComparison.OrdinalIgnoreCase),
			_ => false
		};
	}

	public static string? ReadText(object? value)
	{
		var text = value switch
		{
			string plain => plain,
			JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
			_ => null
		};

		return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
	}
}

internal sealed record FeedFilter
{
	public static readonly FeedFilter None = new();

	public IReadOnlyList<string> Include { get; init; } = [];

	public IReadOnlyList<string> Exclude { get; init; } = [];

	public IReadOnlyList<string> Categories { get; init; } = [];

	public string Signature => string.Join('\u001f',
		string.Join('\u001e', Include),
		string.Join('\u001e', Exclude),
		string.Join('\u001e', Categories));

	public static FeedFilter Create(string? include, string? exclude, string? categories)
	{
		return new FeedFilter { Include = Split(include), Exclude = Split(exclude), Categories = Split(categories) };
	}

	public static IReadOnlyList<string> Split(string? list)
	{
		return
		[
			.. (list ?? string.Empty)
			.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Distinct(StringComparer.OrdinalIgnoreCase)
		];
	}

	public bool Matches(ParsedFeedItem item)
	{
		ArgumentNullException.ThrowIfNull(item);

		var text = $"{item.Title}\n{item.Description}";
		if (Include.Count > 0 && !Include.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase)))
		{
			return false;
		}

		if (Exclude.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase)))
		{
			return false;
		}

		return Categories.Count == 0
			|| item.Categories.Any(category => Categories.Contains(category, StringComparer.OrdinalIgnoreCase));
	}

	public bool Equals(FeedFilter? other)
	{
		return other is not null && Signature == other.Signature;
	}

	public override int GetHashCode()
	{
		return Signature.GetHashCode(StringComparison.Ordinal);
	}
}
