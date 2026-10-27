using System.Diagnostics;
using System.Text;
using MacroDeckHost.Integrations.Feeds.Protocol;

namespace MacroDeckHost.Tests.UnitTests.Feeds;

[TestFixture]
internal sealed class FeedTextTests
{
	private static readonly TimeSpan _bound = TimeSpan.FromSeconds(10);
	private static readonly Uri _feedUrl = new("https://news.example.com/feed.xml");

	private static IEnumerable<TestCaseData> HostileMarkup()
	{
		yield return new TestCaseData(Repeat("<script>", 200_000)).SetArgDisplayNames("unclosed script tags");
		yield return new TestCaseData(Repeat("<STYLE media=\"x\">", 100_000)).SetArgDisplayNames("unclosed style tags");
		yield return new TestCaseData(Repeat("<", 1_600_000)).SetArgDisplayNames("angle brackets that never close");
		yield return new TestCaseData(Repeat("<img ", 300_000)).SetArgDisplayNames("image tags without a source");
		yield return new TestCaseData(Repeat("<img alt=x ", 150_000)).SetArgDisplayNames(
			"image tags with attributes but no source");
		yield return new TestCaseData(Repeat("<br ", 300_000)).SetArgDisplayNames("line break tags that never close");
		yield return new TestCaseData(Repeat("<script>a</style>", 100_000)).SetArgDisplayNames(
			"script blocks closed by the wrong tag");
		yield return new TestCaseData(Repeat("<script>a</script><style>", 60_000)).SetArgDisplayNames(
			"closed script blocks between unclosed style tags");
		yield return new TestCaseData(Repeat("<style>a</style><script>", 60_000)).SetArgDisplayNames(
			"closed style blocks between unclosed script tags");
		yield return new TestCaseData("<br" + new string(' ', 1_000_000) + "x").SetArgDisplayNames(
			"line break tag with a long space run");
	}

	[TestCaseSource(nameof(HostileMarkup))]
	public void A_hostile_rss_description_is_parsed_in_bounded_time(string markup)
	{
		var xml = FeedXml.Rss([
			FeedXml.Item("1",
				"Title",
				"https://news.example.com/1",
				"Mon, 01 Jun 2026 10:00:00 GMT",
				$"<description><![CDATA[{markup}]]></description>")
		]);

		var item = ParseWithin(xml).Items.Single();

		Assert.That(item.Title, Is.EqualTo("Title"));
	}

	[TestCaseSource(nameof(HostileMarkup))]
	public void A_hostile_escaped_rss_title_is_parsed_in_bounded_time(string markup)
	{
		var escaped = markup.Replace("<", "&lt;", StringComparison.Ordinal);
		var xml = FeedXml.Rss([
			FeedXml.Item("1", escaped, "https://news.example.com/1", "Mon, 01 Jun 2026 10:00:00 GMT")
		]);

		Assert.That(ParseWithin(xml).Items, Has.Count.EqualTo(1));
	}

	[TestCaseSource(nameof(HostileMarkup))]
	public void A_hostile_atom_html_content_is_parsed_in_bounded_time(string markup)
	{
		var xml = $"""
				   <?xml version="1.0" encoding="utf-8"?>
				   <feed xmlns="http://www.w3.org/2005/Atom">
				     <title>Atom</title>
				     <entry>
				       <id>1</id>
				       <title>Entry</title>
				       <content type="html"><![CDATA[{markup}]]></content>
				     </entry>
				   </feed>
				   """;

		var item = ParseWithin(xml).Items.Single();

		Assert.That(item.Title, Is.EqualTo("Entry"));
	}

	[Test]
	public void An_item_with_many_hostile_categories_is_parsed_in_bounded_time()
	{
		var category = "<category>&lt;br" + new string(' ', 60_000) + "x</category>";
		var xml = FeedXml.Rss([
			FeedXml.Item("1",
				"Title",
				"https://news.example.com/1",
				"Mon, 01 Jun 2026 10:00:00 GMT",
				string.Concat(Enumerable.Repeat(category, 100)))
		]);

		var item = ParseWithin(xml).Items.Single();

		Assert.That(item.Categories, Has.Count.EqualTo(100));
	}

