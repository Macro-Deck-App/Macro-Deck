using System.Text;
using MacroDeckHost.Integrations.StreamlabsDesktop;

namespace MacroDeckHost.Tests.UnitTests.StreamlabsDesktop;

[TestFixture]
public class StreamlabsTokenReaderTests
{
	[Test]
	public void ABareToken_IsTakenAsIs()
	{
		var parsed = StreamlabsTokenReader.Parse("  abc123  ");

		Assert.Multiple(() =>
		{
			Assert.That(parsed?.Token, Is.EqualTo("abc123"));
			Assert.That(parsed?.Port, Is.Null);
		});
	}

	[Test]
	public void APastedJsonBlob_YieldsTheTokenAndThePort()
	{
		var parsed = StreamlabsTokenReader.Parse("""{"token":"abc123","port":59650,"version":"1"}""");

		Assert.Multiple(() =>
		{
			Assert.That(parsed?.Token, Is.EqualTo("abc123"));
			Assert.That(parsed?.Port, Is.EqualTo(59650));
		});
	}

	[Test]
	public void ABase64QrPayload_IsDecoded()
	{
		var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"token":"from-qr","port":51234}"""));

		var parsed = StreamlabsTokenReader.Parse(payload);

		Assert.Multiple(() =>
		{
			Assert.That(parsed?.Token, Is.EqualTo("from-qr"));
			Assert.That(parsed?.Port, Is.EqualTo(51234));
		});
	}

	[Test]
	public void AnOutOfRangePortInTheBlob_IsIgnored()
	{
		var parsed = StreamlabsTokenReader.Parse("""{"token":"abc","port":0}""");

		Assert.Multiple(() =>
		{
			Assert.That(parsed?.Token, Is.EqualTo("abc"));
			Assert.That(parsed?.Port, Is.Null);
		});
	}

	[Test]
	public void JsonWithoutAToken_IsNotAConnection()
	{
		Assert.That(StreamlabsTokenReader.Parse("""{"port":59650}"""), Is.Null);
	}

	[TestCase(null)]
	[TestCase("")]
	[TestCase("   ")]
	public void NothingPasted_IsNull(string? input)
	{
		Assert.That(StreamlabsTokenReader.Parse(input), Is.Null);
	}

	[Test]
	public void ATokenThatHappensToBeValidBase64_StaysATokenWhenItIsNotJson()
	{
		var parsed = StreamlabsTokenReader.Parse("abcd");

		Assert.That(parsed?.Token, Is.EqualTo("abcd"));
	}
}
