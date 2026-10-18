using System.Security.Cryptography;
using System.Text;
using MacroDeckHost.Integrations.Feeds.Protocol;

namespace MacroDeckHost.Integrations.Feeds.State;

internal sealed record FeedItem
{
	public const int MaxTitleLength = 300;

	public const int MaxDescriptionLength = 1000;

	public const int MaxAuthorLength = 200;

	public const int MaxUrlLength = 2048;

	public const int MaxCategories = 10;

	public const int MaxCategoryLength = 100;

	public required string Key { get; init; }

	public string Title { get; init; } = string.Empty;

	public string? Url { get; init; }

	public string? Description { get; init; }

	public string? Author { get; init; }

	public DateTimeOffset? Published { get; init; }

	public IReadOnlyList<string> Categories { get; init; } = [];

	public string? ImageUrl { get; init; }

	public static FeedItem From(string key, ParsedFeedItem parsed)
	{
		ArgumentNullException.ThrowIfNull(parsed);

		return new FeedItem
		{
			Key = key,
			Title = FeedText.Clip(parsed.Title, MaxTitleLength),
			Url = BoundedUrl(parsed.Link),
			Description = FeedText.ClipOptional(parsed.Description, MaxDescriptionLength),
			Author = FeedText.ClipOptional(parsed.Author, MaxAuthorLength),
			Published = parsed.Published,
			Categories =
			[
				.. parsed.Categories.Take(MaxCategories).Select(category => FeedText.Clip(category, MaxCategoryLength))
			],
			ImageUrl = BoundedUrl(parsed.ImageUrl)
		};
	}

	public bool SameContent(FeedItem other)
	{
		ArgumentNullException.ThrowIfNull(other);

		return Key == other.Key
			&& Title == other.Title
			&& Url == other.Url
			&& Description == other.Description
			&& Author == other.Author
			&& Published == other.Published
			&& ImageUrl == other.ImageUrl
			&& Categories.SequenceEqual(other.Categories);
	}

	private static string? BoundedUrl(string? url)
	{
		return url is { Length: <= MaxUrlLength } ? url : null;
	}
}

internal static class FeedItemKeys
{
	private const int MaxKeyLength = 120;

	public static string Of(ParsedFeedItem item)
	{
		ArgumentNullException.ThrowIfNull(item);

		var key = item.Id is { } id ? "g:" + id
			: item.Link is { } link ? "l:" + link
			: "h:" + Hash($"{item.Title}|{item.Published?.ToUnixTimeSeconds()}");

		return key.Length <= MaxKeyLength ? key : key[..2] + Hash(key);
	}

	private static string Hash(string text)
	{
		return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..32];
	}
}
