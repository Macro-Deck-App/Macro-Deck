using System.Globalization;
using System.Text;
using MacroDeckHost.Integrations.Feeds.Protocol;

namespace MacroDeckHost.Tests.UnitTests.Feeds;

[TestFixture]
internal sealed class FeedParserTests
{
	private static readonly Uri _feedUrl = new("https://news.example.com/feed.xml");

	[Test]
	public void An_rss_item_yields_its_article_fields_as_plain_text()
	{
		const string xml = """
						   <?xml version="1.0" encoding="utf-8"?>
						   <rss version="2.0" xmlns:dc="http://purl.org/dc/elements/1.1/"
						        xmlns:content="http://purl.org/rss/1.0/modules/content/"
						        xmlns:media="http://search.yahoo.com/mrss/">
						     <channel>
						       <title>Example &amp; News</title>
						       <item>
						         <guid>urn:example:1</guid>
						         <title>Rocket &lt;b&gt;lands&lt;/b&gt;</title>
						         <link>/2026/rocket</link>
						         <description><![CDATA[<p>It <i>landed</i>.</p><script>x</script>]]></description>
						         <pubDate>Tue, 10 Jun 2025 04:00:00 EDT</pubDate>
						         <dc:creator>Ada</dc:creator>
						         <category>Space</category>
						         <category>Science</category>
						         <media:thumbnail url="https://img.example.com/rocket.jpg"/>
						       </item>
						     </channel>
						   </rss>
						   """;

		var feed = FeedParser.Parse(Encoding.UTF8.GetBytes(xml), _feedUrl);
		var item = feed.Items.Single();

		Assert.Multiple(() =>
		{
			Assert.That(feed.Title, Is.EqualTo("Example & News"));
			Assert.That(item.Id, Is.EqualTo("urn:example:1"));
			Assert.That(item.Title, Is.EqualTo("Rocket lands"));
			Assert.That(item.Link, Is.EqualTo("https://news.example.com/2026/rocket"));
			Assert.That(item.Description, Is.EqualTo("It landed."));
			Assert.That(item.Published, Is.EqualTo(new DateTimeOffset(2025, 6, 10, 8, 0, 0, TimeSpan.Zero)));
			Assert.That(item.Author, Is.EqualTo("Ada"));
			Assert.That(item.Categories, Is.EqualTo(new[] { "Space", "Science" }));
			Assert.That(item.ImageUrl, Is.EqualTo("https://img.example.com/rocket.jpg"));
		});
	}

