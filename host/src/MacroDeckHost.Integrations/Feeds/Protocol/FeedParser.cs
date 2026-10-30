using System.Xml;
using System.Xml.Linq;

namespace MacroDeckHost.Integrations.Feeds.Protocol;

internal sealed record ParsedFeed(string? Title, IReadOnlyList<ParsedFeedItem> Items);

internal sealed record ParsedFeedItem
{
	public string? Id { get; init; }

	public string? Link { get; init; }

	public string? Title { get; init; }

	public string? Description { get; init; }

	public string? Author { get; init; }

	public DateTimeOffset? Published { get; init; }

	public IReadOnlyList<string> Categories { get; init; } = [];

	public string? ImageUrl { get; init; }
}

internal sealed class FeedParseException : Exception
{
	public FeedParseException()
		: base("The document is not an RSS or Atom feed.")
	{
	}

	public FeedParseException(string message)
		: base(message)
	{
	}

	public FeedParseException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}

internal static class FeedParser
{
	public const int MaxItems = 500;

	private const long MaxCharacters = 8 * 1024 * 1024;

	private static readonly XNamespace _atom = "http://www.w3.org/2005/Atom";
	private static readonly XNamespace _rss1 = "http://purl.org/rss/1.0/";
	private static readonly XNamespace _content = "http://purl.org/rss/1.0/modules/content/";
	private static readonly XNamespace _dc = "http://purl.org/dc/elements/1.1/";
	private static readonly XNamespace _media = "http://search.yahoo.com/mrss/";

	public static ParsedFeed Parse(byte[] content, Uri baseUrl, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(content);
		ArgumentNullException.ThrowIfNull(baseUrl);

		var root = Load(content);

		if (root.Name == _atom + "feed")
		{
			return ParseAtom(root, baseUrl, cancellationToken);
		}

		if (root.Name.LocalName == "rss" && root.Element("channel") is { } channel)
		{
			var items = Items(channel.Elements("item"), cancellationToken);
			return new ParsedFeed(FeedText.SingleLine(channel.Element("title")?.Value),
				[.. items.Select(item => ParseRssItem(item, baseUrl))]);
		}

		if (root.Name.LocalName == "RDF")
		{
			var title = root.Element(_rss1 + "channel")?.Element(_rss1 + "title")?.Value;
			var items = Items(root.Elements(_rss1 + "item"), cancellationToken);
			return new ParsedFeed(FeedText.SingleLine(title), [.. items.Select(item => ParseRssItem(item, baseUrl))]);
		}

		throw new FeedParseException();
	}

	// A feed is untrusted input: its DTD is skipped and nothing is resolved, so a feed that relies on a
	// declared entity is refused instead of expanded.
	private static XElement Load(byte[] content)
	{
		var settings = new XmlReaderSettings
		{
			DtdProcessing = DtdProcessing.Ignore,
			XmlResolver = null,
			MaxCharactersInDocument = MaxCharacters,
			MaxCharactersFromEntities = 1024,
			IgnoreComments = true,
			IgnoreProcessingInstructions = true,
			CloseInput = true
		};

		try
		{
			using var reader = XmlReader.Create(new MemoryStream(content, false), settings);
			return XDocument.Load(reader).Root ?? throw new FeedParseException();
		}
		catch (XmlException exception)
		{
			throw new FeedParseException("The feed is not well-formed XML.", exception);
		}
	}

	private static IEnumerable<XElement> Items(IEnumerable<XElement> items, CancellationToken cancellationToken)
	{
		foreach (var item in items.Take(MaxItems))
		{
			cancellationToken.ThrowIfCancellationRequested();
			yield return item;
		}
	}

	private static ParsedFeed ParseAtom(XElement feed, Uri baseUrl, CancellationToken cancellationToken)
	{
		var feedAuthor = AtomAuthor(feed);

		return new ParsedFeed(AtomText(feed.Element(_atom + "title"), true),
		[
			.. Items(feed.Elements(_atom + "entry"), cancellationToken)
				.Select(entry => ParseAtomEntry(entry, baseUrl, feedAuthor))
		]);
	}

