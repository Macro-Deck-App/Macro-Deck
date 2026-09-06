using MacroDeckHost.Infrastructure.Adb;

namespace MacroDeckHost.Tests.UnitTests.Adb;

public class AdbReverseListParserTests
{
	[Test]
	public void Well_formed_lines_are_parsed_into_mappings()
	{
		const string output =
			"R58M12ABCDE tcp:8193 tcp:8193\n" +
			"R58M12ABCDE tcp:8194 tcp:8194\n";

		var mappings = AdbReverseListParser.Parse(output);

		Assert.That(mappings, Has.Count.EqualTo(2));
		Assert.Multiple(() =>
		{
			Assert.That(mappings[0].Remote, Is.EqualTo("tcp:8193"));
			Assert.That(mappings[0].Local, Is.EqualTo("tcp:8193"));
			Assert.That(mappings[1].Remote, Is.EqualTo("tcp:8194"));
			Assert.That(mappings[1].Local, Is.EqualTo("tcp:8194"));
		});
	}

	[Test]
	public void A_line_with_extra_padding_between_columns_is_parsed_correctly()
	{
		const string output = "R58M12ABCDE    tcp:8193     tcp:8195\n";

		var mappings = AdbReverseListParser.Parse(output);

		Assert.That(mappings, Has.Count.EqualTo(1));
		Assert.Multiple(() =>
		{
			Assert.That(mappings[0].Remote, Is.EqualTo("tcp:8193"));
			Assert.That(mappings[0].Local, Is.EqualTo("tcp:8195"));
		});
	}

	[Test]
	public void A_garbage_line_with_fewer_than_two_tokens_is_skipped()
	{
		var mappings = AdbReverseListParser.Parse("garbage\n");

		Assert.That(mappings, Is.Empty);
	}

	[Test]
	public void Empty_output_yields_no_mappings()
	{
		var mappings = AdbReverseListParser.Parse(string.Empty);

		Assert.That(mappings, Is.Empty);
	}

	[TestCase("tcp:8193", 8193)]
	[TestCase("tcp:1", 1)]
	public void ParseTcpPort_returns_the_port_from_a_tcp_spec(string spec, int expected)
	{
		Assert.That(AdbReverseListParser.ParseTcpPort(spec), Is.EqualTo(expected));
	}

	[TestCase("localabstract:foo")]
	[TestCase("tcp:")]
	[TestCase("nonsense")]
	[TestCase("")]
	public void ParseTcpPort_returns_null_for_anything_that_is_not_a_valid_tcp_spec(string spec)
	{
		Assert.That(AdbReverseListParser.ParseTcpPort(spec), Is.Null);
	}
}
