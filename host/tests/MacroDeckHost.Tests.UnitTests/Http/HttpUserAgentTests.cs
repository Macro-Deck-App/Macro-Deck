using MacroDeckHost.Application.Network.Http;
using MacroDeckHost.Application.Services;

namespace MacroDeckHost.Tests.UnitTests.Http;

[TestFixture]
internal sealed class HttpUserAgentTests
{
	[Test]
	public void The_default_names_the_application_and_the_running_host_version()
	{
		Assert.That(HttpUserAgent.Default, Is.EqualTo($"MacroDeck/{HostVersion.Current}"));
	}

	[TestCase("MacroDeck/3.0.0-beta.15")]
	[TestCase("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36")]
	[TestCase("curl/8.5.0")]
	public void A_well_formed_value_is_accepted_unchanged(string value)
	{
		Assert.Multiple(() =>
		{
			Assert.That(HttpUserAgent.TryNormalize(value, out var normalized), Is.True);
			Assert.That(normalized, Is.EqualTo(value));
		});
	}

	[Test]
	public void Surrounding_whitespace_is_trimmed()
	{
		Assert.Multiple(() =>
		{
			Assert.That(HttpUserAgent.TryNormalize("  MacroDeck/1.0  ", out var normalized), Is.True);
			Assert.That(normalized, Is.EqualTo("MacroDeck/1.0"));
		});
	}

	[TestCase(null)]
	[TestCase("")]
	[TestCase("   ")]
	[TestCase("MacroDeck/1.0\r\nX-Injected: yes")]
	[TestCase("MacroDeck/1.0\nX-Injected: yes")]
	[TestCase("Macro\tDeck/1.0")]
	[TestCase("MacroDeck/1.0 ünïcode")]
	[TestCase("(unbalanced")]
	public void A_value_that_is_not_a_valid_header_value_is_rejected(string? value)
	{
		Assert.That(HttpUserAgent.TryNormalize(value, out _), Is.False);
	}

	[Test]
	public void A_value_over_the_maximum_length_is_rejected()
	{
		var tooLong = "A/" + new string('1', HttpUserAgent.MaxLength);

		Assert.That(HttpUserAgent.TryNormalize(tooLong, out _), Is.False);
	}
}