	private static ParsedFeedItem ParseAtomEntry(XElement entry, Uri baseUrl, string? feedAuthor)
	{
		var links = entry.Elements(_atom + "link").ToList();
		var alternate = links.FirstOrDefault(link => (string?)link.Attribute("rel") is null or "alternate"
				&& ((string?)link.Attribute("type"))?.Contains("html", StringComparison.OrdinalIgnoreCase) != false)
			?? links.FirstOrDefault(link => (string?)link.Attribute("rel") is null or "alternate");
		var enclosure = links.FirstOrDefault(link => (string?)link.Attribute("rel") == "enclosure" && IsImage(link));
		var summary = entry.Element(_atom + "summary");
		var content = entry.Element(_atom + "content");
		var published = FeedDates.ParseRfc3339(entry.Element(_atom + "published")?.Value)
			?? FeedDates.ParseRfc3339(entry.Element(_atom + "updated")?.Value);

		return new ParsedFeedItem
		{
			Id = Trimmed(entry.Element(_atom + "id")?.Value),
			Link = WebUrl((string?)alternate?.Attribute("href"), baseUrl),
			Title = AtomText(entry.Element(_atom + "title"), true),
			Description = AtomText(summary, false) ?? AtomText(content, false),
			Author = AtomAuthor(entry) ?? feedAuthor,
			Published = published,
			Categories =
			[
				.. entry.Elements(_atom + "category")
					.Select(category => Trimmed((string?)category.Attribute("term")))
					.OfType<string>()
			],
			ImageUrl = WebUrl((string?)enclosure?.Attribute("href"), baseUrl)
				?? MediaImage(entry, baseUrl)
				?? WebUrl(FeedText.FirstImageSource(AtomRaw(content) ?? AtomRaw(summary)), baseUrl)
		};
	}

	private static ParsedFeedItem ParseRssItem(XElement item, Uri baseUrl)
	{
		var description = Rss(item, "description")?.Value;
		var encoded = item.Element(_content + "encoded")?.Value;
		var enclosure = Rss(item, "enclosure") is { } candidate && IsImage(candidate) ? candidate : null;
		var guid = Rss(item, "guid");

		return new ParsedFeedItem
		{
			Id = Trimmed(guid?.Value),
			Link = WebUrl(Rss(item, "link")?.Value, baseUrl) ?? PermaLink(guid, baseUrl),
			Title = FeedText.SingleLine(Rss(item, "title")?.Value),
			Description = FeedText.ToPlainText(description) ?? FeedText.ToPlainText(encoded),
			Author = FeedText.SingleLine(item.Element(_dc + "creator")?.Value)
				?? FeedText.SingleLine(Rss(item, "author")?.Value),
			Published = FeedDates.Parse(Rss(item, "pubDate")?.Value)
				?? FeedDates.ParseRfc3339(item.Element(_dc + "date")?.Value),
			Categories =
			[
				.. item.Elements()
					.Where(element => element.Name == "category"
						|| element.Name == _rss1 + "category"
						|| element.Name == _dc + "subject")
					.Select(element => FeedText.SingleLine(element.Value))
					.OfType<string>()
			],
			ImageUrl = WebUrl((string?)enclosure?.Attribute("url"), baseUrl)
				?? MediaImage(item, baseUrl)
				?? WebUrl(FeedText.FirstImageSource(encoded ?? description), baseUrl)
		};
	}

	private static string? PermaLink(XElement? guid, Uri baseUrl)
	{
		return guid is null || (string?)guid.Attribute("isPermaLink") == "false" ? null : WebUrl(guid.Value, baseUrl);
	}

	private static XElement? Rss(XElement item, string name)
	{
		return item.Element(name) ?? item.Element(_rss1 + name);
	}

	private static string? MediaImage(XElement item, Uri baseUrl)
	{
		var media = item.Elements(_media + "group").SelectMany(group => group.Elements()).Concat(item.Elements());

		foreach (var element in media)
		{
			if (element.Name == _media + "thumbnail"
				|| element.Name == _media + "content"
				&& ((string?)element.Attribute("medium") == "image" || IsImage(element)))
			{
				if (WebUrl((string?)element.Attribute("url"), baseUrl) is { } url)
				{
					return url;
				}
			}
		}

		return null;
	}

	private static bool IsImage(XElement element)
	{
		return ((string?)element.Attribute("type"))?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true;
	}

	private static string? AtomAuthor(XElement element)
	{
		return FeedText.SingleLine(element.Element(_atom + "author")?.Element(_atom + "name")?.Value);
	}

	private static string? AtomText(XElement? element, bool singleLine)
	{
		var raw = AtomRaw(element);
		return singleLine ? FeedText.SingleLine(raw) : FeedText.ToPlainText(raw);
	}

	private static string? AtomRaw(XElement? element)
	{
		if (element is null)
		{
			return null;
		}

		return (string?)element.Attribute("type") == "xhtml"
			? string.Concat(element.Nodes().Select(node => node.ToString(SaveOptions.DisableFormatting)))
			: element.Value;
	}

	private static string? WebUrl(string? value, Uri baseUrl)
	{
		if (Trimmed(value) is not { } text || !Uri.TryCreate(baseUrl, text, out var url))
		{
			return null;
		}

		return url.Scheme is "http" or "https" && string.IsNullOrEmpty(url.UserInfo) ? url.AbsoluteUri : null;
	}

	private static string? Trimmed(string? value)
	{
		return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
	}
}