	[Test]
	public void Titles_with_long_whitespace_runs_collapse_in_bounded_time()
	{
		var title = "a" + new string(' ', 4_000_000) + "b";
		var xml = FeedXml.Rss([FeedXml.Item("1", title, "https://news.example.com/1", "Mon, 01 Jun 2026 10:00:00 GMT")])
			.Replace("<title>Example News</title>", $"<title>{title}</title>", StringComparison.Ordinal);

		var feed = ParseWithin(xml);

		Assert.Multiple(() =>
		{
			Assert.That(feed.Title, Is.EqualTo("a b"));
			Assert.That(feed.Items.Single().Title, Is.EqualTo("a b"));
		});
	}

	[Test]
	public void A_cancelled_parse_stops()
	{
		var xml = FeedXml.Rss(("1", "Title", "https://news.example.com/1", "Mon, 01 Jun 2026 10:00:00 GMT"));
		using var cancelled = new CancellationTokenSource();
		cancelled.Cancel();

		Assert.That(() => FeedParser.Parse(Encoding.UTF8.GetBytes(xml), _feedUrl, cancelled.Token),
			Throws.InstanceOf<OperationCanceledException>());
	}

	[Test]
	public void Text_after_a_large_inline_image_is_kept()
	{
		var html = "<img src=\"data:image/png;base64," + new string('A', 100_000) + "\"><p>Hello</p>";

		Assert.That(FeedText.ToPlainText(html), Is.EqualTo("Hello"));
	}

	[TestCase("<p>It <i>landed</i>.</p><script>x</script>", "It landed.")]
	[TestCase("<SCRIPT type=\"text/javascript\">var a = '<b>';</SCRIPT>Text", "Text")]
	[TestCase("<script>a</style>b</script>c", "c")]
	[TestCase("<script>a</script>keep<style>b{}</style> this", "keep this")]
	[TestCase("< style >p { color: red }</ style >A<br/>B", "A\nB")]
	[TestCase("Line one<br>Line two</p><p>Next</p>", "Line one\nLine two\nNext")]
	[TestCase("<div>First</div>\n\n\n\n<div>Second</div>", "First\n\nSecond")]
	[TestCase("Fish &amp; chips &lt;b&gt;", "Fish & chips <b>")]
	[TestCase("<script>alert(1)", "alert(1)")]
	[TestCase("<script>var css = '<style>';</script>Text<style>p{}</style>", "Text")]
	[TestCase("<p> </p><script>only</script>", null)]
	public void Feed_markup_becomes_its_readable_text(string html, string? expected)
	{
		Assert.That(FeedText.ToPlainText(html), Is.EqualTo(expected));
	}

	[Test]
	public void A_single_line_collapses_the_readable_text()
	{
		Assert.That(FeedText.SingleLine("<h1>Rocket</h1>\n  <b>lands</b><br>today"), Is.EqualTo("Rocket lands today"));
	}

	[TestCase("<p><img alt=\"x\" src=\"https://img.example.com/a.png\"></p>", "https://img.example.com/a.png")]
	[TestCase("<IMG width=1 SRC='b.jpg?x=1&amp;y=2'>", "b.jpg?x=1&y=2")]
	[TestCase("<img src=\"first.png\"><img src=\"second.png\">", "first.png")]
	[TestCase("<image src=\"no.png\"><img alt=\"none\">", null)]
	public void The_first_image_source_is_found(string html, string? expected)
	{
		Assert.That(FeedText.FirstImageSource(html), Is.EqualTo(expected));
	}

	[Test]
	public void A_long_ordinary_description_keeps_its_beginning()
	{
		var paragraph = Repeat("<p>The rocket landed safely.</p>", 20_000);

		var text = FeedText.ToPlainText(paragraph);

		Assert.That(text, Does.StartWith("The rocket landed safely.\nThe rocket landed safely."));
	}

	private static ParsedFeed ParseWithin(string xml)
	{
		var content = Encoding.UTF8.GetBytes(xml);
		var stopwatch = Stopwatch.StartNew();
		var parse = Task.Run(() => FeedParser.Parse(content, _feedUrl));

		Assert.That(parse.Wait(_bound), Is.True, $"Parsing did not finish within {_bound}.");
		TestContext.Progress.WriteLine($"Parsed {content.Length} bytes in {stopwatch.ElapsedMilliseconds} ms");
		return parse.Result;
	}

	private static string Repeat(string text, int count)
	{
		return new StringBuilder(text.Length * count).Insert(0, text, count).ToString();
	}
}
