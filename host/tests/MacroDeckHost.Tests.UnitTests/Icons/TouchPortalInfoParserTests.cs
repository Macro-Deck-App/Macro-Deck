using System.Text;
using MacroDeckHost.Application.Icons;

namespace MacroDeckHost.Tests.UnitTests.Icons;

[TestFixture]
public class TouchPortalInfoParserTests
{
	[Test]
	public void Parse_ReadsAllKnownKeys()
	{
		var info = Parse("name=BashTux-GICO-Misc\nauthor=BashTux\nlink=https://www.touch-portal.com\nbgcolor=#000000");

		Assert.Multiple(() =>
		{
			Assert.That(info.Name, Is.EqualTo("BashTux-GICO-Misc"));
			Assert.That(info.Author, Is.EqualTo("BashTux"));
			Assert.That(info.Link, Is.EqualTo("https://www.touch-portal.com"));
			Assert.That(info.BgColor, Is.EqualTo("#000000"));
		});
	}

	[Test]
	public void Parse_IgnoresUnknownKeysBlankLinesAndMalformedLines()
	{
		var info = Parse("\nsomething=else\nnot a pair\nName = Spaced Name \n");

		Assert.Multiple(() =>
		{
			Assert.That(info.Name, Is.EqualTo("Spaced Name"));
			Assert.That(info.Author, Is.Null);
		});
	}

	[Test]
	public void Parse_EmptyValuesStayNull()
	{
		var info = Parse("name=\nauthor=  ");

		Assert.Multiple(() =>
		{
			Assert.That(info.Name, Is.Null);
			Assert.That(info.Author, Is.Null);
		});
	}

	[Test]
	public void Parse_HandlesUtf8WithBom()
	{
		using var stream = new MemoryStream([..Encoding.UTF8.GetPreamble(), ..Encoding.UTF8.GetBytes("name=Päck")]);

		var info = TouchPortalInfoParser.Parse(stream);

		Assert.That(info.Name, Is.EqualTo("Päck"));
	}

	private static TouchPortalPackInfo Parse(string content)
		=> TouchPortalInfoParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(content)));
}