	[Test]
	public void An_atom_entry_yields_its_alternate_link_and_dates()
	{
		const string xml = """
						   <?xml version="1.0" encoding="utf-8"?>
						   <feed xmlns="http://www.w3.org/2005/Atom">
						     <title>Atom Blog</title>
						     <author><name>Grace</name></author>
						     <entry>
						       <id>tag:example.com,2026:1</id>
						       <title type="html">Compilers &amp;amp; you</title>
						       <link rel="edit" href="https://blog.example.com/edit/1"/>
						       <link rel="alternate" type="text/html" href="https://blog.example.com/posts/1"/>
						       <link rel="enclosure" type="image/png" href="https://blog.example.com/1.png"/>
						       <updated>2026-03-01T10:00:00+01:00</updated>
						       <summary type="xhtml">
						         <div xmlns="http://www.w3.org/1999/xhtml"><p>Short <b>summary</b></p></div>
						       </summary>
						       <category term="programming"/>
						     </entry>
						   </feed>
						   """;

		var feed = FeedParser.Parse(Encoding.UTF8.GetBytes(xml), _feedUrl);
		var item = feed.Items.Single();

		Assert.Multiple(() =>
		{
			Assert.That(feed.Title, Is.EqualTo("Atom Blog"));
			Assert.That(item.Id, Is.EqualTo("tag:example.com,2026:1"));
			Assert.That(item.Title, Is.EqualTo("Compilers & you"));
			Assert.That(item.Link, Is.EqualTo("https://blog.example.com/posts/1"));
			Assert.That(item.Published, Is.EqualTo(new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero)));
			Assert.That(item.Description, Is.EqualTo("Short summary"));
			Assert.That(item.Author, Is.EqualTo("Grace"));
			Assert.That(item.Categories, Is.EqualTo(new[] { "programming" }));
			Assert.That(item.ImageUrl, Is.EqualTo("https://blog.example.com/1.png"));
		});
	}

	[Test]
	public void A_feed_with_a_public_doctype_is_parsed()
	{
		const string xml = """
						   <?xml version="1.0"?>
						   <!DOCTYPE rss PUBLIC "-//Netscape Communications//DTD RSS 0.91//EN"
						     "http://my.netscape.com/publish/formats/rss-0.91.dtd">
						   <rss version="0.91">
						     <channel>
						       <title>Netscape</title>
						       <item><title>First</title><link>https://example.com/1</link></item>
						     </channel>
						   </rss>
						   """;

		var feed = FeedParser.Parse(Encoding.UTF8.GetBytes(xml), _feedUrl);

		Assert.Multiple(() =>
		{
			Assert.That(feed.Title, Is.EqualTo("Netscape"));
			Assert.That(feed.Items.Select(item => item.Title), Is.EqualTo(new[] { "First" }));
		});
	}

	[Test]
	public void An_external_entity_is_never_resolved()
	{
		const string xml = """
						   <?xml version="1.0"?>
						   <!DOCTYPE rss [<!ENTITY secret SYSTEM "file:///etc/passwd">]>
						   <rss version="2.0"><channel><title>&secret;</title></channel></rss>
						   """;

		Assert.Throws<FeedParseException>(() => FeedParser.Parse(Encoding.UTF8.GetBytes(xml), _feedUrl));
	}

	[Test]
	public void An_inline_entity_is_never_expanded()
	{
		const string xml = """
						   <?xml version="1.0"?>
						   <!DOCTYPE rss [<!ENTITY marker "expanded-marker">]>
						   <rss version="2.0"><channel><title>&marker;</title></channel></rss>
						   """;

		var exception = Assert.Throws<FeedParseException>(()
			=> FeedParser.Parse(Encoding.UTF8.GetBytes(xml), _feedUrl));

		Assert.That(exception?.ToString(), Does.Not.Contain("expanded-marker"));
	}

	[Test]
	public void An_entity_expansion_bomb_is_refused()
	{
		const string xml = """
						   <?xml version="1.0"?>
						   <!DOCTYPE rss [
						     <!ENTITY a "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa">
						     <!ENTITY b "&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;">
						     <!ENTITY c "&b;&b;&b;&b;&b;&b;&b;&b;&b;&b;&b;&b;&b;&b;&b;&b;">
						     <!ENTITY d "&c;&c;&c;&c;&c;&c;&c;&c;&c;&c;&c;&c;&c;&c;&c;&c;">
						   ]>
						   <rss version="2.0"><channel><title>&d;</title></channel></rss>
						   """;

		Assert.Throws<FeedParseException>(() => FeedParser.Parse(Encoding.UTF8.GetBytes(xml), _feedUrl));
	}

	[Test]
	public void A_document_that_is_no_feed_is_refused()
	{
		Assert.Throws<FeedParseException>(() => FeedParser.Parse("<html><body/></html>"u8.ToArray(), _feedUrl));
	}

	[Test]
	public void A_link_that_is_no_web_address_is_dropped()
	{
		var xml = FeedXml.Rss(("1", "Trap", "javascript:alert(1)", "Mon, 01 Jun 2026 10:00:00 GMT"));

		var item = FeedParser.Parse(Encoding.UTF8.GetBytes(xml), _feedUrl).Items.Single();

		Assert.That(item.Link, Is.Null);
	}

	[TestCase("Tue, 10 Jun 2025 04:00:00 GMT", "2025-06-10T04:00:00+00:00")]
	[TestCase("10 Jun 2025 04:00 +0200", "2025-06-10T02:00:00+00:00")]
	[TestCase("Tue, 10 Jun 2025 04:00:00 PST", "2025-06-10T12:00:00+00:00")]
	[TestCase("Tue, 10 Jun 2025 04:00:00 CDT", "2025-06-10T09:00:00+00:00")]
	[TestCase("Tue, 10 Jun 25 04:00:00 UT", "2025-06-10T04:00:00+00:00")]
	[TestCase("Tue, 10 Jun 2025 04:00:00 Z", "2025-06-10T04:00:00+00:00")]
	[TestCase("Tue, 10 Jun 2025 04:00:00 K", "2025-06-10T04:00:00+00:00")]
	[TestCase("2025-06-10T04:00:00-05:00", "2025-06-10T09:00:00+00:00")]
	public void Feed_dates_are_read_with_their_zone(string text, string expected)
	{
		Assert.That(FeedDates.Parse(text), Is.EqualTo(DateTimeOffset.Parse(expected, CultureInfo.InvariantCulture)));
	}

	[Test]
	public void An_unreadable_date_is_no_date()
	{
		Assert.That(FeedDates.Parse("sometime last week"), Is.Null);
	}
}
